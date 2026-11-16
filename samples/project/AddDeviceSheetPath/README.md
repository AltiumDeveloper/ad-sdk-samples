# Register a device sheet search folder

Adds a folder to the list of device sheet folders that Altium Designer searches, lists that list and removes a
folder again. It shows how to use the device sheet manager and how to ask a server to write its options to the
user's settings.

## What it shows
- `EDP.Utils.GetDeviceSheetManager`: the entry point to the device sheet folder list.
- `SCH.IDeviceSheetManager.AddDeviceFolder`: adds a folder; the second argument turns on searching subfolders.
  It returns a `bool` whose meaning is not documented, so the sample reports it as returned.
- `SCH.IDeviceSheetManager.GetFoldersCount`, `SCH.IDeviceSheetManager.GetFolders_FolderPath`,
  `SCH.IDeviceSheetManager.GetFolders_SearchSubFolders`: read the folder list (indexes start at 0).
- `SCH.IDeviceSheetManager.WillSearchDeviceFolder` and `SCH.IDeviceSheetManager.DeleteFolder`: check and remove
  a registered folder.
- `SCH.IDeviceSheetManager.ChooseDeviceFolder`: AD's folder dialog, used only from the menu when no path is given.
- `DXP.IClient.GetServerModuleByName` and `DXP.IServerOptions.FlushOptions`: get the `IntegratedLibrary` server
  module, test whether it implements `DXP.IServerOptions`, and flush its options.

## Run it
1. No document is needed. Create an empty folder to register (for example under the temp folder).
2. **AddDeviceSheetPath › Add Device Sheet Path** (asks for the folder), or
   `tools/ad.ps1 -Sample AddDeviceSheetPath run AddDeviceSheetPath "Path=<folder>"`
3. The result names the folder, the value `AddDeviceFolder` returned, the folder count before and after, and
   whether the options were flushed. **List Device Sheet Paths** shows the folder in the list.
   **Remove Device Sheet Path** with the same `Path` takes it out again.

| Command | Parameters | Result |
|---|---|---|
| `AddDeviceSheetPath` | `Path=<folder>` (required from `tools/ad.ps1`; the menu opens a dialog), `Recursive` (flag: search subfolders) | `AddDeviceFolder('<folder>', subfolders off) returned true; folders <n> -> <n+1>; WillSearchDeviceFolder = true (<flush result>).`, or `'<folder>' is already device sheet folder #<i> ...; nothing changed.` |
| `ListDeviceSheetPaths` | none | `<n> device sheet folder(s):` then one line `#<i> <path> (subfolders on/off)` per folder |
| `RemoveDeviceSheetPath` | `Path=<folder>` (required) | `Removed device sheet folder #<i> '<folder>'; folders <n> -> <n-1> (<flush result>).`, or `... is not a device sheet folder; nothing changed.` |

## How it works
`AddDeviceSheetPathCommands.cs` gets the manager from `EDP.Utils.GetDeviceSheetManager` in each command.
`AddDeviceSheetPath` reads `Path` (or, from the menu, asks with `ChooseDeviceFolder`), makes it a full path and
checks that the folder exists. It looks the folder up in the list (`GetFoldersCount` / `GetFolders_FolderPath`,
case-insensitive, trailing separator ignored) and stops if it is already there. Otherwise it calls
`AddDeviceFolder`, reads the count again and `WillSearchDeviceFolder`, then flushes the options.
`RemoveDeviceSheetPath` finds the folder's index the same way and calls `DeleteFolder(index)`, then flushes.
The flush helper gets the `IntegratedLibrary` module with `GetServerModuleByName`, which returns null when that
server is not loaded, and calls `FlushOptions` only when the module is an `IServerOptions`.

## Notes
- This changes the user's real Altium Designer settings. Remove a test folder again with `RemoveDeviceSheetPath`.
- The `IntegratedLibrary` server module implements `DXP.IServerOptions` and `FlushOptions` runs. Not known: whether
  that server is loaded when no library or schematic has been used, and whether `FlushOptions` is what makes the
  folder list persist across restarts. The result text says which branch ran.
- `AddDeviceFolder` returns `true` for a new folder and appends it at the end of the
  list, and `DeleteFolder` takes the same 0-based index as `GetFolders_FolderPath`. Not known: what a `false`
  return means, and whether `AddDeviceFolder` itself skips a folder that is already registered (the sample checks
  the list first, so a second add does not reach `AddDeviceFolder`).
- AD stores a path as passed (no trailing separator added), but the default folder comes back with a trailing
  backslash. The lookup normalizes both sides, so it does not depend on the form.
- `ChooseDeviceFolder` runs only when the command comes from the menu; `tools/ad.ps1` never opens a dialog.
