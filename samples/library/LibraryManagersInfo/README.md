# List library, model type and supplier managers

Lists the available libraries, the registered model types and the supplier sources, and on request opens the
device sheet folder picker. It shows how to get the library and component-data managers from `EDP.Utils` and how to
read their index-based lists safely.

## What it shows
- `EDP.Utils.LoadIntegratedLibraryManager`: the integrated library manager (`SCH.IIntegratedLibraryManager`).
  The reference text calls the accessor "IntegratedLibraryManager"; this is its .NET name.
- `SCH.IIntegratedLibraryManager.AvailableLibraryCount` / `SCH.IIntegratedLibraryManager.AvailableLibraryPath`:
  the libraries on the Installed, Project and Search path tabs of the Available Libraries dialog, index 0 to
  count - 1.
- `EDP.Utils.LoadModelTypeManager`, `SCH.IModelTypeManager.ModelTypeCount`, `SCH.IModelTypeManager.ModelTypeAt`,
  `SCH.IModelType.Name` / `SCH.IModelType.Description`: the registered model types. `ModelTypeAt` is an extension
  method that returns the typed `SCH.IModelType`.
- `EDP.Utils.GetDeviceSheetManager`, `SCH.IDeviceSheetManager.ChooseDeviceFolder`: a modal folder picker. It returns
  `false` when the user cancels, and the chosen folder through its `ref` parameter.
- `EDP.Utils.GetSupplierManager`, `SCH.ISupplierManager.GetSupplierSourceCount`,
  `SCH.ISupplierManager.GetSupplierSource`, `SCH.ISupplier.SourceName`: the configured supplier sources.
  `GetSupplierSourceCount` throws a `COMException` saying the supplier search part of `ISupplierManager` is
  obsolete (see Notes), so this section reports `Supplier sources: not available (...)`.
- Modal dialogs and `tools/ad.ps1`: the picker only runs when `ctx.IsInteractive` is true.

## Run it
1. No document is needed. The output depends on the libraries installed in AD and the supplier sources configured.
2. **LibraryManagersInfo › List Library Managers**, or `tools/ad.ps1 -Sample LibraryManagersInfo run ListLibraryManagers`
3. The result has three sections, `Available libraries: <n>`, `Model types: <n>` and `Supplier sources: <n>`, each
   followed by one `[<index>] <value>` line per entry. Model type lines read `[<i>] <name> (<description>)`.
   The third section is `Supplier sources: not available (<AD's error message>)`.
4. **LibraryManagersInfo › List Library Managers, Choose Device Folder...** opens the Choose Device Sheet Folder
   dialog first. The result then also contains `Device sheet folder chosen: <folder>` or
   `Device sheet folder: dialog cancelled.`

| Command | Parameters | Result |
|---|---|---|
| `ListLibraryManagers` | none | the three lists, up to 50 entries each |
| `ListLibraryManagers` | `Max=<n>` | at most `n` entries per list, then `... <k> more` |
| `ListLibraryManagers` | `ChooseDevice=true` | menu: opens the picker and reports the choice; `tools/ad.ps1`: `Device sheet folder: skipped (...)` |

## How it works
`LibraryManagersInfoCommands.cs` builds one report in four steps. Each step gets its
manager from `EDP.Utils` and reports `manager not available` if the accessor returns null:

1. `LoadIntegratedLibraryManager`, then `AvailableLibraryPath(i)` for `i` from 0 to `AvailableLibraryCount() - 1`.
2. `LoadModelTypeManager`, then `ModelTypeAt(i)` up to `ModelTypeCount() - 1`, with each type's `Name()` and
   `Description()`.
3. Only with `ChooseDevice=true` and an interactive call: `GetDeviceSheetManager`, then
   `ChooseDeviceFolder(ref folder)` with an empty start folder. Its `bool` result tells a choice from a cancel.
4. `GetSupplierManager`, then `GetSupplierSource(i)` up to `GetSupplierSourceCount() - 1`, with each
   `SourceName()`. A `COMException` from `GetSupplierSourceCount` is caught and reported in that section.

`Max` (default 50) caps each list so the message box stays readable.

## Notes
- Read-only: the command changes nothing, except what the user does in the device folder dialog.
- Indexes: the reference says that `AvailableLibraryPath` and `ModelTypeAt` count from 0.
  Both lists start at [0] with a real entry (a library path, `PCBLIB`), and the last index
  (count - 1) is a real entry too. For `GetSupplierSource` the base is unknown: `GetSupplierSourceCount` fails
  before any source can be read.
- The model types are `PCBLIB`, `FpgaPinInfo`, `PCB3DLib`, `SIM`, `IBIS` and `SI`.
- The SDK marks the extension method `SCH.ISupplierManager.GetSupplierSource` `[Obsolete]` with no message and no
  replacement (compiler warning CS0612). It is the only public way to get a supplier source by index, so the
  warning is suppressed at that single call. The supplier part does not work at all:
  `GetSupplierSourceCount()` throws `COMException (0x8000FFFF)` "Supplier Search functionality of ISupplierManager
  is obsolete. Please use interface IPartSearchManager". `SCH.IPartSearchManager` (`EDP.Utils.GetPartSearchManager`)
  only has dialog-style methods and no list of supplier sources, so this sample has no replacement for the listing.
- The code checks all four `EDP.Utils` accessors for null (for example before the schematic server is loaded); the
  three that run without the dialog return objects.
- The menu item with `ChooseDevice=true` is a second entry added to the `.rcs` by hand: the scaffold tool gives
  both entries the same ID when one command is listed twice.
