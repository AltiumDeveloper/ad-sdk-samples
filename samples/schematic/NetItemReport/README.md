# Report all nets and net items of the compiled project

Compiles the focused project and writes a report of every net in its flattened document: the net's properties and
the identifier of each of its net items. It shows how to read nets from the compiled data model, and how to resolve
a net item's schematic handle to the live schematic object with the unified data model.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` and `EDP.IProject.DM_Compile`: the nets exist only in the compiled model, so
  the project is compiled first.
- `EDP.IProject.DM_DocumentFlattened`: the flattened document holds the nets of the whole project
  (`EDP.IDocument.DM_NetCount` / `EDP.IDocument.DM_Nets`).
- `EDP.INet`: `DM_NetNumber`, `DM_NetName`, `DM_FullNetName`, `DM_ElectricalString`, `DM_SignalType`,
  `DM_CalculatedNetName`, `DM_HiddenNetName`, and the three item lists `DM_AllNetItems`, `DM_RemovedNetItems`,
  `DM_RemovedHarnessEntries`, each a count method plus an indexed accessor.
- `EDP.IProject.DM_DataModel` with `EDP.IUnifiedDataModel.BeginRead` / `EndRead`: a read bracket around all access
  to the compiled model, closed in a `finally`.
- `EDP.IUnifiedDataModel.SupportsInterface`: turns the handle string of `EDP.IDMObject.DM_SchHandle` into the
  schematic object (an `out object`), read here as `SCH.ISch_BasicContainer.IdentifierString`.
- `DXP.IClient.OpenDocument` / `ShowDocument`: optionally open the report as a text document in Altium Designer.

## Run it
1. Open a schematic project (for example the SpiritLevel-SL1 example project that ships with Altium Designer) and
   focus one of its documents, so that it is the focused project in the Projects panel. Open all of its sheets if
   you want every net item resolved (see Notes).
2. **NetItemReport › Get Net Info** (schematic editor), or `tools/ad.ps1 -Sample NetItemReport run GetNetInfo`
3. The returned text gives the number of nets and net items, the first 15 nets with their item counts, and the path
   of the report (`NetItemReport_<project>.txt` in the temp folder, or in `Out`). With `Open=true` the report opens
   in Altium Designer.

| Command | Parameters | Result |
|---|---|---|
| `GetNetInfo` | `Out=<folder>` (default: temp folder), `Open=true` (open the report, default off), `Compile=false` (skip compiling, default on) | `<project>: <n> nets, <m> net items, ...; <r> resolved to schematic objects, <u> not resolved.`, per-net lines, `Report: <path>` |

## How it works
`NetItemReportCommands.cs` gets the workspace from `DXP.GlobalVars.Client.GetDXPWorkspace()` and its focused
project, compiles it (unless `Compile=false`), and gets the project's data model. Inside
`BeginRead`/`EndRead` it takes the flattened document and walks its nets. For each net it writes the net
properties, then the three item lists. `ReportItems` handles one list. For each item it passes `DM_SchHandle()` to
`SupportsInterface`, casts the returned object to `SCH.ISch_BasicContainer` and reads `GetState_IdentifierString()`.
Items whose handle is empty or does not resolve are written as "(no schematic object, <sheet>)" and counted as not resolved. When any are left, the summary adds a hint to open the sheets.
After `EndRead` the report is written with `File.WriteAllLines` and, with `Open=true`, opened with
`OpenDocument("Text", ...)` and `ShowDocument`.

## Notes
- Read-only: compiling refreshes the compiled model but changes no design document.
- The cast to `SCH.ISch_BasicContainer` always succeeds, because SDK interfaces are dispatch interfaces (see the
  guide "Interfaces at run time"). A non-schematic object would fail only when `GetState_IdentifierString` is called,
  so that call is wrapped and a `COMException` counts the item as not resolved.
- `DM_Compile` returns `false` on failure: the sample then prefixes a warning.
- **Open the sheets.** `SupportsInterface` resolves a handle only when the item's sheet is open in an editor; on
  closed sheets it returns false. Focusing one sheet of a five-sheet project resolves only that sheet's items.
  `IDMObject.DM_SCHObjectInterface` is no alternative: it returns null for every net item.
- On SpiritLevel-SL1: `DM_SchHandle` is never empty (`<sheet id>\<object id>`). The
  removed net items are the ports and sheet entries that flattening merged away. `DM_CalculatedNetName` equals
  `DM_FullNetName` (the reference wrongly says "the bus width"). `DM_SignalType` and `DM_HiddenNetName` are empty.
- The reference gives no description for `IUnifiedDataModel` or `DM_SchHandle`.
- A large project can take a while to compile; progress is logged every 50 nets.
