# Export a schematic or project via an external DLL

Hands the focused project, or the active schematic sheet or schematic library, to a third-party native exporter
(`SIMetrixExporter.DLL`) by loading the DLL at run time and calling its `ExportToSimetrix` export. It shows how to
resolve the focused project, the active view's document and its kind, and how to pass an Altium Designer document
to native code.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` / `EDP.IWorkspace.DM_FreeDocumentsProject`: the focused project, and how to
  reject the Free Documents pseudo project.
- `EDP.IProject.DM_ProjectFullPath`: the project file's full path.
- `DXP.IClient.GetDocumentByPath`: the project's own server document, looked up by that path.
- `DXP.IClient.CurrentView` / `DXP.IServerDocumentView.GetOwnerDocument`: the document of the active view (the view
  AD passed to the process is used first, when there is one).
- `DXP.IServerDocument.GetKind`: `SCH` or `SCHLIB` selects the export format (`SXSCH`, `SXLIB`).
- `DXP.IServerDocument.FileName`: the source file, used for the default target name.
- Passing an SDK object to native code: the SDK interfaces are dispatch interfaces, so the document goes to the DLL
  as an `IDispatch` pointer (`Marshal.GetIDispatchForObject`), or as the interface asked for with `DocumentIid`.

## Run it
1. Open a project (for example an example project that ships with Altium Designer, such as SpiritLevel-SL1) and
   open one of its documents, so the project is focused. For the file export, make a `.SchDoc` or `.SchLib` the
   active document. For an actual export, put `SIMetrixExporter.DLL` (from the SIMetrix/SIMPLIS integration
   package, not part of this sample) next to the extension, or pass `Dll=<path>`.
2. **SimetrixExporter › Export Project To Simetrix** or **SimetrixExporter › Export File To Simetrix** (schematic
   editor menu), or `tools/ad.ps1 -Sample SimetrixExporter run ExportFileToSimetrix "Out=<folder>"`
3. With the DLL: `ExportToSimetrix('<target>', '<format>', <source>) returned True.` (or an error when it returns
   False). Without the DLL: `Cannot export <source> as <format> to '<target>': SIMetrixExporter.DLL not found
   (looked in: <folders>). ...`, which still shows the resolved document, format and target.

| Command | Parameters | Result |
|---|---|---|
| `ExportProjectToSimetrix` | `Out=<folder>` (default: the temp folder; created if missing), `Dll=<file or folder>` (default: the extension's folder), `DocumentIid=<guid>` (optional) | Calls the DLL with target `<Out>\SXPRJ`, format `SXPRJ` and the project document; returns the DLL's result |
| `ExportFileToSimetrix` | `Out=<file or folder>` (default: `<temp>\<source name>.sxsch` or `.sxlib`), `Dll=<file or folder>`, `DocumentIid=<guid>` (optional) | Calls the DLL with the target, `SXSCH` or `SXLIB` and the active document; returns the DLL's result |

## How it works
`Commands/SimetrixExporterCommands.cs`: `ExportProjectToSimetrix` gets the workspace, its focused project (not the
Free Documents project), the project's full path and the project's server document from `GetDocumentByPath`. The
target is `<Out>\SXPRJ`. `ExportFileToSimetrix` takes the view passed to the process, or `CurrentView`, and its
owner document. `GetKind` selects `SXSCH`/`.sxsch` or `SXLIB`/`.sxlib`; any other kind is rejected. The target
defaults to the source name with the new extension in the temp folder.

`SimetrixDll.cs`: `Locate` finds the DLL from `Dll`, or in the folder of the loaded shim assembly
(`SimetrixExporter.dll`): in Debug builds the hot reload host loads the Impl assembly from a stream, so it has no
location of its own. `Export` loads the DLL with `NativeLibrary.Load` (dependencies are also looked up in the DLL's
folder), finds `ExportToSimetrix` with `NativeLibrary.TryGetExport`, calls it through a `StdCall` delegate (two
BSTR strings, an interface pointer, a one-byte Boolean result, after the export's declaration) and frees the DLL
again, on every call.

## Notes
- **The DLL is not part of this sample.** Without it, the commands go as far as loading it: document and project
  resolution, the format, the target and the error messages.
- **A crash in the DLL ends Altium Designer.** An access violation in native code cannot be caught by .NET. The
  likeliest cause is the document pointer: the export's declaration expects a native `IServerDocument` interface,
  whose IID the .NET SDK does not expose (all SDK interfaces carry the `IDispatch` IID). By default the sample passes
  the document's `IDispatch` pointer. If the DLL calls `IServerDocument` methods directly through that pointer, it
  may call the wrong methods. `DocumentIid=<guid>` asks the native object for the given interface instead (the GUID
  must come from the DLL vendor or the native SDK's `DXP.Interfaces`). Which pointer the DLL needs is an open point.
- Opening a `.PrjPcb` with `IWorkspace.DM_OpenProject(path, true)` does not make it the focused project:
  `DM_FocusedProject` still returns another project until a document of the new project is opened and shown.
- The delegate follows the export's declaration (`WideString`, `WideString`, `IServerDocument` returning `Boolean`,
  `stdcall`).
- Whether the DLL treats `<Out>\SXPRJ` as a folder or a file name is not known.
- Whether the DLL accepts `SXLIB` is not known.
- The extension folder: no helper in the .NET SDK returns it. The sample uses the folder of the shim assembly. When
  only the Impl assembly is hot-loaded (no shim), no extension folder is found and `Dll=` must be given; `Dll=`
  always works.
- There are no folder or save dialogs: `Out` gives the target, so a `tools/ad.ps1` call never blocks on a dialog.
  An existing target file is overwritten by the DLL (if it does so) without a prompt.
