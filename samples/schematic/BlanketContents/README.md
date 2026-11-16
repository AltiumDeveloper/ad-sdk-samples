# List the items grouped by each blanket on a sheet

For every blanket of the focused schematic sheet, reports the lines, sheet symbols and net items that the compiled
project data model assigns to it. It shows how to read blankets from the EDP data model and resolve their items'
schematic handles to live SCH objects through the unified data model.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` / `EDP.IWorkspace.DM_FocusedDocument`: the project and sheet the user works on.
- `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: blankets exist only in the compiled model, so compile
  when the project is out of date.
- `EDP.IProject.DM_DataModel` with `EDP.IUnifiedDataModel.BeginRead` / `EDP.IUnifiedDataModel.EndRead`: one read
  bracket around every access to the compiled model.
- `EDP.IDocument.DM_BlanketCount` / `EDP.IDocument.DM_Blankets`: the blankets of a sheet.
- `EDP.IBlanket.DM_Lines`, `EDP.IBlanket.DM_SheetSymbols`, `EDP.IBlanket.DM_NetItems` (with their `...Count`
  methods): what a blanket covers.
- `EDP.IDMObject.DM_SchHandle` and `EDP.IUnifiedDataModel.SupportsInterface`: from a data-model item to the live
  schematic object.
- `SCH.ISch_BasicContainer.ObjectId` (`SCH.TObjectId.eWire`, `SCH.TObjectId.eBus`): tell the resolved objects apart
  by object ID, not by a type test.
- `SCH.ISch_ServerInterface.SchObjectFactory` with `SCH.TObjectId.eBlanket`, `SCH.ISch_Polygon.VerticesCount` /
  `SCH.ISch_Polygon.Vertex` and `SCH.ISch_Document.RegisterSchObjectInContainer`: create a blanket (helper command).

## Run it
1. Open a project and focus one of its schematic sheets that has a blanket. If the sheet has none, place one by
   hand (**Place › Directives › Blanket**).
2. **BlanketContents › Show Blanket**, or `tools/ad.ps1 -Sample BlanketContents run ShowBlanket`
3. The result starts with `<sheet> (kind '<kind>'): <n> blanket(s)` (plus `(project compiled)` when the command
   compiled), followed by one block per blanket with the counts of lines (wires, buses, other), sheet symbols and
   net items, and the object kinds found.

The helper **BlanketContents › Add Test Blanket** (`tools/ad.ps1 -Sample BlanketContents run AddTestBlanket`) places
a blanket through the SCH API. That blanket is visible on the sheet but is **not**
reported by `ShowBlanket`: the count stays `0 blanket(s)`, also with `Compile=force` and after a save. Use a
hand-placed blanket to see the per-blanket output.

| Command | Parameters | Result |
|---|---|---|
| `ShowBlanket` | `Compile=false` / `Compile=force` (optional; default compiles when `DM_NeedsCompile` says so, `force` compiles always) | `<sheet> (kind '<kind>'): <n> blanket(s)`; per blanket `Lines: <n> (<w> wires, <b> buses, <o> other, <u> not resolved) [eWire=..]`, `Sheet symbols: ...`, `Net items: ...` |
| `AddTestBlanket` | `Rect=x1,y1,x2,y2` (mils, optional), `Margin=<mils>` (default 100) | `Placed a blanket on <sheet>: 4 vertices (x,y) ... mil.` By default the rectangle encloses all wires of the sheet plus the margin. |

## How it works
`BlanketContentsCommands.cs`, `ShowBlanket`: gets the workspace from `DXP.GlobalVars.Client`, then the focused
project, and compiles it when `DM_NeedsCompile` says so (unless `Compile=false`). Inside
`BeginRead`/`EndRead` it takes the focused document, checks that its kind is `SCH`, and walks
`DM_Blankets`. For each line, sheet symbol and net item it calls `Resolve`: `DM_SchHandle`, then
`SupportsInterface`, then `GetState_ObjectId` on the object cast to `SCH.ISch_BasicContainer`. A small `Tally`
counts the results by object ID, and the report is returned as text.

`AddTestBlanket` gets the current sheet from `SCH.GlobalVars.SchServer`, computes a rectangle (the `Rect`
parameter, or the union of all wires' `BoundingRectangle` grown by `Margin`), creates a blanket with
`SchObjectFactory`, sets four vertices, and adds it with `RegisterSchObjectInContainer` and the robot message
`SCHMPrimitiveRegistration`, between `IProcessControl.PreProcess` and `PostProcess`. It returns the corners read
back from the blanket.

## Notes
- Blankets are read from the compiled model, so `Compile=false` on an out-of-date project may report an old state
  (the summary says when the command compiled). `DM_NeedsCompile()` can return false for a project with an edited
  sheet; use `Compile=force` when in doubt.
- A blanket created through the SCH API (`AddTestBlanket`) does not appear in the compiled model, even after
  `DM_Compile` returns true and the sheet is saved. A blanket placed by hand, also with no directive inside it, is
  reported, without any compile. The cause of the difference is not known.
- `ISch_Polygon.Vertex` is 1-based.
- An item counts as "not resolved" when its handle is empty, `SupportsInterface` returns false or no object, or the
  object raises a `COMException`. Handles resolve only for sheets open in an editor (see the NetItemReport sample).
  On the open, focused sheet all blanket items resolve.
- `DM_Lines` is not limited to wires and buses. On a harness sheet all lines are `eSignalHarness`, and they count
  as "other". Sheet symbols resolve to `eSheetSymbol`, and the net items there are the `eSheetEntry` objects of
  those sheet symbols.
- The document check compares `EDP.IDocument.DM_DocumentKind` with `SCH`, case-insensitively. The .NET API has no
  `cDocKind_SCH` constant; the value `SCH` comes from the
  reference of `DM_DocumentKind`. The report prints the kind it saw.
- `AddTestBlanket` changes the sheet in memory. Close it without saving to discard the blanket.
