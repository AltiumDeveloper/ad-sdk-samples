# List all physical designators of a flattened project

Lists the physical designator of every component instance of the focused project, read from the project's flattened
document, where all sheets of the hierarchy are merged into one compiled view. It shows how to compile a project only
when needed and how to enumerate the components of the compiled model.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`: the project of the focused document. The workspace comes from
  `DXP.GlobalVars.Client.GetDXPWorkspace()`, which returns `DXP.IDXPWorkSpace`: cast it to `EDP.IWorkspace`.
- `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: compile only when the compiled model is stale.
  `DM_Compile` returns `false` on failure.
- `EDP.IProject.DM_DocumentFlattened`: the flattened document, one compiled view of all sheets of the project.
- `EDP.IDocument.DM_ComponentCount` / `EDP.IDocument.DM_Components`: a count plus an indexed accessor for the
  component instances.
- `EDP.IPart.DM_PhysicalDesignator` and `EDP.IPart.DM_LogicalDesignator`: the designator of the instance and the
  designator drawn on the sheet, called on the `EDP.IComponent` returned by `DM_Components` (it derives from
  `EDP.IPart`). They differ in multichannel designs (see Notes).
- `EDP.IDMObject.DM_OwnerDocumentName`: the sheet a component comes from.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument`: optionally open the report as a text document in AD.

## Run it
1. Open a schematic project (for example the SpiritLevel-SL1 example project that ships with Altium Designer) and
   focus one of its sheets, so that it is the focused project in the Projects panel.
2. **HierarchicalDesign › Get All Components** (schematic or PCB editor), or
   `tools/ad.ps1 -Sample HierarchicalDesign run GetAllComponents`
3. The result starts with `<project>.PrjPcb: <n> components in the flattened document (compiled first).` (or
   `(already compiled).`), followed by one line per component such as `  C1 (logical C1, sheet SL_Power.SchDoc)`,
   up to `Max` lines.

| Command | Parameters | Result |
|---|---|---|
| `GetAllComponents` | `Out=<file or folder>` (optional: write all physical designators, one per line, to the file, or to `Project_Report.Txt` in the folder), `Open=true` (optional: write the report, to the temp folder when `Out` is not given, and open it in AD), `Max=<n>` (designators listed in the result, default 50) | The count and designator lines; with `Out`/`Open` also `Report written to <path>` |

## How it works
`HierarchicalDesignCommands.cs`: `GetAllComponents` casts the client's DXP workspace to `EDP.IWorkspace` and takes
its focused project. If `DM_NeedsCompile` is true it calls `DM_Compile` and remembers a `false` result for a
warning. It then takes `DM_DocumentFlattened` and loops from 0 to `DM_ComponentCount` over `DM_Components(i)`,
collecting `DM_PhysicalDesignator` for each instance and, for the first `Max`, a line with `DM_LogicalDesignator`
and `DM_OwnerDocumentName`. `WriteReport` writes the designators with `File.WriteAllLines` when `Out` or `Open` is
given, and with `Open` calls `OpenDocument("Text", path)` and `ShowDocument`.

## Notes
- Read-only: compiling refreshes the compiled model but changes no design document. A large project may take a while
  to compile; the command logs when it starts compiling.
- The physical designator is the annotated designator of the component instance, with no sheet path prefix. In the
  flat SpiritLevel-SL1 project all 117
  physical designators equal the logical ones. In the Bluetooth Sentinel example a sheet used six times gave logical
  `LED1` / `R1` and physical `LED1`..`LED6` / `R3`..`R8`, and many other components were renumbered (logical `C14`,
  physical `C5`). How the channel designators are formed depends on the project's multichannel and annotation
  settings. `DM_OwnerDocumentName` returns the sheet file name without a path (for a repeated sheet, that sheet's
  own file name).
- `DM_NeedsCompile` returns false right after a project is opened.
- A project without schematic sheets, or one whose compile fails, may have no flattened document: the command then
  raises an error.
- The report is written only on request, to the temp folder by default, not to the all-user application data
  folder (ProgramData), which may not be writable.
- No `EDP.IUnifiedDataModel.BeginRead` bracket is used, as in the docs' design-data guide.
