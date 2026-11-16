# Update or commit project documents by VCS status

Reads the version control status of every logical document of the focused project and, on request, updates the
out-of-date documents and commits the modified ones. It shows how to reach the `VersionControl` server as a
`DXP.IVcsManager`, get the project's `DXP.IVcsProject` binding, and test the flags of a `DXP.TVCSStatusSet`.

## What it shows
- `DXP.IClient.GetServerModuleByName`: the `VersionControl` server module, used as `DXP.IVcsManager` (the module
  itself is the manager).
- `DXP.IDXPProject.DM_GetVCSProject`: the project's VCS binding. The property `VCSProject` is a method that
  returns `object` in .NET; cast it to `DXP.IVcsProject`.
- `DXP.IVcsManager.GetLocationInRepository`: an empty location for the project folder means "not under version
  control".
- `DXP.IVcsManager.GetStatus`: the status of one file as a `DXP.TVCSStatusSet`. Test a flag with
  `status.Contains(DXP.TVCSStatus.vcsOutOfDate)` (works, although the reference lists no members for the set
  class).
- `DXP.IVcsManager.GetStatusString`: the same status as AD's text.
- `DXP.IVcsProject.ProviderType`, `DXP.IVcsProject.GetRepositoryRoot`, `DXP.IVcsProject.GetLocationInRepository`:
  where the project lives in the repository.
- `DXP.IVcsManager.GetProviderByType` and `DXP.IVcsProvider.ListRepository`: list the repository folder of the
  project (optional, `List=true`).
- `DXP.IVcsManager.GetVcsFileNamesFactory` and `DXP.IVcsFileNamesFactory.CreateFromSingleFileName`: the
  `DXP.IVcsFileNames` list that `DXP.IVcsProject.Update` and `DXP.IVcsProject.Commit` take.

## Run it
1. Open a project whose folder is a working copy of an SVN or Git repository known to AD (Version Control
   preferences), and make it the focused project. Modify and save one document to see the Commit path.
2. **ProjectVcsSync › Get Project Vcs Status**, or `tools/ad.ps1 -Sample ProjectVcsSync run GetProjectVcsStatus`
3. The first line names the project, provider, repository root and location; then one line per logical document
   such as `  Sheet1.SchDoc: [vcsControlled, vcsModified] ('Modified') -> Commit`. A project outside version
   control returns `<project>: project is not under version control.`

| Command | Parameters | Result |
|---|---|---|
| `GetProjectVcsStatus` | `List=true` (optional: also call `ListRepository` for the project folder) | Header line, one status line per logical document with the action Sync would take, document count |
| `SyncProjectVcs` | `DryRun=true` (default; `DryRun=false` acts), `Message=<commit message>` (default `Automatically commit changes`) | One line per document that is out of date or modified: `would Update` / `would Commit`, or the `Update` / `Commit` result and the new status; a count line |

## How it works
`ProjectVcsSyncCommands.cs`: `Bind` gets the `VersionControl` server module with `GetServerModuleByName` and uses it
as `DXP.IVcsManager`, takes the focused project from `DXP.IDXPWorkSpace.DM_FocusedProject`, and casts
`DM_GetVCSProject()` to `DXP.IVcsProject`. When there is no VCS project, or `GetLocationInRepository` returns an
empty string for the project folder, the command returns "not under version control".

`GetProjectVcsStatus` loops over `DM_LogicalDocuments`, calls `GetStatus` and `GetStatusString` for each
`DM_FullPath`, and lists the `DXP.TVCSStatus` values contained in the set. With `List=true` it calls
`GetProviderByType(ProviderType())` and `ListRepository(GetRepositoryRoot(), GetLocationInRepository(<project file>),
false)`, and reports the number of entries (the first 20 by name).

`SyncProjectVcs` loops over the same documents: a document whose status contains `vcsOutOfDate` is updated with
`Update(<list>, true)`; otherwise one that contains `vcsModified` is committed with `Commit(<list>, Message)`. The
one-file list comes from `GetVcsFileNamesFactory().CreateFromSingleFileName`. Without `DryRun=false` it only reports
what it would do. After a real Update or Commit it reads the status again.

## Notes
- `SyncProjectVcs DryRun=false` commits to the repository (and may need credentials or show AD's own dialogs).
  It is not undoable from AD. Use a throwaway repository for tests.
- The `int` results of `DXP.IVcsProject.Update` and `DXP.IVcsProject.Commit` are printed raw. Their meaning is
  not documented.
- `DXP.TVCSStatusSet.Contains(DXP.TVCSStatus)` works at run time. An untracked document gives an empty set and
  `GetStatusString` returns `Not in version control`.
- For a project in a Git working copy, `ProviderType()` is `ptGit`, `IVcsProject.GetRepositoryRoot()` returns an
  empty string, and `IVcsManager.GetLocationInRepository` returns the path relative to the working copy root, with
  forward slashes and no leading slash.
- For a project outside any working copy, `DM_GetVCSProject()` returns null, so the commands report
  "not under version control (no VCS project)" before any VCS call.
- The SDK interfaces are dispatch interfaces, so `server as IVcsManager` succeeds for any server module. A wrong
  object would only show as a `COMException` on the first call.
- Documents are handled one at a time. Only logical documents are checked (not the project file itself or
  generated files).
