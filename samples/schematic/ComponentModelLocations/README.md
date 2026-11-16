# Report library and model file locations of sheet parts

Compiles the project of the focused schematic sheet and, for every part on it, asks the integrated library manager
where the part's symbol and the data files of each of its models (footprints, 3D, simulation models) are found. It
shows how to walk parts and implementations in the design model and resolve their library locations.

## What it shows
- `EDP.Utils.LoadIntegratedLibraryManager`: get the `SCH.IIntegratedLibraryManager` (namespace `SCH`, handed out by
  `EDP.Utils`).
- `EDP.IWorkspace.DM_FocusedDocument`, `EDP.IDocument.DM_Project`, `EDP.IProject.DM_Compile`: compile the sheet's
  project so the design model has its parts.
- `EDP.IDocument.DM_PartCount` / `EDP.IDocument.DM_Parts`: the parts of a sheet in the design model.
- `SCH.IIntegratedLibraryManager.GetComponentLocation` with `EDP.IPart.DM_SourceLibraryName` and
  `EDP.IPart.DM_LibraryReference`: where the symbol is found; the `ref` argument returns the top-level library.
- `EDP.IPart.DM_ImplementationCount` / `EDP.IPart.DM_Implementations`: the models (`EDP.IComponentImplementation`)
  linked to a part, with `DM_ModelName`, `DM_ModelType`, `DM_Description` and their parameters
  (`EDP.IDMObject.DM_Parameters`).
- `EDP.IComponentImplementation.DM_IntegratedModel` chooses the lookup:
  `SCH.IIntegratedLibraryManager.GetComponentDatafileLocation` for a model of an integrated library,
  `SCH.IIntegratedLibraryManager.FindDatafileInStandardLibs` with `DM_DatafileEntity`, `DM_DatafileKind` and
  `DM_DatafileLocation` otherwise.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument`: open the written report as a text document.

## Run it
1. Open a project (the SpiritLevel-SL1 example project that ships with Altium Designer works) and focus one of its
   schematic sheets, for example `SL_Power.SchDoc`.
2. **ComponentModelLocations › Part Library Report**, or `tools/ad.ps1 -Sample ComponentModelLocations run PartLibraryReport`
3. The result starts with `SL_Power.SchDoc (compiled): <p> parts, <i> implementations, <d> data files (<r> located, <u> not located).`,
   then one block per part (`Designator:`, part type, library reference, source library, component and top-level
   location) with one `Model <n>:` block per model, its parameters and its data file locations. An empty location is
   shown as `<empty>`.

| Command | Parameters | Result |
|---|---|---|
| `PartLibraryReport` | `Out=<file or folder>` (optional), `Open=true` (optional: write the report, to a temporary file when `Out` is not given, and open it in AD), `Compile=false` (optional; default `true`: compile the sheet's project first) | The report as text; with `Out`/`Open` also `Report written to <path>` |

## How it works
`ComponentModelLocationsCommands.cs`: `PartLibraryReport` gets the workspace as `EDP.IWorkspace`, takes the focused
document and checks that its `DM_DocumentKind` is `SCH`. Unless `Compile=false`, it calls `DM_Compile` on the sheet's
`DM_Project`, reports the result and queries the focused document again. It loads the integrated library manager once
and walks `DM_Parts`. For each part it calls `GetComponentLocation(DM_SourceLibraryName, DM_LibraryReference, ref
topLevel)`, then walks the part's implementations: model name, type, description, parameters, and for each data file
either `GetComponentDatafileLocation(index, model name, model type, library reference, component location, ref found)`
(integrated model) or `FindDatafileInStandardLibs(entity, kind, location, true, ref found)`. Both the return value and
the `ref` output are printed. `Out` writes the same lines to a file and `Open` opens it in AD.

## Notes
- Parts and implementations come from the design model. A sheet outside a project, or with `Compile=false` before
  the first compile, may report no parts; the command then raises an error that says so. With nothing focused it
  raises `No document is focused`.
- The command only reads: it compiles the project (unless `Compile=false`) but changes no document.
- **The lookups can come back empty.** `GetComponentLocation` and `FindDatafileInStandardLibs` return an empty
  string and leave the `ref` output (`argFoundInLibraryPath`) empty for all parts and data files of
  SpiritLevel-SL1, and also for a component and footprint of an installed integrated library
  (`Miscellaneous Devices.IntLib` / `Res1` / `AXIAL-0.3`), with the library passed as a name or as a full path.
- `DM_IntegratedModel()` is false for every implementation of SpiritLevel-SL1 (its parts come from project
  `.SchLib`/`.PcbLib`/`.PCB3DLib` files), so that project does not reach the `GetComponentDatafileLocation` branch.
- `DM_ModelType()` and `DM_DatafileKind(k)` spell the same kind differently: `PCBLIB` versus `PCBLib`. Compare
  them case-insensitively.
- The component location returned by `GetComponentLocation` is passed as `argComponentLibraryName` of
  `GetComponentDatafileLocation`, and `argForComponentInstance` of `FindDatafileInStandardLibs` is `true`. The
  reference describes that argument as "True if it is in an integrated library", which does not match its use here
  for non-integrated models.
- `EDP.IProject.DM_Compile` compiles the project from .NET and returns a bool, which the report includes.
