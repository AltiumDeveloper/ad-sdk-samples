# Redirect an outputer's source document in an OutJob

Opens an Output Job (`.OutJob`), finds an outputer by its display name (by default "Schematic Prints") and points it
at another source document, then marks the Output Job modified. It shows how to reach the outputers of an Output Job
through `EDP.IWSM_OutputJobDocument` and change their source with `EDP.IOutputer.DM_SetDocumentPath`.

## What it shows
- `DXP.IClient.OpenDocument`: open the `.OutJob` with the document kind `OUTPUTJOB`; the result is a
  `DXP.IServerDocument`, cast to `EDP.IWSM_OutputJobDocument`.
- `EDP.IWSM_OutputJobDocument.OutputerCount` and `EDP.IWSM_OutputJobDocument.Outputer`: walk the job's outputers.
- `EDP.IOutputer.DM_ViewName`: the outputer's display name, used to find the one to change.
- `EDP.IOutputer.DM_SetDocumentPath` and `EDP.IOutputer.DM_GetDocumentPath`: set the outputer's source document and
  read back what was stored.
- `EDP.IWSM_OutputJobDocument.BeginModifyOutputer` / `EDP.IWSM_OutputJobDocument.EndModifyOutputer`: optional
  bracket around the change (`BeginModify=True`).
- `DXP.IServerDocument.Modified` (`SetModified`) and `DXP.IServerDocument.DoSafeFileSave`: mark the job changed, and
  optionally save it.
- `EDP.IOutputer.DM_GeneratorName` and `EDP.IOutputer.DM_GetDocumentKind`: describe the outputers in the listing.

## Run it
1. Have an `.OutJob` with an outputer whose name is shown as "Schematic Prints" (or pass `Outputer=<name>`), in a
   folder you may change, for example a copy of the `3D Prints.OutJob` of the SpiritLevel-SL1 example project that
   ships with Altium Designer. Work on a copy: with `Save=True` the file is overwritten.
2. `tools/ad.ps1 -Sample OutputJobChangeSource run ListOutputJobOutputers "OutJob=<path>\My.OutJob"` to see the
   outputer names and their documents, then
   `tools/ad.ps1 -Sample OutputJobChangeSource run ChangeOutputJobFile "OutJob=<path>\My.OutJob|Source=Other.SchDoc"`.
   The menu items **OutputJobChangeSource › …** pass no `OutJob`, so from the menu they only report the missing
   parameter.
3. The result lists each changed outputer as `'<name>' document: '<before>' -> '<after>'`, then
   `Outputers changed: n of m. Modified: True.` and whether the job was saved.

| Command | Parameters | Result |
|---|---|---|
| `ChangeOutputJobFile` | `OutJob=<path>` (required), `Source=<document>` (required), `Outputer=Schematic Prints`, `Save=False`, `BeginModify=False` | one line per changed outputer (document before and after), the count, modified state, saved or not |
| `ListOutputJobOutputers` | `OutJob=<path>` (required) | `Outputers: m`, then per outputer: name, generator, document kind, document path |

## How it works
`OutputJobChangeSourceCommands.cs`. Both commands check that the `OutJob` file exists, open it with
`IClient.OpenDocument("OUTPUTJOB", path)` and cast the `IServerDocument` to `IWSM_OutputJobDocument`.

`ChangeOutputJobFile` loops `GetState_Outputer(i)` for `i < GetState_OutputerCount()` and, for every outputer whose
`DM_ViewName()` equals `Outputer` exactly, reads `DM_GetDocumentPath()`, calls `DM_SetDocumentPath(Source)`
(inside `BeginModifyOutputer`/`EndModifyOutputer` when `BeginModify=True`) and reads the path back. If at least one
outputer changed, it calls `SetModified(true)` on the server document and, with `Save=True`, `DoSafeFileSave("")`.
No match is an error.

`ListOutputJobOutputers` only reads: `DM_ViewName`, `DM_GeneratorName`, `DM_GetDocumentKind` and
`DM_GetDocumentPath` of each outputer, plus the document's `Modified` state.

## Notes
- By default the job is changed in memory only; AD shows it as modified. `Save=True` saves it.
- `Source` is passed to `DM_SetDocumentPath` unchanged, and AD stores it verbatim: a bare file name
  (`SL_Config_2E.SchDoc`) stays a bare file name and is written to the OutJob's
  `OutputDocumentPath<N>` key on save. AD does **not** check that the document exists - a
  name that matches no document is accepted silently.
- **`BeginModify=True` discards the change**: `BeginModifyOutputer` returns `True`, but after
  `EndModifyOutputer` the outputer still reports the old document. The default (`BeginModify=False`) is the one that
  works. The option is kept to show this behaviour.
- The cast from `IServerDocument` to `IWSM_OutputJobDocument` works on the .NET proxy.
- The name match is exact and case-sensitive; all matching outputers are changed.
- `OpenDocument` on an OutJob that is already open returns the open document, with unsaved changes and the
  `Modified` flag, so `ListOutputJobOutputers` shows a change made without `Save`.
- An outputer whose data source is not one file reports a placeholder: `[Project]`, `[PCB Document]` and
  `All SCH Documents` were seen in the SpiritLevel example's `3D Prints.OutJob`.
- None of the `EDP.IOutputer` members used here has a description in the reference yet.
