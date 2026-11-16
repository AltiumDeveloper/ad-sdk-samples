# List the part choices of a Vault component

Finds a component item in the installed Vaults (workspaces) by its HRID and lists its part choices, the
manufacturer parts approved for it, from that Vault's part catalog. It teaches the chain from a Vault item to its
part choices: vault manager, installed vault, item by HRID, the vault's part catalog, the item's part choice list.

## What it shows
- `EDP.Utils.GetVaultManager`: the vault manager, directly from the .NET API.
- `DXP.IEDMS_VaultManager.GetInstalledVaultCount` / `GetInstalledVault`: the installed vaults, by index.
- `DXP.IEDMS_Vault.ItemByHRID`: a synchronous lookup of an item (`DXP.IULB_Item`) by its item HRID, without a
  revision suffix.
- `DXP.IEDMS_Vault.GetVaultGUID` / `GetHRID` and `DXP.IULB_Item.GetGUID`: the identifiers the part catalog needs.
- `EDP.Utils.GetPartCatalog`: the part catalog (`SCH.IPartCatalog2`) of one vault, keyed by its vault GUID.
- `SCH.IPartCatalog2.GetItemPartChoiceList`: the part choices of an item, keyed by the item GUID, as a
  `SCH.IPartChoice2ListResult`.
- `SCH.IPartCatalog2Result.GetIsSuccess` / `GetErrorMessage`: the outcome the result carries.
- `SCH.IPartChoice2ListResult.GetPartChoiceList`, `SCH.IPartChoice2List.GetPartChoice` (with `Count` from
  `DXP.IInterfaceList`, read through its accessor `GetCount()`): walk the list.
- `SCH.IPartChoice2.PartID` / `MPN` / `ManufacturerName` / `Description`: one approved manufacturer part.

## Run it
1. Sign in to a workspace in Altium Designer, so that at least one Vault is installed. No document is needed. You
   need the item HRID of a component that has part choices in that workspace.
2. **PartChoice › Get Part Choice Info** (the menu passes `Hrid=CMP-00029-00000`, which probably
   does not exist in your workspace), or
   `tools/ad.ps1 -Sample PartChoice run GetPartChoiceInfo "Hrid=<component item HRID>"`
3. The result names the item, its GUID and the vault it was found in, the number of part choices, then one line per
   part choice with its part ID, MPN, manufacturer and description.

| Command | Parameters | Result |
|---|---|---|
| `GetPartChoiceInfo` | `Hrid=<component item HRID>` (required), `Vault=<index>` (optional: search only that vault; default: all vaults in order) | `Item '<HRID>' (GUID <guid>) in Vault <i> '<vault HRID>': <n> part choice(s).` followed by `<k>. PartID: …, MPN: …, Manufacturer: …, Description: …` lines |

## How it works
`PartChoiceCommands.cs` reads `Hrid` (required) and the optional `Vault` index, gets the vault manager from
`EDP.Utils.GetVaultManager()` and checks that a vault is installed. It walks the installed vaults (or only the one
given), skips disabled ones in the full search, and calls `ItemByHRID` on each until one returns an item; a COM error from one vault is collected and the
search goes on. With the item found, it gets the vault's part catalog with `EDP.Utils.GetPartCatalog(GetVaultGUID())`,
calls `GetItemPartChoiceList(item GUID)`, checks `GetIsSuccess()`, then walks `GetPartChoiceList()` with `GetCount()`
and `GetPartChoice(i)` and formats one line per part choice. Every failure (no vault, item not found, no catalog,
no result, failed result) is reported with its own message.

## Notes
- Read-only: nothing in the vault or the part catalog is changed.
- Disabled vaults (`IEDMS_Vault.GetEnabled()` false): `ItemByHRID` still finds items in them, but
  `EDP.Utils.GetPartCatalog` then throws a `System.Exception` ("API_CreatePartCatalog returns error code
  -2147418113"). So the all-vault search skips disabled vaults. `Vault=<i>` still tries a disabled vault and
  reports that error.
- `ItemByHRID` returns null (no exception) for an unknown HRID, and `GetPartChoice(i)` works. A COM error from an
  unreachable vault is handled.
- The lookups run on Altium Designer's main thread and block it while each vault answers. Pass `Vault=` to search
  only one vault.
- The `IPartChoice2` records have more fields (`PartSourceName`, `PartChoiceType`, `GUID`, `ItemGUID`); the sample
  prints four of them.
