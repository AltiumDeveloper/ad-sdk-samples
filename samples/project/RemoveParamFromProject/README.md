# Remove all parameters from the focused project

Removes every project-level parameter of the focused project, by index from the last to the first. It shows how
to reach the focused project through the workspace and how to read, add and remove its parameters with the
`DM_*` parameter members of `DXP.IDXPProject`.

## What it shows
- `DXP.IClient.GetDXPWorkspace` and `DXP.IDXPWorkSpace.DM_FocusedProject`: the focused project, queried again in
  every command (null when no project is focused).
- `DXP.IDXPProject.DM_GetParameterCount`, `DXP.IDXPProject.DM_GetParameterName` and
  `DXP.IDXPProject.DM_GetParameterValue`: project parameters are addressed by a 0-based index.
- `DXP.IDXPProject.DM_RemoveParameter`: removes the parameter at an index. The loop runs from the last index down
  to 0, so a removal never changes the indexes still to visit.
- `DXP.IDXPProject.DM_GetParameterExist`, `DXP.IDXPProject.DM_SetParameter` and `DXP.IDXPProject.DM_AddParameter`:
  add a parameter, or change the value of one that already exists, by name.

## Run it
1. Open a throw-away copy of a project (for example SpiritLevel-SL1, an example project that ships with Altium
   Designer) and make it the focused
   project. **The removal does not ask for confirmation.**
2. Optionally add test parameters: `tools/ad.ps1 -Sample RemoveParamFromProject run AddProjectParameter "Name=Test|Value=1"`.
3. **RemoveParamFromProject › Remove Param From Project**, or
   `tools/ad.ps1 -Sample RemoveParamFromProject run RemoveParamFromProject`
4. The result names the removed parameters and shows the count going to 0. **List Project Parameters** then
   reports `0 parameter(s)`.

| Command | Parameters | Result |
|---|---|---|
| `RemoveParamFromProject` | none | `<project>: removed <n> parameter(s) (<names>); count <n> -> 0. ...` |
| `ListProjectParameters` | none | `<project>: <n> parameter(s)` then one `#i Name = Value` line each |
| `AddProjectParameter` | `Name=<name>` (required), `Value=<text>` (default empty) | `<project>: added parameter '<name>' = '<value>'; count <a> -> <b>.` (`updated` instead of `added` when the name exists) |

## How it works
`RemoveParamFromProjectCommands.cs` gets the workspace from `DXP.GlobalVars.Client.GetDXPWorkspace()` and the
project from `DM_FocusedProject()`, and throws a `UserException` when either is missing. `RemoveParamFromProject`
reads `DM_GetParameterCount()`, then for each index from the last to 0 records `DM_GetParameterName(i)` and calls
`DM_RemoveParameter(i)`. It reads the count again and returns both counts and the removed names.
`ListProjectParameters` reads name and value per index. `AddProjectParameter` checks `DM_GetParameterExist(name)`
and calls `DM_SetParameter` for an existing name, `DM_AddParameter` for a new one.

## Notes
- The change is made in memory. Save the project to keep it, or close it without saving to discard it.
- **Check which project is focused before removing.** Opening a project file does not
  make it the focused project: `DM_FocusedProject` still returns the project focused before, until a document of
  the new project is opened. Run `ListProjectParameters` first; its first line names the project the commands
  act on.
- `DM_RemoveParameter` takes the same 0-based index as `DM_GetParameterName`, and the
  later parameters move down one index after a removal. A loop from index 0 upwards would skip every second
  parameter, so the reverse loop is required.
- `DM_AddParameter` with an existing name adds a duplicate: the count grows and two
  parameters have the same name. `AddProjectParameter` therefore updates existing names with `DM_SetParameter`.
