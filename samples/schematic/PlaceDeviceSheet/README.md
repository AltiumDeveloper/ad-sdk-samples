# Place a device sheet or Vault sheet on a schematic

Places a reusable sheet on the active schematic in two ways: a device sheet found by name in the device sheet
folders, placed as a sheet symbol by running the built-in `Sch:CreateSheetSymbolFromSheet` process without user
interaction, and a Vault sheet item revision found by HRID, handed to the workspace with `DM_UseItemRevision`.

## What it shows
- `EDP.Utils.GetDeviceSheetManager`: the device sheet manager, available directly in .NET (no need to load
  `IntegratedLibrary.dll` by hand).
- `SCH.IDeviceSheetManager.FindDeviceSheetPath`: resolve a device sheet name to its file in the registered folders.
- `DXP.Utils.MessageRouterSendCommandToModule`: run a built-in process (`Sch:CreateSheetSymbolFromSheet`) with a
  parameter string, here with `InteractiveCreate=False|InteractivePlace=False` and a location from
  `EDP.Utils.MilsToCoord`.
- `SCH.ISch_Iterator.AddFilter_ObjectSet` with `SCH.TObjectId.eSheetSymbol`, `SCH.ISch_SheetSymbol.SymbolType` and
  `SCH.ISch_SheetSymbol.SchSheetFileName`: list the sheet symbols before and after, since the process returns nothing.
- `EDP.Utils.GetVaultManager`, `DXP.IEDMS_VaultManager.GetInstalledVault`, `DXP.IEDMS_Vault.ItemRevisionByHRID`:
  find a Vault item revision by HRID.
- `DXP.IDXPWorkSpace.DM_UseItemRevision` with `DXP.IEDMS_Vault.GetVaultGUID`, `DXP.IULB_ItemRevision.ItemGUID` and
  `DXP.IULB_ItemRevision.GUID`: start placing that revision in the active document.

## Run it
1. Open a `.SchDoc` (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it active. For `PlaceDeviceSheet`, put a sheet named `TestDeviceSheet.SchDoc` (or any name you
   pass) in a folder registered as a device sheet folder (Preferences › Data Management › Device Sheets, or the
   `AddDeviceSheetPath` sample). For `PlaceSheetFromVault`, sign in to a workspace and pick the HRID of a schematic
   sheet item revision.
2. **PlaceDeviceSheet › Place Device Sheet**, or
   `tools/ad.ps1 -Sample PlaceDeviceSheet run PlaceDeviceSheet "Name=TestDeviceSheet|X=5000|Y=5000"`
3. The result names the resolved file and the new sheet symbol(s), with type, file name and location in mils.

| Command | Parameters | Result |
|---|---|---|
| `PlaceDeviceSheet` | `Name=<device sheet>` (required), `X=`/`Y=` (mils, default 5000), `DryRun=true` | `Device sheet '<name>' resolved to '<path>'. ... 1 new sheet symbol(s): <type> '<file>' at (<x>, <y>).` |
| `PlaceSheetFromVault` | `Hrid=<item revision HRID>` (required), `Vault=<index>` (default 0), `DryRun=true` | `Item revision '<HRID>' ... vault <GUID>, item <GUID>, revision <GUID>. DM_UseItemRevision returned true: placement started in '<file>'.` |

## How it works
`PlaceDeviceSheetCommands.cs`, `PlaceDeviceSheet`: checks for an active schematic (`SCH.GlobalVars.SchServer`,
`GetCurrentSchDocument`), gets the device sheet manager and calls `FindDeviceSheetPath(Name)`. A null or empty
result is "not found". With `DryRun` it stops there. Otherwise it lists the sheet symbols with a filtered
`ISch_Iterator`, builds the parameter string (`SymbolType=Device Sheet`, interactive steps off,
`LocationX`/`LocationY` from `MilsToCoord`, `SourceSheetID=<Name>`), sends it to `Sch:CreateSheetSymbolFromSheet`
for the current view, lists the sheet symbols again and returns the ones that are new.

`PlaceSheetFromVault`: gets the vault manager, checks the Vault index, checks that a schematic is the active
document, looks up the HRID with `ItemRevisionByHRID` (null and `COMException` both mean not found), then passes the
Vault, item and revision GUIDs to `DM_UseItemRevision` and reports its bool result.

## Notes
- `Sch:CreateSheetSymbolFromSheet` and its parameters are not documented in the SDK reference.
  `InteractiveCreate=False|InteractivePlace=False` places without a dialog and without a symbol on the cursor,
  `LocationX`/`LocationY` take internal coordinates (`MilsToCoord(5000)` gives a symbol at 5000 mils), and the
  symbol exists by the time `MessageRouterSendCommandToModule` returns, so counting objects before and after the
  call is reliable.
- The new symbol reads back as `eSheetSymbolType_DeviceSheet`, and its `SchSheetFileName` is the bare name
  (`TestDeviceSheet`), not the resolved path.
- `FindDeviceSheetPath`: the name is passed without the file extension and the full path of the matching file comes
  back. An unknown name gives a falsy result with no exception and no dialog; the reference says a blank string, and
  this sample treats null and empty as not found. A test for null alone would miss a missing sheet.
- `IEDMS_Vault.ItemRevisionByHRID` returns `null` for an unknown HRID against a signed-in workspace; it does not
  throw. The `COMException` branch in this sample is defensive.
- `DM_UseItemRevision` is undocumented: this sample passes an empty parameter string, and the reference does not say
  whether it places at once or attaches the sheet to the cursor.
- Both commands change the document in memory only; undo or close without saving to discard.
