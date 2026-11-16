# Copy a schematic parameter into PCB comments

Matches the PCB components of the focused project to its schematic components by designator and copies a schematic
parameter into each PCB component's Comment text. A second command writes parameters from a CSV table onto the
schematic components. The SDK lessons: walking the project's sheets, correlating objects across documents, changing
a PCB text and creating a schematic parameter with the right change notifications.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`, `EDP.IProject.DM_LogicalDocumentCount` / `EDP.IProject.DM_LogicalDocuments`,
  `EDP.IDocument.DM_DocumentKind` (`SCH`) and `EDP.IDocument.DM_FullPath`: find the project's schematic sheets.
- `DXP.IClient.OpenDocument` then `SCH.ISch_ServerInterface.GetSchDocumentByPath`: get a sheet as an
  `SCH.ISch_Document` (it must be open first). `DXP.IClient.ShowDocument` is optional and changes the active document.
- `SCH.ISch_Document.SchIterator_Create` with `SCH.TObjectId.eSchComponent`, and
  `SCH.ISch_Component.SchDesignator`: the designator of each schematic component (.NET has no `Designator`
  property).
- `SCH.ISch_Component.SchIterator_Create` with `SCH.TObjectId.eParameter`, `SCH.ISch_Parameter.Name` /
  `SCH.ISch_Parameter.Text`: read a component parameter by name.
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eComponentObject`, `PCB.IPCB_Component.Name` and
  `PCB.IPCB_Component.Comment` (both `PCB.IPCB_Text`), `PCB.IPCB_Text.Text`: the PCB side.
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` and `PCB.IPCB_Primitive.BeginModify` / `EndModify` around the
  Comment change; `DXP.IServerDocument.Modified` (`SetModified`) on the board's own server document.
- `SCH.ISch_ServerInterface.SchObjectFactory` (`eParameter`), `SCH.ISch_Component.AddSchObject`,
  `SCH.ISch_RobotManager.SendMessage` (`SCHMBeginModify`, `SCHMEndModify`, `SCHMPrimitiveRegistration`): set or
  create a hidden parameter on a component.

## Run it
1. Open a copy of the SpiritLevel-SL1 example project that ships with Altium Designer, open its `.PcbDoc` and make
   it the active document.
2. **FixPcbComponent › Import Sch Component Params**, or
   `tools/ad.ps1 -Sample FixPcbComponent run ImportSchComponentParams "File=<repo>/samples/pcb/FixPcbComponent/TestData/SpiritLevel_Params.csv|Show=false"`.
   The example table sets the parameters `SampleValue` and `SampleNote` on R12-R14, C1, C2 and R15 (all on
   `SL_Power.SchDoc`) and lists ZZ99, which the project does not have.
3. Expected: `SpiritLevel_Params.csv: 4 row(s), 7 designator(s), parameters: SampleValue, SampleNote.`, then
   `Created 12, updated 0, unchanged 0 parameter(s) on 6 component part(s); 1 of 5 sheet(s) modified.`, a line for
   `SL_Power.SchDoc` and `Not found in the project (1): ZZ99`.
4. With the PCB active: **FixPcbComponent › Fix Pcb Component** (copies `Comment`), or
   `tools/ad.ps1 -Sample FixPcbComponent run FixPcbComponent "Param=SampleValue"`.
5. Expected: `<board>.PcbDoc: updated 6 comment(s) from schematic parameter 'SampleValue'.`, the counts, and lines
   like `  R12: '<old comment>' -> 'PV-R'`. The comments on the board show the new text.

| Command | Parameters | Result |
|---|---|---|
| `FixPcbComponent` | `Param=<schematic parameter>` (default `Comment`), `DryRun=true` (optional: report only) | `<board>: updated <n> comment(s) ...`, counts (PCB components, matched, already equal, no match, no parameter, differing values) and up to 20 `designator: 'old' -> 'new'` lines |
| `ImportSchComponentParams` | `File=<.csv>` (from a menu click without it: an Open dialog), `Delimiter=,` / `;` / `tab` (default: detected from row 1), `Show=false` (default `true`: show each sheet), `DryRun=true` | Rows, designators and parameter names read; created / updated / unchanged parameter counts per sheet; designators not found; designators listed twice |

`DryRun=true` changes no object, but both commands still open the project's schematic sheets to read them.

## How it works
`FixPcbComponentCommands.cs`, `FixPcbComponent`: gets the current board and the focused project.
`ProjectSchematics.OpenSheets` walks `DM_LogicalDocuments`, keeps documents of kind `SCH`, opens each with
`OpenDocument` and gets its `ISch_Document` from `GetSchDocumentByPath`. `ProjectSchematics.Components` iterates each
sheet's components and reads `SchDesignator`. The command builds a designator → components map (case-insensitive,
first match used). If the board is no longer the current document it shows
it again, because `PreProcess` acts on the current view. Then a board iterator walks the components. For each one
whose `Name` text has a schematic match carrying the parameter (`ProjectSchematics.FindParameter`), the Comment text
is set inside `BeginModify`/`EndModify` on the text, all between `PreProcess` and `PostProcess`. The board document is
marked modified.

`ImportSchComponentParams`: `ParameterTable.Load` reads the CSV (row 1 `Designator` + parameter names, then one row
per designator list) and expands `R1-R5` and `C1,C3`. For each sheet and each listed component,
`ProjectSchematics.SetParameter` updates the parameter text (`SCHMBeginModify`/`SCHMEndModify` on the parameter) or
creates a hidden parameter at the component's location (`SchObjectFactory`, `AddSchObject`,
`SCHMPrimitiveRegistration`). All of this sits inside `SCHMBeginModify`/`SCHMEndModify` on the component. Changed
sheets are marked modified.

## Notes
- Both commands change documents in memory only. Close without saving to discard.
- Only the changed sheets and the board are marked modified.
- The .NET SDK has no spreadsheet reader, so the import reads CSV (save the sheet as CSV in Excel). Quoted cells hold
  lists such as `"C1,C2"`. Reading stops at the first row with an empty designator cell.
- The schematic parameter to copy is the `Param` parameter, with default `Comment`. When the eParameter iterator does
  not return a parameter called `Comment`, the command falls back to `SCH.ISch_Component.SchComment`. The eParameter
  iterator does return the system Comment parameter, so the fallback is only a safety net.
- Matching uses `PCB.IPCB_Component.Name` (the designator on the board). In multi-channel designs
  this differs from the schematic designator, and `PCB.IPCB_Component.SourceDesignator` would be the better key.
- A multi-part schematic component has one `SCH.ISch_Component` per part, all with the same designator. The import sets
  the parameters on every part. `FixPcbComponent` reads the first part and reports designators whose parts or
  duplicates have different values.
- The board stays the active document while the project's sheets are opened with `IClient.OpenDocument`, and the
  comments are written to it correctly. The command still re-shows the board before `PreProcess` as a precaution.
- Setting `IPCB_Text.Text` on a component comment raises no confirmation dialog (for comments without a
  variant value).
- A Comment value such as `=Value` is copied as plain text.
