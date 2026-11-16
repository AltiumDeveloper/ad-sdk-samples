# Add an outputer to an Output Job

Opens an Output Job (`.OutJob`) document, adds it to the focused project and adds a predefined output (by default a
"PCB Print" in the "Documentation" category) once for each PDF output medium of the job. It shows how to reach an
Output Job through `EDP.IWSM_OutputJobDocument` and create outputers in it from code.

## What it shows
- `DXP.IClient.OpenDocument`: open the `.OutJob` with the document kind `OUTPUTJOB`; the result is a
  `DXP.IServerDocument`.
- `DXP.IServerDocument.DoSafeFileSave`: save the document right after opening it.
- `EDP.IWorkspace.DM_FocusedProject`, `EDP.IProject.DM_IndexOfSourceDocument` and
  `EDP.IProject.DM_AddSourceDocument`: add the Output Job to the focused project when it is not in it yet.
- `EDP.IWSM_OutputJobDocument`: the server document cast to the Output Job interface; `OutputMediumCount` and
  `OutputMedium` enumerate the media, `EDP.IOutputMedium.TypeString` and `EDP.IOutputMedium.Name` describe them.
- `EDP.IWSM_OutputJobDocument.CreateOutputer`: add an outputer from an output category name, a predefined output
  name and the new outputer's name. It takes no medium.
- `EDP.IWSM_OutputJobDocument.OutputerCount`, `Outputer`, `MediumOutputersCount` and `MediumOutputer`, with
  `EDP.IOutputer.DM_ViewName` (the outputer's name), `EDP.IOutputer.DM_GetDescription` (its output type's default
  name) and `EDP.IOutputer.DM_GeneratorName`: read the outputers back.

## Run it
1. Open a project (for example a copy of the SpiritLevel-SL1 example project that ships with Altium Designer) and
   focus one of its documents. Have an `.OutJob` file with at least one PDF output medium in a folder you can write
   to (the command saves it), for example a copy of the example project's `3D Prints.OutJob`.
2. `tools/ad.ps1 -Sample OutJobCreateOutputer run CreateOutputer "OutJob=<path>\My.OutJob"`. The menu items
   **OutJobCreateOutputer › Create Outputer** and **List Outputers** pass no `OutJob` parameter, so from the menu they
   only report that the parameter is missing.
3. The result lists the job's media, one `created: '<name>' (<type default name>)` line per PDF medium, and the outputer count
   before and after. `ListOutputers "OutJob=<path>\My.OutJob"` shows the new outputers.

| Command | Parameters | Result |
|---|---|---|
| `CreateOutputer` | `OutJob=<path>` (required), `Category=Documentation`, `Type=PCB Print`, `Name=MyOutputer`, `MediumType=PDF` | the media examined, the outputers created, `OutputerCount: <before> -> <after>` |
| `ListOutputers` | `OutJob=<path>` (required) | the media with their outputers, then all outputers of the job |

## How it works
`OutJobCreateOutputerCommands.cs`. `CreateOutputer` checks that the `OutJob` file exists and that a project is
focused, then opens the file with `IClient.OpenDocument("OUTPUTJOB", path)` and saves it with
`DoSafeFileSave("")`. It adds the file to the project with `DM_AddSourceDocument` unless
`DM_IndexOfSourceDocument` finds it there already. It casts the `IServerDocument` to `IWSM_OutputJobDocument`,
walks the media with `GetState_OutputMedium(i)` and, for each medium whose `TypeString` equals `MediumType`
(case-insensitive), calls `CreateOutputer(Category, Type, Name)`. It reports each created outputer's name (`DM_ViewName`),
description and generator, and `OutputerCount` before and after.

`ListOutputers` opens the same file and reads it only: for each medium `MediumOutputersCount`/`MediumOutputer`, then
all outputers through `OutputerCount`/`Outputer`.

## Notes
- The command saves the OutJob right after opening it, but not after `CreateOutputer`: the
  new outputers exist in memory only until the document is saved. Running the command again saves them (its first
  save writes the outputers of the previous run). The project is changed too (the OutJob is added), and is not saved.
- N PDF media give N outputers with the same name and no PDF medium gives none. `CreateOutputer`
  takes no medium: the new outputer is enabled on **no** medium
  (`OutputEnabledN_OutputMediumM=0` in the saved file), so no medium generates it until the user enables it in the
  OutJob editor. The interfaces have no member to enable it from code.
- `CreateOutputer` returns null for an unknown category or output name (no exception, no dialog). It worked without
  `BeginModifyOutputer`/`EndModifyOutputer`.
- `DM_GetDescription` is the output type's default name ('PCB Prints'), `DM_ViewName` the outputer's own name
  ('MyOutputer').
- `MediumOutputersCount`/`MediumOutputer` list the outputers of the medium's output group (Print and PDF media list
  the same Documentation outputers), not the ones enabled for it, and `MediumOutputersCount` is -1 for a Video
  medium. `TypeString` is the display type ('PDF'), not the file's `Type=Publish`.
- `OpenDocument` on an OutJob that is already open returns the open (modified) document.
- The `.OutJob` file format is not documented.
- The API is spelled `CreateOutputer` ("Outputer", not "Outputter").
