# Show the license and subscription state

Reads the licensed state of the running Altium Designer and, when a valid license exists, the subscription
status and expiry date. It shows how to get the client's security manager and read licensing state without
changing it.

## What it shows
- `DXP.IClient.GetSecurityManager`: the client's security manager. In .NET it is an extension method.
- `DXP.ISecurityManager.GetLicensedState`: a text describing the available licenses.
- `DXP.ISecurityManager.AnyValidLicenses`: guards the subscription queries.
- `DXP.ISecurityManager.IsSubscriptionCurrent` and `DXP.ISecurityManager.AbsoluteSubscriptionExpiryDate`: the
  subscription status and its expiry. The expiry is typed `double`: an OLE Automation date, read
  with `DateTime.FromOADate`.

## Run it
1. No document is needed. Altium Designer must be running with any license state.
2. **LicenseState › Get License State**, or `tools/ad.ps1 -Sample LicenseState run GetLicenseState`
3. The result reads `Available Licenses - <state>. License subscription valid to <d-MMM-yyyy>` (or `expired.` /
   `expired on <date>`; with no valid license only the first sentence).

| Command | Parameters | Result |
|---|---|---|
| `GetLicenseState` | `Raw=true` (optional) | `Available Licenses - <state>. <subscription status>`; with `Raw=true` a second line `AnyValidLicenses=…\|IsSubscriptionCurrent=…\|AbsoluteSubscriptionExpiryDate=<number>` |

## How it works
`LicenseStateCommands.cs` takes `DXP.GlobalVars.Client` and calls `GetSecurityManager`. It starts the text with
`GetLicensedState`. If `AnyValidLicenses` is true it reads `AbsoluteSubscriptionExpiryDate` and
`IsSubscriptionCurrent`: a current subscription gives "valid to <date>", an expiry of 0 gives "expired." and any
other value "expired on <date>". The date is converted with `DateTime.FromOADate` and formatted `d-MMM-yyyy` in
the invariant culture. `Raw=true` appends the unconverted values.

## Notes
- Read-only. `DXP.ISecurityManager.RefreshLicenseInformation` and `DXP.ISecurityManager.UseLicense` change the
  licensing state and are deliberately not called.
- The SDK reference has no descriptions for `DXP.ISecurityManager`. That `AbsoluteSubscriptionExpiryDate` is a
  `TDateTime` (days since 1899-12-30) is an assumption. A valid license can return
  `AbsoluteSubscriptionExpiryDate=54789`, which `DateTime.FromOADate` reads as 1-Jan-2050, a whole day count that
  fits the `TDateTime` reading. The `Raw=true` output lets you check yours. A value outside the OLE date range is
  shown as a number.
- The text of `GetLicensedState` depends on the installed license, so it can't be asserted exactly. It can already
  end in a period, so the output may show ".." where ". " is appended.
