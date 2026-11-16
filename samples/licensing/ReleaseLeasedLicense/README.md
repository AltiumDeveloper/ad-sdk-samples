# List and release leased license files

Lists the licenses of the running Altium Designer and the leased license files (`*.alf`) in its security folder,
and releases them, then switches the installation to standalone. It shows how to reach the license manager from an
extension and how to guard a destructive command with a dry run.

## What it shows
- `DXP.IClient.GetLicenseManager`: the client's license manager. In .NET it is an extension method on `IClient`.
- `DXP.ILicenseManager.GetCurrentLicenseInfo`: two `out` strings with the current license ids and names. Their
  format is undocumented, so the sample shows them as received.
- `DXP.ILicenseManager.ReleaseLicense`: releases one license, given the full path of its `.alf` file.
- `DXP.ILicenseManager.ChangeToStandalone` and `DXP.ILicenseManager.ReturnLeasedLicensesOnLogout`: called after
  the release.
- A dry run by default: the command that changes the license state does nothing without `Confirm=true`.

## Run it
1. No document is needed. To release for real you need a leased license (not a standalone one), and you will lose
   it: you may have to sign in or lease a license again afterwards.
2. **ReleaseLeasedLicense › List License Files**, or `tools/ad.ps1 -Sample ReleaseLeasedLicense run ListLicenseFiles`
3. The result shows `License ids: '…'`, `License names: '…'`, the security folder used, and the `.alf` files in it.
4. **ReleaseLeasedLicense › Release License** (always a dry run from the menu), or
   `tools/ad.ps1 -Sample ReleaseLeasedLicense run ReleaseLicense`: lists the files that would be released.
   Only `tools/ad.ps1 -Sample ReleaseLeasedLicense run ReleaseLicense "Confirm=true"` releases them.

| Command | Parameters | Result |
|---|---|---|
| `ListLicenseFiles` | `SecurityFolder=<folder>` (optional) | license ids and names, the security folder and how it was found, `<n> .alf file(s)` with name, size and date |
| `ReleaseLicense` | `SecurityFolder=<folder>` (optional), `Confirm=true` (required to change anything) | without `Confirm`: `Dry run, nothing changed.` and the files that would be released. With `Confirm`: one line per file (`Released …` or `Failed to release …`), a summary, then the license info and `.alf` files read back |

## How it works
`ReleaseLeasedLicenseCommands.cs` takes `DXP.GlobalVars.Client` and calls `GetLicenseManager`. The security
folder is the `SecurityFolder` parameter if given. Otherwise it is derived from the running installation: the
subkey of `HKLM\SOFTWARE\Altium\Builds` whose `ProgramsInstallPath` is the folder of the running executable gives
the data folder (`Application`, relative to `%ProgramData%`), and `_Security` is appended to it. The `.alf` files
are listed with `DirectoryInfo.GetFiles`.

`ListLicenseFiles` returns `GetCurrentLicenseInfo` and the file list. `ReleaseLicense` without `Confirm` returns
the plan only. With `Confirm` it calls `ReleaseLicense` with each file's full path. A failure of one file is
reported and the others are still released. Then it calls `ChangeToStandalone` and `ReturnLeasedLicensesOnLogout`,
also when no file was found, and reads the license info and the folder again.

## Notes
- **Destructive.** `ReleaseLicense "Confirm=true"` gives up the installation's leased license. The menu item
  cannot pass `Confirm`, so from the menu it is always a dry run.
- Whether `ReleaseLicense` deletes the `.alf` file, what it does with a missing, foreign or standalone license file,
  and what `ChangeToStandalone` and `ReturnLeasedLicensesOnLogout` do are undocumented. `ChangeToStandalone` and
  `ChangeToNetwork` may not be fully implemented. The read-back after `Confirm` is there so that you can see the
  effect.
- The `<data folder>_Security` name is not documented. Matching the running executable's folder against
  `ProgramsInstallPath` finds the right installation, and its `_Security` folder holds the leased `.alf` file. On
  another setup, pass `SecurityFolder=<folder>` if the detection fails.
- `GetCurrentLicenseInfo` returns each string with a trailing line break (`<id>\r\n`, `<name>\r\n`) for one
  license; how several licenses are separated is undocumented.
- The SDK reference has no description for `ReleaseLicense`, `GetCurrentLicenseInfo` or
  `ReturnLeasedLicensesOnLogout`.
