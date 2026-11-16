# Generate Gerber files through an Output Job

Generates Gerber files for the active PcbDoc with no user interface. A template Output Job is attached to the
focused project for the duration of the command, and one of its outputers is run directly into a chosen folder. It
shows how to drive an `.OutJob` from code through `EDP.IWSM_OutputJobDocument` and `EDP.IOutputer`.

## What it shows
- `EDP.IProject.DM_AddSourceDocument` / `DM_RemoveSourceDocument`: attach a temporary Output Job to the project and
  detach it again; `EDP.IProject.DM_IndexOfSourceDocument` reads back that it is gone.
- `DXP.IClient.OpenDocument` with kind `OUTPUTJOB`, then a cast to `EDP.IWSM_OutputJobDocument`: the Output Job as
  an object model. `DXP.IClient.CloseDocument` closes it again.
- `EDP.IProject.DM_CurrentProjectVariant` with `EDP.IWSM_OutputJobDocument.VariantName`: generate for the project's
  current variant.
- `EDP.IWSM_OutputJobDocument.OutputerCount` / `Outputer`: enumerate the job's outputs.
- `EDP.IOutputer.DM_SetDocumentPath`: point an output at a data source other than the one stored in the job.
- `EDP.IOutputer.DM_ViewName` and `EDP.IOutputer.DM_Generate_OutputFilesTo`: pick one output by name and generate
  only that one, into a folder of your choice.
- `DXP.IClient.CurrentView`, `DXP.IServerDocumentView.GetOwnerDocument` and `DXP.IServerDocument.GetKind`: check
  that the active document is a PCB before doing anything.

## Run it
1. Open a board project, for example a copy of the SpiritLevel-SL1 example project that ships with Altium Designer.
   Keep it the focused project, and make its `.PcbDoc` the active document.
2. **GenerateGerber › Generate Gerber** in the PCB editor, or
   `tools/ad.ps1 -Sample GenerateGerber run GenerateGerber "Out=<folder>"`
3. The result lists the project, the PcbDoc, the outputers of the job, `DM_Generate_OutputFilesTo returned True`
   for `'Gerber Files'`, `Temp OutJob detached: True`, and the Gerber files written into the output folder.

| Command | Parameters | Result |
|---|---|---|
| `GenerateGerber` | `Out=<folder>` (default `<temp>\GenerateGerber`), `OutJob=<.OutJob file>` (default: the embedded `Gerber.OutJob`) | Project and PcbDoc, variant, each outputer (run or not), the generate result, the detach read-back, and the files written or changed under `Out` |

## How it works
`GenerateGerberCommands.cs` first checks the focused project (`EDP.IWorkspace.DM_FocusedProject`) and that the
active document is a PCB. It writes the Output Job template to a new temp file: the `OutJob` parameter's file, or
`Gerber.OutJob`, which is embedded in the Impl assembly (`GenerateGerber.Impl.csproj`). It snapshots the output
folder, then adds the temp job to the project and opens it with `OpenDocument("OUTPUTJOB", …)`. When the project
has a current variant, the job is set to it. Every outputer is pointed at the PcbDoc with `DM_SetDocumentPath`, and
the one whose `DM_ViewName` is `Gerber Files` is generated with `DM_Generate_OutputFilesTo(Out, "")`. A `finally`
block closes the job, removes it from the project and deletes the temp file, whatever happened before. The result
compares the output folder with the snapshot, because `DM_Generate_OutputFilesTo` returns only a bool.


## Notes
- The project gets a source document added and removed. This leaves the project marked modified, although its
  document list is back to what it was.
- The command uses the focused project, even when the active PcbDoc belongs to another project.
- `Gerber.OutJob` has its machine-specific entries empty (the Gerber output's `DocumentPath`, the Folder Structure
  medium's output paths). It still defines Print Job, PDF and Video media and a target printer. None of them is used,
  because only the outputer is run.
- The Gerber configuration selects `LayerClassesPlot.Set="All Layers"` and plots the used drill drawing and drill
  guide layer pairs. With an empty plot set the Gerber output produces only report files (no layers), while
  `DM_Generate_OutputFilesTo` still returns True. So `True` means the generation ran, not that files were written;
  the command lists the written files instead.
- The opened document casts to `EDP.IWSM_OutputJobDocument`, generation shows no dialog, and a second run overwrites
  the files without a prompt. The format of `DM_Generate_OutputFilesTo`'s second argument (parameter overrides) is not
  documented; it is empty here. The `EDP.IOutputer` reference has no member descriptions.
- The template is embedded rather than read from next to the DLL: in Debug the hot reload host loads the Impl from
  bytes, so the assembly has no file location.
