# Place a Vault component by item revision HRID

Looks up a component item revision in one of the Vaults (workspaces) installed in Altium Designer by its HRID,
and starts placing it into the active document with `DXP.IDXPWorkSpace.DM_UseItemRevision`. It teaches the chain
of identifiers that call needs: the Vault GUID, the item GUID and the item revision GUID.

## What it shows
- `EDP.Utils.GetVaultManager`: the vault manager, directly from the .NET API, with no need to load
  `EDMSInterface.dll` and call an exported function through P/Invoke.
- `DXP.IEDMS_VaultManager.GetInstalledVaultCount` / `GetInstalledVault`: the installed vaults, by index.
- `DXP.IEDMS_Vault.ItemRevisionByHRID`: a synchronous lookup of one item revision (`DXP.IULB_ItemRevision`) by
  its human-readable ID, unlike the asynchronous `IEDMS_Vault` `Get*` requests.
- `DXP.IEDMS_Vault.GetVaultGUID`, `DXP.IULB_ItemRevision.ItemGUID`, `DXP.IULB_ItemRevision.GUID`: the three
  GUIDs `DM_UseItemRevision` takes. `DXP.IULB_ItemRevision.HRID` / `ItemHRID` name what was found.
- `DXP.IClient.CurrentView`: the active document the component goes into.
- `DXP.IClient.GetDXPWorkspace` / `DXP.IDXPWorkSpace.DM_UseItemRevision`: hand the revision to the workspace,
  which starts the placement; it returns `bool`.

## Run it
1. Sign in to a workspace, find the HRID of a component item revision in it (Components or Explorer panel) and
   note the index of the Vault that holds it. Open a .SchDoc and make it the active document.
2. **PlaceComponentFromVault › Place Vault Component** (the menu passes the example `Hrid=CMP-0001-00000-1`), or
   `tools/ad.ps1 -Sample PlaceComponentFromVault run PlaceVaultComponent "Hrid=<HRID>|Vault=<index>"`
3. The result names the item revision and its GUIDs and says whether `DM_UseItemRevision` started the placement.

| Command | Parameters | Result |
|---|---|---|
| `PlaceVaultComponent` | `Hrid=<item revision HRID>` (required), `Vault=<index>` (default 0), `DryRun=true` (look up only) | `Item revision '<HRID>' (item '<item HRID>') in Vault <i> '<vault>': vault <GUID>, item <GUID>, revision <GUID>.` followed by `DM_UseItemRevision returned true: placement started in <kind> '<file>'.` or `DryRun: not placed.` |

## How it works
`PlaceComponentFromVaultCommands.cs` reads `Hrid`, `Vault` and `DryRun`, gets the vault manager from
`EDP.Utils.GetVaultManager()`, checks the installed vault count and the index, and gets the vault with
`GetInstalledVault`. The vault's `GetHRID` only labels the messages, so a COM error there is caught. Unless
`DryRun` is set it requires an active document (`GetCurrentView().GetOwnerDocument()`).
It calls `ItemRevisionByHRID` and treats both a `null` result and a `COMException` as "not found". It reads the
Vault GUID, the item GUID and the revision GUID, and passes them to `DM_UseItemRevision` with an empty last
argument. A `false` result is reported as an error.

## Notes
- Modifies the design: the placed component is added to the active document (once placed).
- `DryRun=true` stops after the lookup, so the Vault side can be checked without a placement.
- `ItemRevisionByHRID` returns `null` (no COM error) for an unknown HRID, and the lookup returns within a few
  seconds. `ItemHRID` is the revision HRID without the revision suffix (`CMP-00001` for `CMP-00001-1`). The Vault
  GUID comes back upper-case, the item and revision GUIDs lower-case.
- The format of the last argument of `DM_UseItemRevision` (`argParams`) is not documented.
- `CMP-0001-00000-1` is an example HRID and will not exist in every Vault: pass your own `Hrid=`.
