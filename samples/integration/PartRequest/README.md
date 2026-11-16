# List the current user's Part Request tasks

Queries the workspace task service for the Part Request tasks of the signed-in user and lists each task's HRID,
GUID, state, description and assignee. It shows how to reach the EDMS task service from an extension and how to
configure a task search.

## What it shows
- `EDP.Utils.GetDXPServerManager`: the EDMS entry point. It is the only `EDP` call: the server manager and every
  task type are in the `DXP` namespace.
- `DXP.IEDMS_DXPServerManager.IsConnected`: check the sign-in before using any service, and fail with a clear
  message instead of opening a sign-in dialog.
- `DXP.IEDMS_DXPServerManager.GetTaskService`: the task service of the connected workspace.
- `DXP.IEDMS_TaskService.CreateSearchParams` and `DXP.IEDMS_TaskSearchParams` (`ForCurrentUser`, `SortField`,
  `ItemsPerPage`, `PageIndex`, `TaskType` = `DXP.TTaskType.ttPartRequest`): describe the search.
- `DXP.IEDMS_TaskService.GetPartRequestTasks`: runs the search, returns `bool` plus a `DXP.IInterfaceList` and
  an error message. A `ref DXP.IEDMS_TaskSearchParams` argument selects the typed overload.
- `DXP.IInterfaceList.Get`: returns `object`. Cast each item to `DXP.IEDMS_TaskExt` (or its base
  `DXP.IEDMS_TaskBase`).
- `DXP.IEDMS_TaskBase` (`HRID`, `GUID`, `TaskState`, `Description`, `AssigneeGUID`): the task fields.
- `DXP.IEDMS_DXPServerManager.GetUserNameByGUID`: optionally turns the assignee GUID into a user name.

## Run it
1. Sign in to an Altium 365 workspace (or a server with the task service). No document needs to be open.
2. **PartRequest › Get Part Requests**, or `tools/ad.ps1 -Sample PartRequest run GetPartRequests`
3. The result is either `No part requests for the current user.` or a count line followed by one line per task.

| Command | Parameters | Result |
|---|---|---|
| `GetPartRequests` | `Max=<n>` (lines, default 50), `Sort=<field>` (default `CreatedAt`), `Names=true` (resolve assignee names, optional) | `<n> part request(s) for the current user (sorted by '<field>', total records reported by the search: <t>):`, then `[<i>] <HRID> GUID <guid> state <TTaskStateType>: '<description>' assignee <guid> ['<name>']` per task |

## How it works
`PartRequestCommands.cs` gets the server manager from `EDP.Utils.GetDXPServerManager` and throws a
`UserException` when it is missing or `IsConnected` is false. It gets the task service and builds the search:
current user, the `Sort` field, `ItemsPerPage` -1, page 0, task type `ttPartRequest`. It calls
`GetPartRequestTasks`. A `false` result becomes a `UserException` carrying the service's error message. An empty
list becomes the "no part requests" message. Otherwise each item of the list is cast to `IEDMS_TaskExt` (falling
back to `IEDMS_TaskBase`) and formatted on one line, up to `Max` lines. A COM error while reading one item is
reported in that item's line. A COM error while setting up or running the search becomes a `Task search failed`
message. With `Names=true` the assignee GUID is passed to `GetUserNameByGUID`.

## Notes
- Read-only: the command only queries the task service.
- The search with `ItemsPerPage` = -1 and `SortField` = `CreatedAt` succeeds. `Sort=HRID` does not make the search
  fail either. The reference lists no accepted sort field names.
- Each item is cast to `IEDMS_TaskExt` first. The line ends with `(IEDMS_TaskBase only)` when only the base cast
  works.
- The reference does not say whether the search fills in `IEDMS_TaskSearchParams.GetTotalRecords`. The count line
  prints it as read.
- The command never signs in. `IEDMS_DXPServerManager.LoginWithUI` would open a dialog, which blocks AD when the
  command runs from `tools/ad.ps1`.
