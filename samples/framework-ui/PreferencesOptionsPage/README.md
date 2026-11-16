# Add a Preferences page and persist its options

Adds a "General" page with one value (Temperature) to the Altium Designer Preferences dialog, and stores the value
in the extension's own options store. It shows the protected extension contract of `DXP.ServerModule` and
`DXP.OptionsForm`, `DXP.Utils.ServerOptionsReader` / `DXP.Utils.ServerOptionsWriter`, and the Preferences
export/import data manager.

## What it shows
- `DXP.ServerModule` (protected `RegisterOptionsPages`, `RegisterOptionsPageCreator`): register a page creator under
  a name. AD's Preferences dialog finds the page through the `OptionsPage` block of the `.ins`, whose `FormName`
  is that name.
- `DXP.OptionsPage`: the wrapper the creator returns, built with its public constructor `OptionsPage(OptionsForm)`.
- `DXP.OptionsForm` (protected `SetStateControlsImpl`, `GetStateControlsImpl`, `SetDefaultStateImpl`,
  `GetModifiedImpl`, `ClearModified`, `GetNotificationCodeImpl`, `CreateDataManager`): the page's WinForms form.
  AD calls these to fill the controls, apply them, reset them to defaults and ask whether they changed.
- `DXP.ServerModule` (protected `ReceiveNotificationImpl`): receive notifications in the server module. Unlike the
  public `DXP.ServerModule.ReceiveNotification` this one is virtual.
- `DXP.Utils.ServerOptionsReader`, `DXP.IOptionsReader.ReadString`, `DXP.IOptionsReader.ValueExists`: read a value
  of the extension's options (server name, section, value name, default).
- `DXP.Utils.ServerOptionsWriter`, `DXP.IOptionsWriter.WriteString`, `DXP.IOptionsWriter.EraseSection`,
  `DXP.IOptionsWriter.Flush`: write the value, and flush it.
- `DXP.OptionsFormDataManager` (protected `SaveSettingsToFilesImpl`, `LoadSettingsFromFilesImpl`) with
  `DXP.IStrings`: take part in AD's Preferences export/import with your own file.
- `DXP.IClientAPI_Interface.SpecialFolder_ExportPreferences`: the folder the export file goes to.

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample PreferencesOptionsPage` and Altium Designer restarted
(the page and its `.ins` entry are part of the shim).

1. No document is needed.
2. **PreferencesOptionsPage › Show Preference**, or `tools/ad.ps1 -Sample PreferencesOptionsPage run ShowPreference`
3. The stored value (`'100'` until something wrote one), the value cached for the page, and the shim's counters.
   For the page itself: **Preferences** (gear icon) › **PreferencesOptionsPage** › **General**, change Temperature,
   OK, then run **Show Preference** again.

| Command | Parameters | Result |
|---|---|---|
| `ShowPreference` | none | `Stored [General] Temperature = '<v>' (ValueExists: <bool>, default '100')`, `Cached by the page: '<v>'`, then the shim status lines (page registrations and creations, page calls, notifications, exports/imports, last save, last error) |
| `SetPreference` | `Temperature=<text>` (required), or `Erase` (flag: erase the `General` section) | `Wrote [General] Temperature = '<v>'.` (or `Erased section [General].`), `Read back: '<v>' (ValueExists: <bool>)`, `Page cache reloaded: '<v>'` |
| `ExportPreferencesFolder` | `WriteIni` (flag, optional), `Folder=<folder>` (optional, default the export folder) | `SpecialFolder_ExportPreferences: '<folder>' (exists: <bool>)`; with `WriteIni`: `Wrote <folder>\MyPref_General.ini` and `Read back Temperature: '<v>'` |

`SetPreference` and `ExportPreferencesFolder` are helpers that use the same store and ini code without opening the
modal Preferences dialog.

## How it works
The page is shim code (`PreferencesOptionsPage/PreferencesPage.cs`), because AD keeps the page objects after the
call that created them. `PluginServerModule` overrides `RegisterOptionsPages` and calls `RegisterOptionsPageCreator("General", ...)`;
the creator returns `new OptionsPage(new GeneralOptionsForm())`. The template's `OnCreated` hook (called once,
when AD creates the server module) loads the cached value with `ServerOptionsReader(...).ReadString("General",
"Temperature", "100")`.

`GeneralOptionsForm` (an `OptionsForm`) shows the cached value in `SetStateControlsImpl` and remembers it
(`ClearModified`), so `GetModifiedImpl` can compare. `GetStateControlsImpl` copies the text box into the cache
and saves it with `ServerOptionsWriter(...).WriteString` and `Flush`. `GetNotificationCodeImpl` returns 1023, and
`ReceiveNotificationImpl` saves again when a system notification with that code arrives. `CreateDataManager` returns `GeneralDataManager`, which writes and reads `MyPref_General.ini` in the export
folder when AD exports or imports preferences.

The commands (`PreferencesOptionsPage.Impl/Commands/PreferencesOptionsPageCommands.cs`) use the same store directly,
and reach the shim's cache, counters and ini code through delegates in AppDomain data.

## Notes
- None of the protected members above is listed in the API reference; they are in the `Altium.SDK` assembly
  metadata and the compiler accepts the overrides.
- The page is declared in an `OptionsPage` block of the `.ins`, the way AD's own extensions declare their pages
  (for example `TextEdit.ins`: a category block with nested pages whose `FormName` is the page name).
  There is no `SystemExtension`, so the server starts on first use. If the page is missing, run any command of the
  sample first and reopen Preferences.
- `RegisterOptionsPages` is called once, when the server module is created, not when Preferences is
  opened. `ShowPreference` reports how often it ran.
- AD calls the module's protected `ReceiveNotificationImpl`, so a .NET server module can receive
  notifications by overriding it, without a separate COM notification handler.
- The page's notification code is 1023, the sample's own number (the SDK's
  `DXP.NotificationCode.SystemNotificationCode` values end at 101). The sample saves on apply as well as on that
  notification, so the value is kept either way; the counters in `ShowPreference` tell which path ran.
- `ServerOptionsWriter` + `Flush` is visible to a new `ServerOptionsReader` opened afterwards, and `EraseSection`
  removes the value (the reader then reports `ValueExists: False` and the caller's default).
- `SpecialFolder_ExportPreferences` returns an existing folder with a trailing separator. `Path.Combine` is still the
  safe way to build the file path.
- `ExportPreferencesFolder WriteIni` writes the value the page has cached (`Cached by the page` in
  `ShowPreference`), not a fresh read of the store. The folder must exist; the command does not create it.
- Importing (`LoadSettingsFromFilesImpl`) only updates the page's cache. The value is stored on the next apply
  of the page or the next notification.
- Temperature is free text. Nothing parses or validates it.
- The value lives in the options of server `PreferencesOptionsPage`, not in a design: no undo, and it survives AD
  restarts. `SetPreference Erase` removes it again (back to the default `'100'`).
- The Preferences dialog is modal: while it is open, pipe commands wait.
- All shim callbacks catch their exceptions and record them as `Last error`, so a failure does not raise a dialog
  inside AD's Preferences or notification code.
- Hot reload: only the commands reload. A change to the page, the data manager or the `.ins` needs a redeploy
  and an AD restart.
