# List each net's component pins via the data model

Walks every net of the focused project's flattened document and reports the component pins on each one as
`<component designator>-<pin designator>`. It shows how to follow a net's pin items (`EDP.INet.DM_Pins`) to the live
`SCH.ISch_Pin` through the project's unified data model, and how to get from that pin to its component's designator.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` with `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: compile the
  project only when it needs it, so its nets are up to date.
- `EDP.IProject.DM_DocumentFlattened`: the nets of the whole project (`EDP.IDocument.DM_NetCount` /
  `EDP.IDocument.DM_Nets`, `EDP.INet.DM_NetName`).
- `EDP.INet.DM_PinCount` / `EDP.INet.DM_Pins`: only the pin items of a net, not its other net items.
- `EDP.IProject.DM_DataModel` with `EDP.IUnifiedDataModel.BeginRead` / `EndRead` around all reads of the compiled
  model, and `EDP.IUnifiedDataModel.SupportsInterface` to turn `EDP.IDMObject.DM_SchHandle` into the schematic object.
- `SCH.ISch_BasicContainer.ObjectId` compared with `SCH.TObjectId.ePin`: the object kind decides whether the object is
  a pin, because casting to an SDK interface always succeeds.
- `SCH.ISch_Pin.OwnerSchComponent` and `SCH.ISch_Pin.Designator`, plus `SCH.ISch_Component.SchDesignator` and
  `SCH.ISch_Designator.Text` for the component's designator text (the .NET API has no `ISch_Component.Designator`).
- `DXP.IClient.OpenDocument` / `ShowDocument`: optionally open the report as a text document in Altium Designer.

## Run it
1. Open a schematic project (for example the SpiritLevel-SL1 example project that ships with Altium Designer), open
   its schematic sheets, and focus one of them so that the project is the focused project. Pins resolve only on
   sheets that are open in an editor (see Notes).
2. **NetPinConnection › Get Pin Connection** (schematic editor), or
   `tools/ad.ps1 -Sample NetPinConnection run GetPinConnection`
3. The returned text gives the number of nets and pin items, how many pins resolved, the first 15 nets with their
   pins (for example `GND: C1-2 U1-4 ...`) and the path of the report (`NetPinConnection_<project>.txt` in the temp
   folder, or in `Out`). With `Open=true` the report opens in Altium Designer.

| Command | Parameters | Result |
|---|---|---|
| `GetPinConnection` | `Out=<folder>` (default: temp folder), `Open=true` (open the report, default off) | `<project>: <n> nets, <m> pin items, <r> resolved to schematic pins, <u> not resolved.`, per-net pin lines, `Report: <path>`. The summary ends with `(compiled first)` only when `DM_NeedsCompile` was true |

## How it works
`NetPinConnectionCommands.cs` gets the workspace from `DXP.GlobalVars.Client.GetDXPWorkspace()` and its focused
project, compiles it if `DM_NeedsCompile()` is true, and gets the project's data model. Inside one
`BeginRead`/`EndRead` bracket it takes the flattened document and walks its nets. For each net it walks
`DM_Pins(j)` and calls `PinText`. `PinText` passes `DM_SchHandle()` to `SupportsInterface`, checks
`GetState_ObjectId()` for `ePin`, casts to `ISch_Pin`, and joins `OwnerSchComponent().GetState_SchDesignator().GetState_Text()`
and `GetState_Designator()` with a dash. Each net becomes a block of lines in this format: `(`, the net
name, one line per resolved pin, `)`. After `EndRead` the report is written with `File.WriteAllLines` and, with
`Open=true`, opened with `OpenDocument("Text", ...)` and `ShowDocument`.

## Notes
- Read-only: compiling refreshes the compiled model but changes no design document.
- Pins that do not resolve (empty handle, `SupportsInterface` returns false, not a pin, or a `COMException`) are left
  out of the net's block, but they are counted and the summary then adds a hint. `SupportsInterface` returns false
  for items on a sheet that is not open in an editor (see the NetItemReport sample), so expect unresolved pins
  there. With all five sheets of SpiritLevel-SL1 open, all 599 pins resolve.
- A pin without an owner component would be reported as `?-<pin>`. `OwnerSchComponent` is non-null for all 599 pins
  of SpiritLevel-SL1, so the fallback is defensive.
- `DM_NeedsCompile()` returns false for a freshly opened, unedited project: its compiled model is already current,
  so `DM_Compile()` is not called (the reference has no description for it).
- Nets without component pins, such as a bus net, produce an empty block.
- Multi-part components: the designator comes from the component's `SchDesignator` text, so the pins of all parts
  of a component carry that text.
- A large project can take a while to compile; progress is logged every 50 nets.
