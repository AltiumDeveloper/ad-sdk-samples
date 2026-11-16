# Read and write custom project option values

Stores a custom section/key/value parameter in the options storage of the focused project when the key has no value
yet, and reports the stored value. It shows the external-parameter methods of `DXP.IOptionsStorage`, reached from a
project with `DM_OptionsStorage`, plus a read-back and a delete command.

## What it shows
- `DXP.IDXPWorkSpace.DM_FocusedProject`: the focused project, queried again in each command.
- `DXP.IDXPProject.DM_OptionsStorage`: the project's options storage (an extension method; `using DXP;`).
- `DXP.IOptionsStorage.GetExternalParameterValue`: reads a value by section and name. The third argument is
  returned when no value is found; a marker default tells "no value" apart from a real one.
- `DXP.IOptionsStorage.AddExternalParameter`: adds a value, only when the key has none.
- `DXP.IOptionsStorage.ModifyExternalParameter`: replaces an existing value (`Overwrite=true`).
- `DXP.IOptionsStorage.DeleteExternalParameter`: removes a key.
- `DXP.IOptionsStorage.SectionExists`, `DXP.IOptionsStorage.GetSectionText`, `DXP.IOptionsStorage.GetFileName`:
  inspect the storage without changing it.

## Run it
1. Open a project (for example a copy of the SpiritLevel-SL1 example project that ships with Altium Designer) and
   make it the focused project.
2. **OptionsStorage › Add User Parameter**, or `tools/ad.ps1 -Sample OptionsStorage run AddUserParameter "Section=MySection|Key=UserName|Value=Allen"`
3. The result names the project and says whether the value was added or kept, then `stored value = 'Allen'`.
   **Read User Parameter** shows the value and the section; **Delete User Parameter** removes it again.

| Command | Parameters | Result |
|---|---|---|
| `AddUserParameter` | `Section` (default `MySection`), `Key` (default `UserName`), `Value` (default `Allen`), `Overwrite` (optional flag: `Overwrite=true`, `Overwrite=1` or a bare `Overwrite`) | `<project>: [<Section>] <Key> added \| modified (was '<old>') \| kept (...); stored value = '<value>'.` |
| `ReadUserParameter` | `Section`, `Key` (same defaults) | the value or `(no value)`, `SectionExists`, `GetFileName()` and the section text, one per line |
| `DeleteUserParameter` | `Section`, `Key` (same defaults) | `<project>: [<Section>] <Key> deleted; value before = ..., after = ...; SectionExists(...) = ...` |

## How it works
`OptionsStorageCommands.cs` gets the focused project from `DXP.GlobalVars.Client.GetDXPWorkspace()` and its storage
from `DM_OptionsStorage()`. A `UserException` covers a missing workspace, project or storage, and an empty `Section`
or `Key`. `AddUserParameter` reads the key with an empty default. An empty result means "not set", so it calls
`AddExternalParameter`. With `Overwrite=true` and a different value it calls `ModifyExternalParameter`; otherwise it
keeps the value. It then reads the value back with the default `Not Set`. `ReadUserParameter`
reads with a `<no value>` marker default and adds `SectionExists`, `GetFileName` and `GetSectionText`.
`DeleteUserParameter` calls `DeleteExternalParameter` and reads the key before and after.

## Notes
- `GetExternalParameterValue(section, key, "")` returns `null`, not `""`, for a missing key. `AddUserParameter`
  therefore tests with `string.IsNullOrEmpty`. A plain `== string.Empty` check never sees "not set".
- `GetFileName()` returns the full path of the `.PrjPcb`. `GetSectionText` returns `Key=Value` lines, and an empty
  text when the section is missing. `ModifyExternalParameter` replaces the value in place, with no duplicate.
  Deleting the last key of a section removes the section. After the writes AD lists the project as modified.
- Not known: whether a key stored with an empty value reads back as the default. If it does, `AddUserParameter`
  treats it as unset and calls `AddExternalParameter` again; whether that adds a duplicate entry is not documented.
- The commands act on `DM_FocusedProject()`. Opening a project does not make it the focused one: select it in the
  Projects panel or open one of its documents first, and check the project name in the result.
- `BeginUpdate` / `EndUpdate` exist on `DXP.IOptionsStorage` but are not used: the reference does not say what they
  do.
- `DXP.IDXPProject.DM_OptionsStorage` has no description in the reference. `EDP.IWorkspace.DM_OptionsStorage`
  describes a workspace-level storage for EDE and project options; this sample uses the project-level one only.
