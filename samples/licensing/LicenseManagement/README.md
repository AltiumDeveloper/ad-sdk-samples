# Read, use and release Altium Designer licenses

Reads the license state that the three licensing managers of the running Altium Designer report. It can also use
a license (from an .alf file, by name, by PLS serial number, or the License Server's on-demand licenses) and
release it again. The sample covers how an extension reaches `DXP.ILicenseManager`, `DXP.ISecurityManager` and
`DXP.ISecurityServerManager`, and which calls change the license state.

## What it shows
- `DXP.IClient.GetLicenseManager`, `DXP.IClient.GetSecurityManager`, `DXP.IClient.GetSecurityServerManager`:
  extension methods on `IClient` in .NET.
- `DXP.ISecurityManager.RefreshLicenseInformation`: updates the security manager's information after licenses
  were used or released. It is not read-only. On PLS and AIS it may clear used licenses without releasing them on
  the server.
- `DXP.ISecurityManager.AnyValidLicenses`, `IsSubscriptionCurrent`, `AbsoluteSubscriptionExpiryDate`,
  `GetLicensedState`, `AbsoluteSubscriptionActivationCode`, `GetLicenseUsage`: the license fields.
- `DXP.ILicenseManager.GetCurrentLicenseInfo` (two `out` strings), `SubscriptionExpiryDate`,
  `IsSubscriptionCurrent`: the license manager's view of the current licenses.
- `DXP.ISecurityServerManager.GetIsFreeLicenseMode` and `HasActiveSubscription`: the two getters of the
  security server manager.
- `DXP.ILicenseManager.UseLicense`, `UseLicenseByName`, `DXP.ISecurityManager.UseLicense` (serial number),
  `DXP.ISecurityServerManager.UseAvailableOnDemandLicenses`, `AddLicenseFile`: ways to use a license.
- `DXP.ILicenseManager.ReturnLeasedLicensesOnLogout`, `ReleaseLicense`,
  `DXP.ISecurityServerManager.ClearLicenses`, `FreeAllLicenses`, and `DXP.Utils.RunCommand` with
  `EDMSInterface:LogoutFromDXPServer`: ways to release one, which differ by server type.
- A dry-run-first guard (`Confirm=true`) on every command that changes the license state.

## Run it
1. No document is needed. Altium Designer must be running with a license.
2. **LicenseManagement › Check AD License**, or `tools/ad.ps1 -Sample LicenseManagement run CheckADLicense`
3. The result lists the license ids and names, the subscription expiry date and status, the security folder and its
   `.alf` files. `SecurityServerInfo` and `GetLicenseInfo Refresh=false` read the other two managers.
4. The use and release commands without `Confirm=true` only report what they would call. Run them with
   `Confirm=true` only on an installation whose licenses you can restore. They need the matching server (License
   Server, PLS or AIS) and, for `UseLicenseFile`, an `.alf` file.

| Command | Parameters | Result |
|---|---|---|
| `GetLicenseInfo` | `[Refresh=false]` | Refreshes (unless `Refresh=false`), then shows the six security-manager fields |
| `CheckADLicense` | `[SecurityFolder=<folder>]` | License ids, names, expiry, subscription state; the `.alf` files of the security folder (read-only) |
| `SecurityServerInfo` | none | `IsFreeLicenseMode`, `HasActiveSubscription` (read-only) |
| `ReleaseLicenses` | `Confirm=true` `[Names=true]` `[AlfFiles=true]` `[Files=<a.alf;b.alf>]` `[SecurityFolder=<folder>]` `[ClearLicenses=true]` `[FreeAll=true]` `[AisLogout=true]` | `ReturnLeasedLicensesOnLogout`, then the selected optional calls, then the state read back. Without `Confirm`: the list of calls |
| `UseLicenseFile` | `File=<.alf>` `Server=AIS\|LicenseServer` `Confirm=true` `[Keep=true]` | `UseLicense`, the state while in use, the release for that server type, the state after |
| `UseLicenseByName` | `Name=<license name>` `Confirm=true` | `UseLicenseByName`, then the state after (not released) |
| `UseLicenseBySerial` | `Serial=<PLS serial>` `Confirm=true` `[Keep=true]` | `ISecurityManager.UseLicense`, then `FreeAllLicenses`, then the state after |
| `UseOnDemandLicenses` | `Confirm=true` `[Force=true]` `[Keep=true]` | `UseAvailableOnDemandLicenses`, then `ReturnLeasedLicensesOnLogout`, then the state after |
| `AddLicenseFile` | `Confirm=true` `[Replace=true]` (the menu item passes `Confirm=true`) | File dialog. The serial number of the loaded license, then the state after. Menu only |

## How it works
`Licensing.cs` gets the three managers from `DXP.GlobalVars.Client`. It also holds the `Confirm` guard, the
read-back of the license-manager state, the date formatting, and the lookup of the security folder.

`LicenseManagementCommands.cs` has the read commands. `GetLicenseInfo` calls `RefreshLicenseInformation` and then reads the `ISecurityManager` getters. `CheckADLicense` calls `GetCurrentLicenseInfo`,
`SubscriptionExpiryDate` and `IsSubscriptionCurrent`, and lists `*.alf` in the security folder.
`SecurityServerInfo` reads the two `ISecurityServerManager` getters.

`LicenseUseCommands.cs` has the commands that change state. Each one first builds the list of calls it would
make and returns it when `Confirm=true` is missing. With `Confirm=true` it makes the calls, recording each call's
outcome in the returned text, and then reads the license-manager state back. The "use" commands release in a
`finally` block in the way that fits each server type: AIS through
`RunCommand("EDMSInterface:LogoutFromDXPServer", "")`, License Server through `ReturnLeasedLicensesOnLogout`,
PLS through `FreeAllLicenses`. `Keep=true` skips the release.

## Notes
- **These commands change the license state of the running Altium Designer**, and on a License Server they can
  change other users' seats. `UseOnDemandLicenses Force=true` can take seats from other users. Always look at
  the dry-run output first.
- The behaviour per server type described here and in the code comments (what each call does on a License
  Server, PLS or AIS) is not documented in the SDK reference. Treat it as indicative, not guaranteed.
- `ILicenseManager.ReleaseLicense` does not work for license names. For `.alf` files, it marks the license unused
  locally but does not release it on the server. It is offered (`Names=true`, `AlfFiles=true`, `Files=`) for
  checking, not as a working technique.
- `UseAvailableOnDemandLicenses` leases on a separate thread. Releasing right away can race with it. The sample
  does not wait on AD's UI thread. To see the leased licenses, use `Keep=true` and then `CheckADLicense` and
  `ReleaseLicenses`.
- The security folder has no .NET helper. The sample derives `%ProgramData%\<data folder>_Security` from the
  installation's key under `HKLM\SOFTWARE\Altium\Builds` (value `Application`, matched by `ProgramsInstallPath`).
  `SecurityFolder=<folder>` overrides it.
- `AbsoluteSubscriptionExpiryDate` and `SubscriptionExpiryDate` are `double` in .NET. They are OLE Automation dates
  (days since 1899-12-30, read with `DateTime.FromOADate`) and are shown that way, with the raw number next to them.
  A value of 54789 reads as 2050-01-01.
- `GetCurrentLicenseInfo` returns each string with a trailing line break.
- `EDMSInterface:LogoutFromDXPServer` is not in the .NET reference.
- The meaning of `GetIsFreeLicenseMode` is undocumented.
- Not covered: `ILicenseManager.ChangeToNetwork` / `ChangeToStandalone` and
  `ISecurityServerManager.AutoLeaseNewOnDemandLicensesByTimer`, which are not implemented.
- `AddLicenseFile` opens a modal dialog, so it refuses to run through `tools/ad.ps1`; start it from the menu.
