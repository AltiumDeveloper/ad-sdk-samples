# Read the signed-in user of each installed Vault

Lists the Vaults (workspaces) installed in Altium Designer with the signed-in user's full name and GUID for each,
and optionally the user the server manager reports. It teaches how to reach the current user through the vault
manager (`DXP.IEDMS_Vault.GetUserDetails`) and through the server manager (`DXP.IEDMS_DXPServerManager.GetUserInfo`).

## What it shows
- `EDP.Utils.GetVaultManager`: the vault manager, directly from the .NET API.
- `DXP.IEDMS_VaultManager.GetInstalledVaultCount` / `GetInstalledVault`: the installed vaults, by index.
- `DXP.IEDMS_Vault.GetVaultTypeID`: the vault type; entries of type `AltiumVault` (legacy Altium Vault) are
  skipped.
- `DXP.IEDMS_Vault.GetHRID` / `GetServiceURL`: which vault a line describes.
- `DXP.IEDMS_Vault.GetUserDetails`: the signed-in user as a `DXP.IEDM_User` (`FullName`, `GUID`). The types
  `IEDMS_InstalledVault` and `IEDMS_UserDetails` no longer exist in the SDK; the same calls are on
  `DXP.IEDMS_Vault` and `DXP.IEDM_User` (a mapping that is not documented).
- `EDP.Utils.GetDXPServerManager`, `DXP.IEDMS_DXPServerManager.IsConnected` / `GetUserInfo` / `GetEnterpriseVault`,
  `DXP.IEDMS_DXPServerUserInfo.GetFullName` / `GetUserName` / `GetDomain` / `GetGUID`, `DXP.IEDMS_Vault.GetUserGUID`:
  the server manager path (only with `Server=true`).

## Run it
1. Sign in to a workspace in Altium Designer, so that at least one Vault is installed. No document is needed.
2. **PortalUserInfo › Get User Info**, or `tools/ad.ps1 -Sample PortalUserInfo run GetUserInfo ""`
3. The result starts with `<n> installed Vault(s): <k> with a signed-in user, <s> skipped.`, then one line per
   vault: `[<i>] '<HRID>' (<service URL>) [<type ID>] user '<full name>' GUID <guid>`, or the reason it was
   skipped.

| Command | Parameters | Result |
|---|---|---|
| `GetUserInfo` | `Vault=<index>` (optional: only that vault), `Server=true` (optional: also the server manager's user) | header line, one line per vault, then a `Server manager:` block when `Server=true` |

## How it works
`PortalUserInfoCommands.cs` reads the optional `Vault` index and the `Server` flag, gets the vault manager from
`EDP.Utils.GetVaultManager()` and checks the installed vault count and the index. For each vault it calls
`GetInstalledVault(i)`, reads `GetVaultTypeID()`, skips `AltiumVault` entries, and calls `GetUserDetails()`; a null
user is reported as skipped. A `COMException` from one vault is reported on that vault's line, so the other vaults
are still listed. With `Server=true`, `DescribeServer` gets `EDP.Utils.GetDXPServerManager()`, checks
`IsConnected()`, reads `GetUserInfo()` and then `GetEnterpriseVault()` with its `GetUserGUID()` and
`GetUserDetails()`.

## Notes
- Read-only: nothing is changed.
- The output contains personal data (full names, user names, GUIDs). Redact it before sharing.
- The password is never read: the sample does not call `DXP.IEDMS_DXPServerUserInfo.GetPassword`, on purpose.
- `GetUserDetails` returns null, without an exception, for an Altium 365 workspace the user is not signed in to.
  `GetVaultTypeID` returns `LocalVault` for Altium 365 workspaces and `AltiumVault` for the Altium Content Vault,
  which is still installed. Each workspace gives the same person a different user GUID. For the server manager's
  enterprise vault, `GetUserGUID`, `GetUserDetails().GetGUID()` and `GetUserInfo().GetGUID()` are the same value;
  `GetUserName` is the sign-in e-mail and `GetDomain` is empty.
- `GetUserDetails`, `GetUserInfo` and `GetEnterpriseVault` are listed by the API as extension methods; they need
  `using DXP;`.
