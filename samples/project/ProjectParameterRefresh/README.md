# Set a project parameter and refresh the Projects panel

Changes the value of a parameter of the focused project through the design model and then asks the Projects panel to
show the change. It teaches how to reach project parameters with `EDP.IProject.DM_Parameters` and the
`EDP.IProject.DM_RefreshInWorkspaceForm` call that follows a design-model change.

## What it shows
- `DXP.IClient.GetDXPWorkspace` cast to `EDP.IWorkspace`, then `EDP.IWorkspace.DM_FocusedProject`: the project the
  user is working on, queried again in each command. The `DM_*` helpers need `using EDP;`.
- `EDP.IProject.DM_ParameterCount` / `EDP.IProject.DM_Parameters`: the project's parameters by 0-based index
  (both inherited from `EDP.IDMObject`).
- `EDP.IParameter.DM_Name` / `EDP.IParameter.DM_Value` / `EDP.IParameter.DM_SetValue`: read and change one parameter.
- `EDP.IProject.DM_RefreshInWorkspaceForm`: called after the change, so the Projects panel shows
  the new value.

## Run it
1. Open a project that has at least one project parameter (Project Options › Parameters) and focus it in the
   Projects panel. The SpiritLevel-SL1 example project has none; add one to a copy first.
2. **ProjectParameterRefresh › List Project Parameters**, or `tools/ad.ps1 -Sample ProjectParameterRefresh run ListProjectParameters`
3. `tools/ad.ps1 -Sample ProjectParameterRefresh run SetProjectParameter "Name=<parameter>|Value=New value"`, then
   **List Project Parameters** again: the parameter shows `New value`. The menu item **Set Project Parameter**
   writes `KKKKEEE` into parameter #0.

| Command | Parameters | Result |
|---|---|---|
| `SetProjectParameter` | `Name=<name>` (case-insensitive) or `Index=<n>` (0-based, default 0); `Value=<text>` (default `KKKKEEE`) | `<project>: parameter #<i> '<name>' changed from '<old>' to '<new>'; Projects panel refreshed. ...` |
| `ListProjectParameters` | none | `<project>: <n> parameter(s)` and one `#i Name = Value` line per parameter |

## How it works
`ProjectParameterRefreshCommands.cs` gets the workspace from `DXP.GlobalVars.Client`, casts it to `EDP.IWorkspace` and
takes `DM_FocusedProject`; a `UserException` reports a missing workspace or project. `SetProjectParameter` checks
`DM_ParameterCount`, finds the parameter by name (a loop over `DM_Parameters` comparing `DM_Name`) or by index (with a
bounds check), remembers `DM_Value`, calls `DM_SetValue`, then `DM_RefreshInWorkspaceForm`. It reads the value back
through a fresh `DM_Parameters(index)` lookup and returns old and new values. `ListProjectParameters` loops over
`DM_Parameters` and prints name and value.

## Notes
- The change is made in memory only, and **`DM_SetValue` does not mark the project as modified**: the project gets
  no `*` marker, AD offers no save when it is closed, and the new value is lost. The `.PrjPcb` on disk keeps the
  old value.
- `DM_RefreshInWorkspaceForm` has no visible effect for a parameter change: the Projects panel tree does not show
  project parameters. The call is harmless; it does not collapse or reorder the tree. What it refreshes for other
  design-model edits is not documented.
- `DM_Parameters(index)` does not follow the order of the `[ParameterN]` sections in the `.PrjPcb`; two parameters
  came back alphabetically by name. Look parameters up by `Name`, not by `Index`.
- The reference does not say whether `EDP.IProject.DM_Parameters` lists the same parameters, in the same order, as
  `DXP.IDXPProject.DM_GetParameterName` (used by the RemoveParamFromProject sample).
- Name lookup is case-insensitive; when two parameters differ only in case, the first one is changed.
