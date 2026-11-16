# Show Vault Explorer and search vault items by HRID

Shows a panel by name and searches the items of an installed Vault (workspace) by HRID with
`DXP.IEDMS_Vault.SearchItems`, then lists each hit's HRID, description and creator. It teaches the item search call:
its option and flag sets, and how to read the asynchronous item result.

## What it shows
- `DXP.GlobalVars.GUIManager` / `DXP.IGUIManager.SetPanelVisibleInCurrentForm`: show a panel by its name
  (`"Vault Explorer"`).
- `EDP.Utils.GetVaultManager`: the vault manager, straight from the .NET API, with no need to load
  `EDMSInterface.dll` and call its exported `API_GetEDMS_VaultManager`.
- `DXP.IEDMS_VaultManager.GetInstalledVaultCount` / `GetInstalledVault`: the installed vaults, by index.
- `DXP.IEDMS_Vault.SearchItems`: an item search with an HRID filter and a maximum count, returning a
  `DXP.IULB_ItemsResult`.
- `DXP.TEDMS_GetOptionSet` (`DXP.TEDMS_GetOption.eGetOption_GetEffectiveItemRevision`) and `DXP.TEDMS_VaultFlagSet`
  (`DXP.TEDMS_VaultFlag.edmsWait`, `edmsForceRefresh`): built with their constructors, which take the values.
- `DXP.IEDM_AsyncResult.WaitUntilDone` / `IsDone` / `GetOutput` and `DXP.IEDM_AsyncBasicResult.Result`
  (`DXP.IEDM_RecordResultList.Success`, `Message`, `GetFirstErrorMessage`): wait, then check the outcome.
- `DXP.IULB_ItemsResult.Items` (`DXP.IInterfaceList`): `Get(i)` returns `object`, cast to `DXP.IULB_Item`.
- `DXP.IULB_Item.HRID` / `Description` / `CreatedByName`: what each line shows.

## Run it
1. Sign in to a workspace in Altium Designer, so that at least one Vault is installed. No document is needed.
   Find the HRID of an item in that workspace (for example in the Explorer panel), or use a pattern such as
   `CMP-%`.
2. **VaultItemSearchByHrid › Browse Vault** (searches the example HRID `PCLCMP-0001-00010` in Vault 0), or
   `tools/ad.ps1 -Sample VaultItemSearchByHrid run BrowseVault "Hrid=<HRID>|Vault=<index>"`
3. The panel is requested (from the menu), and the result names the vault and the number of hits, then lists one
   `HRID  Description  CreatedByName` line per item and the number of item revisions in the result.

| Command | Parameters | Result |
|---|---|---|
| `BrowseVault` | `Hrid=<HRID>` (required), `Vault=<index>` (default 0), `Max=<count>` (default 100), `Timeout=<seconds>` (default 30), `ShowPanel=true\|false` (default: true from the menu, false from `tools/ad.ps1`), `Panel=<name>` (default `Vault Explorer`) | `Vault <i> of <n> '<HRID>' (<URL>): <count> item(s) for HRID '<Hrid>'.` then one `<HRID>  <Description>  <CreatedByName>` line per item and `<k> item revision(s) in the result.` |

## How it works
`VaultItemSearchByHridCommands.cs` reads the parameters (an empty `Hrid` is an error). If `ShowPanel` is on, it
calls `SetPanelVisibleInCurrentForm(Panel, true)` on `DXP.GlobalVars.GUIManager`. It gets the vault manager from
`EDP.Utils.GetVaultManager()`, checks the installed vault count and the index, and gets the vault. It calls
`SearchItems` with the HRID filter, five empty filters, the maximum count, an option set with
`eGetOption_GetEffectiveItemRevision`, a flag set with `edmsWait` and `edmsForceRefresh`, no callback and no
context. It waits with `WaitUntilDone(Timeout)`, reports a failure from `Result` or `GetOutput()`, then walks
`Items`, casts each entry to `IULB_Item` and returns one line per item.

## Notes
- Read-only: nothing in the vault, the files or the design changes. Only the panel visibility changes.
- `Vault Explorer` is still a valid panel name (the panel the UI calls Explorer; `Explorer` itself is not a valid
  name). `SetPanelVisibleInCurrentForm` opens it also without a focused form, `(name, false)` closes it, and an
  unknown name is ignored without an error, so a typo in `Panel=` goes unnoticed (`IsPanelValidInCurrentForm` can
  check a name first). `ShowPanel` stays off by default for `tools/ad.ps1` calls because it changes the user's
  layout.
- The HRID filter is a case-insensitive pattern in which `%` matches any run of characters and `_` one character
  (`CMP-00023%`, `CMP-00023-0000_`); `*` is not a wildcard. Without a wildcard it is an exact match. `Max` truncates
  the hit list silently. With `eGetOption_GetEffectiveItemRevision` the result holds one item revision per item.
- `edmsWait` makes EDMS requests block (as for `GetFolders`), a failed request still completes with
  `Success = false`, and some installed Vaults answer `SearchItems failed ...: Not supported.`, so `Vault=0` is not
  always a usable default: try another index.
- The search runs on Altium Designer's main thread and blocks it until it is done.
