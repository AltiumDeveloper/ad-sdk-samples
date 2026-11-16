# Compile the project and count a sheet's child documents

Compiles the project of the focused schematic sheet and reports how many child documents the sheet has in the
project's sheet hierarchy, with their names. It shows how to reach the workspace data model from a document, compile
a project from .NET, and read a document's place in the hierarchy.

## What it shows
- `EDP.IWorkspace.DM_FocusedDocument`: the active document as a data model object (null when nothing is open).
- `EDP.IDocument.DM_DocumentKind`: the document kind string, `SCH` for a schematic sheet.
- `EDP.IDocument.DM_Project`: the project the document belongs to.
- `EDP.IWorkspace.DM_FreeDocumentsProject`: the pseudo project of documents that belong to no project.
- `EDP.IProject.DM_Compile`: compiles the project from .NET, without a
  `WorkspaceManager:Compile` process call. Returns a bool.
- `EDP.IDocument.DM_ChildDocumentCount` / `EDP.IDocument.DM_ChildDocuments`: the sheet's child documents in the
  compiled hierarchy, by index.

## Run it
1. Open SpiritLevel-SL1, an example project that ships with Altium Designer, and make its top sheet `SL1 Xilinx Spartan-IIE PQ208 Rev1.02.SchDoc`
   the active document.
2. **SchChildDocumentCount › Count Child** (schematic editor menu), or
   `tools/ad.ps1 -Sample SchChildDocumentCount run CountChild`
3. The result starts with `<sheet> (project <project>): <n> child documents (compiled; before the compile: <m>).`,
   followed by one indented line per child document. The top sheet reports its 4 child sheets
   (`SL_Config_2E.SchDoc`, `SL_FPGA_Auto_2E.SchDoc`, `SL_LCD_SW_LED_2E.SchDoc`, `SL_Power.SchDoc`), matching the 4
   sheet symbols on the sheet, and the leaf sheet `SL_Power.SchDoc` reports 0.

| Command | Parameters | Result |
|---|---|---|
| `CountChild` | `Compile=false` or `Compile=0` (optional: skip the compile; any other value, and the default, compiles) | The child document count of the focused schematic before and after the compile, and the child document names |

## How it works
`SchChildDocumentCountCommands.cs`: `CountChild` casts `DXP.IClient.GetDXPWorkspace` to `EDP.IWorkspace`, reads
`DM_FocusedDocument` and requires `DM_DocumentKind` to be `SCH` (otherwise a `UserException`). It takes the document's
project from `DM_Project` and compares its `DM_ProjectFullPath` with that of `DM_FreeDocumentsProject` to add a note
for free documents. It reads `DM_ChildDocumentCount` once, calls `DM_Compile` (unless `Compile=false`), queries the
focused document again, and reads the count a second time. The child names come from
`DM_ChildDocuments(i).DM_FileName()`. Nothing is modified or saved.

## Notes
- The process call `MessageRouter_SendCommandToModule('WorkspaceManager:Compile', ...)` has no .NET
  counterpart by that name. `EDP.IProject.DM_Compile` is the .NET way, as in the docs' `SCH.IConnection` example.
- The command compiles the document's own project, which is normally the focused project.
- `DM_ChildDocumentCount` is already valid *before* any compile: on the freshly opened SpiritLevel project the top
  sheet reports its 4 child documents with their names before `DM_Compile` is called, and the same values after it.
  The example project folder ships a `.PrjPcbStructure` file; for a never-compiled project without one, the command
  prints the count before and after the compile, so a run shows any difference.
- `DM_Compile` returns true, runs synchronously, opens no dialog and leaves the documents unmodified.
- If `DM_Compile` returns false, the result says so and the count may be stale.
