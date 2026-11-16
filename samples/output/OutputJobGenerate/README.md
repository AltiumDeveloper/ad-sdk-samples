# Run every Output Job medium of the focused project

Finds the Output Jobs (`.OutJob`) of the focused project, opens and shows each one, and runs its output media by
launching the built-in `WorkspaceManager:GenerateReport` or `WorkspaceManager:Print` process with a parameter
string. It shows how to walk an Output Job's media through `EDP.IWSM_OutputJobDocument` and how to start a built-in
process from .NET with `DXP.Utils.RunCommand`.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`, `EDP.IProject.DM_LogicalDocuments` and `EDP.IDocument.DM_DocumentKind`:
  find the project's Output Jobs (document kind `OUTPUTJOB`).
- `DXP.IClient.OpenDocument` and `DXP.IClient.ShowDocument`: open the `.OutJob` and make it the shown document;
  the `DXP.IServerDocument` is cast to `EDP.IWSM_OutputJobDocument`.
- `EDP.IWSM_OutputJobDocument.OutputMediumCount` and `EDP.IWSM_OutputJobDocument.OutputMedium`: walk the media
  (the output containers of the OutJob editor).
- `EDP.IOutputMedium.Name`, `EDP.IOutputMedium.TypeString` and `EDP.IOutputMedium.OutputPath`: pick the process
  by type, and set a Print Job's destination folder.
- `DXP.Utils.RunCommand`: launch `Server:Process` with a `Key=Value|Key=Value` parameter string. For comparison,
  `DXP.Utils.MessageRouterSendCommandToModule` and `DXP.ICommandLauncher.LaunchCommand` (through
  `DXP.IClient.GetCommandLauncher`) can be chosen with `Route=`.

## Run it
1. Open a project that contains an Output Job and make it the focused project, for example the SpiritLevel-SL1
   example project that ships with Altium Designer (it contains `3D Prints.OutJob`). Work on a copy.
2. **OutputJobGenerate › List Output Media**, or `tools/ad.ps1 -Sample OutputJobGenerate run ListOutputMedia`, to
   see each medium's name and type string.
3. **OutputJobGenerate › Run Output Jobs**, or
   `tools/ad.ps1 -Sample OutputJobGenerate run RunOutputJobs "Out=<folder>|Medium=Folder Structure"`.
4. The result lists each medium as launched (process and parameter string) or skipped, then the files that were
   written or changed under `Out`.

| Command | Parameters | Result |
|---|---|---|
| `RunOutputJobs` | `Out=<folder>` (default `<temp>\OutputJobGenerate`), `Types=Generate Files` (`;`-separated `TypeString` values; `Print` and `PDF` are opt-in), `Medium=<name>`, `Route=RunCommand` (or `MessageRouter`, `Launcher`), `EditProperties=False` | per OutJob: whether it was already open, one line per medium (launched or skipped), `Media launched: n, skipped: m`, the files written under `Out` |
| `ListOutputMedia` | none | per OutJob: each medium's `Name`, `TypeString` and `OutputPath` |

## How it works
`OutputJobGenerateCommands.cs`. Both commands get the workspace with `GetDXPWorkspace`, take
`DM_FocusedProject()` and collect the logical documents whose `DM_DocumentKind()` is `OUTPUTJOB`. No focused
project, or a project without an Output Job, is an error.

`RunOutputJobs` records the files under `Out`, then for every Output Job calls `OpenDocument("OUTPUTJOB", path)`
and `ShowDocument`, casts to `IWSM_OutputJobDocument` and loops `GetState_OutputMedium(j)`. A medium is run when
its `GetState_TypeString()` is in `Types` (and its name equals `Medium`, if given):

- `Print` (alias `Print Job`): `WorkspaceManager:Print` with `Action=PublishMultimedia|DisableDialog=True|ObjectKind=OutputBatch|
  OutputMedium=<name>|PromptOverwrite=FALSE|OpenOutput=FALSE`, after `SetState_OutputPath(<Out>\)`. The old
  `OutputPath` is put back when the call returns.
- `PDF`: `WorkspaceManager:Print` with `Action=PublishToPDF` and
  `OutputFilePath=<Out>\<name>`.
- `Generate Files` and any other type: `WorkspaceManager:GenerateReport` with
  `ObjectKind=OutputBatch|OutputMedium=<name>|OutputFilePath=<Out>\<name>|OutputPathMedia=[Media Name]|
  ReleaseManaged=false|DoGenerate=True|PromptOverwrite=FALSE|OpenOutput=FALSE` (plus `DoEditProperties=True` with
  `EditProperties=True`).

The process is launched by `Launch`, which calls `DXP.Utils.RunCommand(process, parameters)` by default. The
processes return nothing, so the command compares the files under `Out` before and after.

`ListOutputMedia` only reads `GetState_Name`, `GetState_TypeString` and `GetState_OutputPath` of each medium.

## Notes
- Printer media print on a real printer. They run only when `Types` names `Print` (or the alias `Print Job`); the
  default (`Types=Generate Files`) prints nothing.
- `TypeString` returns the container type as the OutJob editor shows it. On the
  SpiritLevel example's `3D Prints.OutJob`: `Print`, `PDF`, `Generate Files`, `Multimedia` — not the words the
  `.OutJob` file stores (`Printer`, `Publish`, `GeneratedFiles`, `Multimedia`), and not the medium's name (the
  printer medium is *named* `Print Job`). Run `ListOutputMedia`
  first and pass the value it shows in `Types`.
- The processes get no OutJob file name; they act on the Output Job that was just shown. This works, but it is not
  documented.
- Launch routes, with the same parameter string: `Route=RunCommand` (`DXP.Utils.RunCommand`) and
  `Route=MessageRouter` (`MessageRouterSendCommandToModule`) both generate the files and return only after they
  exist; `Route=Launcher` (`ICommandLauncher.LaunchCommand`) returns at once and generates nothing.
- `OutputFilePath` is a base folder and `OutputPathMedia=[Media Name]` adds a sub folder named after the medium,
  so this command's `<Out>\<medium name>` produces the medium name twice
  (`<Out>\Folder Structure\Folder Structure\...`). For the PDF container the file is
  written into that folder under the Output Job's name (`3D Prints.PDF`).
- Generating does not mark the Output Job modified.
- **Generating is not unattended.** Generating the SpiritLevel Generate Files medium raises a modal
  `no used layers...` warning that someone has to click OK on, although `PromptOverwrite=FALSE`,
  `OpenOutput=FALSE` and `DisableDialog=True` were set. Individual outputs may show their own dialogs; do not drive
  this command from an unattended script.
- `DoEditProperties=True` may open the medium's properties dialog, which blocks Altium Designer
  when the command is called from a script. It is left out unless `EditProperties=True`.
- `;` separates the values of `Types`, because `|` separates the parameters.
- Output Jobs the command opened stay open; the result says which ones were already open.
- The menu items pass no parameters, so from the menu `RunOutputJobs` runs the `Generate Files` media into
  `<temp>\OutputJobGenerate`.
