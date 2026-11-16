# Report components of the focused project as a BOM text

Walks every schematic sheet of the focused project and reports each component as text. The report has the
designator, current footprint, comment, description, parameters, pins and library linkage. The sample shows how
to iterate components and their child objects with `SCH.ISch_Iterator`, and how to look a component up in its
source library with `SCH.IIntegratedLibraryManager`. A panel shows the last report and refreshes it.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` and `EDP.IWorkspace.DM_FreeDocumentsProject`: the focused project, rejected
  when it is the free documents project.
- `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: compile only when the data model is out of date.
- `EDP.IProject.DM_LogicalDocumentCount` / `EDP.IProject.DM_LogicalDocuments` with `EDP.IDocument.DM_DocumentKind`:
  the project's schematic sheets (kind `SCH`).
- `SCH.ISch_ServerInterface.GetSchDocumentByPath`: the schematic editor's object for a project sheet.
- `SCH.ISch_Iterator` with `SCH.TObjectSet` and `SCH.TIterationDepth.eIterateFirstLevel`: the components of a
  sheet, then the implementations, parameters and pins of each component. Every iterator is destroyed in a
  `finally` block.
- `SCH.ISch_Implementation.ModelType` / `IsCurrent` / `ModelName`: the current `PCBLIB` model is the footprint.
- `SCH.ISch_Component.SchDesignator`, `SchComment`, `ComponentDescription`, `SCH.ISch_Parameter.Name`,
  `SCH.ISch_Pin.Designator` and `Location`.
- `SCH.ISch_Component.VaultGUID`: tells a workspace (vault) component from a file-library component.
- `SCH.IIntegratedLibraryManager.FindComponentSymbol` and `SCH.ISch_ServerInterface.LoadComponent`: a workspace
  component's symbol and its library copy.
- `SCH.IIntegratedLibraryManager.GetComponentLocation`, `GetComponentCount`, `GetComponentName`,
  `GetParameterCount`, `GetParameterName` and `GetParameterValue`: a file-library component's parameters in its
  source library.
- `DXP.IClientAPI_Interface.BeginCursor` / `EndCursor`: a busy cursor around the work.

## Run it
The sample has a panel, so the extension must be deployed with `tools/deploy.ps1 -Sample ProjectBomReport` and
Altium Designer restarted.

1. Open a project with schematic sheets, for example a copy of the SpiritLevel-SL1 example project that ships with
   Altium Designer. Open its sheets in the schematic editor, then focus one of its documents.
2. **ProjectBomReport › Project Bom Report** (the report appears in the ProjectBomReport panel), or
   `tools/ad.ps1 -Sample ProjectBomReport run ProjectBomReport "Linkage=false"`
3. The first line is a summary, `<project>.PrjPcb: <n> component(s) on <m> schematic sheet(s) (project <compile
   state>; <k> sheet(s) skipped; linkage on|off).`, followed by one block per sheet and component.

| Command | Parameters | Result |
|---|---|---|
| `ProjectBomReport` | `Linkage=true\|false` (default `true`), `Out=<file or folder>` (optional) | The report. From a menu or the panel, it is shown in the panel instead. With `Out`, it is also written to the file, or to `<project>.BOM.txt` in the folder |
| `Panel.Show` | none | Opens the ProjectBomReport panel, which shows the last report and has a **Refresh** button and a **Library linkage** check box |

## How it works
`ProjectBomReportCommands.cs` gets the workspace and the focused project. It refuses free documents, and fails
with a message if the schematic server isn't running. It compiles the project if `DM_NeedsCompile` says so, then
goes through all logical documents of kind `SCH`. It gets each sheet from `GetSchDocumentByPath`. A sheet the
schematic server doesn't know is listed as skipped. A component iterator walks each sheet. For every component,
`Implementations` collects the first-level `eImplementation` children (checking `ObjectId`, since an `as` cast
succeeds for any object) to find the current `PCBLIB` model. `ReportParameters` and `ReportPins` iterate the
component's first-level parameters and pins. `ReportLinkage` loads the `IIntegratedLibraryManager`. For a
component with a `VaultGUID`, it calls `FindComponentSymbol` and `LoadComponent` and lists the library copy's
parameters. For any other component, it finds the component in its source library with `GetComponentLocation`
and lists that copy's parameters by index. The report is stored as a string in AppDomain data, so it survives a
hot reload. `Ui/PanelContent.cs` shows it and runs the command again through the shim when **Refresh** is
clicked.

## Notes
- Only sheets the schematic server has loaded are reported. `GetSchDocumentByPath` returns null for a sheet of an
  open, compiled project that is not open in the schematic editor, although `DM_LogicalDocuments` lists it. Such
  sheets are listed as skipped: open them and run the report again.
- Pin locations are printed as `SCH.ISch_Pin.Location` returns them, unconverted. These are internal units of
  1/10,000 mil, like PCB coordinates (pins on a 50 mil grid come out as multiples of 500,000).
- For a component from a file library, `GetComponentLocation` returns the full path of the library file the
  project uses (for a project copy, the copy's own `.SchLib`).
- The workspace branch (`VaultGUID` set) needs a signed-in workspace and a component placed from it. Whether the
  component that `LoadComponent` returns needs releasing is not documented.
- The sample has no custom document kind of its own: the shared shim's `NewDocumentInstance` always returns null
  and cannot be overridden by a sample. The panel and `Out=` take the place of a document view and its file.
- `Out=` must name an existing folder, or a file in one. The sample does not create folders.
- The sample has `Panel.Show` but no command to hide the panel; close it in AD.
- Read only: the command changes no design data. A compile updates only the project's in-memory data model.
