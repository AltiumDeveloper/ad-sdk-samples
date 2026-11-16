# Add a pin to a component's library symbol and reload it

Takes a component on the current schematic sheet, opens its source schematic library, adds a pin to its symbol,
saves the library and replaces the placed component with the symbol loaded again from the library. It shows the
round trip from a placed component to its `.SchLib` and back.

## What it shows
- `SCH.ISch_Document.PopupMenuHitTest`: the objects under the cursor when the schematic context menu opened;
  `SCH.ISch_HitTest.HitTestCount` / `SCH.ISch_HitTest.HitObject` walk them.
- `SCH.ISch_Component.SourceLibraryName` / `SCH.ISch_Component.LibReference`: which library and which symbol a
  placed component came from.
- `DXP.IClient.GetDocumentByPath` / `DXP.IClient.OpenDocument`: reuse or open the library (`"SCHLIB"` kind), then
  `SCH.ISch_ServerInterface.GetSchDocumentByPath` for the schematic editor's view of it, checked with
  `SCH.ISch_BasicContainer.ObjectId` = `SCH.TObjectId.eSchLib`.
- `SCH.ISch_Lib.SchComponentByLibRef`: the library symbol by name.
- `SCH.ISch_ServerInterface.SchObjectFactory` (`SCH.TObjectId.ePin`): a new pin, set up with
  `SCH.ISch_Pin.Designator`, `SCH.ISch_Pin.Name`, `SCH.ISch_Pin.Orientation` (`SCH.TRotationBy90.eRotate180`),
  `SCH.ISch_GraphicalObject.Location` and the symbol's part (`SCH.ISch_GraphicalObject.OwnerPartId`,
  `SCH.ISch_GraphicalObject.OwnerPartDisplayMode`), added with `SCH.ISch_BasicContainer.AddSchObject`.
- `SCH.ISch_RobotManager.SendMessage` with `SCH.SCHConstant.SCHMBeginModify` / `SCH.SCHConstant.SCHMEndModify`
  around the symbol change and `SCH.SCHConstant.SCHMPrimitiveRegistration` for the sheet's removed and added
  component, inside `DXP.IProcessControl.PreProcess` / `DXP.IProcessControl.PostProcess`.
- `DXP.IServerDocument.Modified` / `DXP.IServerDocument.DoFileSave`: mark the library modified and save it.
- `SCH.ISch_ServerInterface.LoadComponentFromLibrary`: a free copy of the symbol from the saved library, placed with
  `SCH.ISch_BasicContainer.RemoveSchObject` / `SCH.ISch_BasicContainer.AddSchObject`,
  `SCH.ISch_GraphicalObject.MoveToXY` and `SCH.ISch_GraphicalObject.SetState_xSizeySize`.

## Run it
1. Copy a project whose sheets use symbols from a `.SchLib` file (for example the SpiritLevel-SL1 example that
   ships with Altium Designer, whose libraries are in its `Libraries` folder) and open one of its sheets.
   **The library file is modified and saved.**
2. **EditSchComponent › List Sheet Components**, or `tools/ad.ps1 -Sample EditSchComponent run ListSheetComponents`,
   to see the designators and source libraries.
3. **EditSchComponent › Edit Sch Lib Component** with one component selected, or
   `tools/ad.ps1 -Sample EditSchComponent run EditSchLibComponent "Designator=R1|PinDesignator=99"`
4. The result names the component and the library, reports `library symbol pins <n> -> <n+1>` and
   `DoFileSave('SCHLIB') returned true`, and ends with a read-back of the replaced component with its pin count.

| Command | Parameters | Result |
|---|---|---|
| `EditSchLibComponent` | `Designator=<x>` (else the component under the context-menu cursor, else the one selected component), `Library=<.SchLib path>` (else found from `SourceLibraryName`), `LibraryFolder=<folder>` (searched first for a relative `SourceLibraryName`), `PinDesignator=0`, `PinName=0`, `PinX=400`, `PinY=330` (mils) | What was changed, plus a read-back from the sheet |
| `ListSheetComponents` | `Designator=<x>` (optional filter), `Max=50` | Designator, LibReference, SourceLibraryName, part, pin count and location (mils) of each component |

## How it works
`EditSchComponentCommands.cs`, `EditSchLibComponent`:

1. `FindComponent`: with `Designator` it iterates the sheet's components; without it, it reads
   `PopupMenuHitTest` and takes the first hit object whose `ObjectId` is `eSchComponent`, then falls back to the only
   selected component (`SCH.ISch_GraphicalObject.Selection`).
2. `ResolveLibrary`: `Library` wins. Otherwise a rooted `SourceLibraryName` is used as it is; a relative one is
   looked up in `LibraryFolder`, the sheet's folder and the sheet's `Libraries` subfolder.
3. The library is taken from `GetDocumentByPath` or opened with `OpenDocument("SCHLIB", path)`, then found in the
   schematic server with `GetSchDocumentByPath` and checked to be an `eSchLib`. `SchComponentByLibRef` finds the
   symbol. If it already has a pin with the requested designator, the command stops without changing anything.
4. The pin is created with `SchObjectFactory`, configured, and added to the symbol between `SCHMBeginModify` and
   `SCHMEndModify`, inside `PreProcess`/`PostProcess` on the library. The symbol is made the library's current
   component and the library is redrawn.
5. `SetModified(true)` and `DoFileSave("SCHLIB")`. A `false` result stops the command before the sheet changes.
6. `LoadComponentFromLibrary(libRef, path)` loads the symbol again. The placed component's location, part,
   orientation, mirroring and designator are read, then, inside `PreProcess`/`PostProcess` on the sheet, the old
   component is removed and registered, the new one is added, moved, given those values, resized and registered.
7. The sheet is iterated again and the component with the old designator is reported with its pin count.

`ListSheetComponents` is a read-only helper for checking the result.

## Notes
- **Not idempotent, and it writes to disk**: the library file is saved with the new pin. Work on a copy. A second run
  with the same `PinDesignator` stops with an error instead of adding a duplicate pin.
- The library is left open after the command. The sheet change is in memory only.
- `IClient.OpenDocument("SCHLIB", path)` is enough to make the library reachable through
  `ISch_ServerInterface.GetSchDocumentByPath`.
- The reloaded component is a fresh copy of the symbol: parameter values, comment and other properties that were
  changed on the sheet are not carried over. This sample carries over location, designator, part, orientation and
  mirroring.
- The reloaded component's `SourceLibraryName` is **not** the `.SchLib` it was loaded from: AD
  re-resolves it, for example to an installed integrated library with a symbol of the same name
  (`PassiveSMD.IntLib` for `RES`). A second run on the same component then needs `Library=<path to the .SchLib>`.
- `PopupMenuHitTest` only has data while the schematic context menu is open, and this sample does not add a
  context-menu entry. Outside a context menu it returns an empty hit test (0 objects, no error). From the main menu the command therefore uses the selected component;
  through `tools/ad.ps1` pass `Designator`.
- The pin location is in the symbol's coordinates (mils, converted with `EDP.Utils.MilsToCoord`); the default
  (400, 330) may overlap other pins.
- The pin colour is 0 (black).
- Which robot messages Altium Designer expects for adding a child object and for removing a component is not
  documented. The messages sent here are accepted without error.
- Only `.SchLib` sources are handled. A component from an integrated, database or workspace library is reported as
  such; pass `Library=<path to a .SchLib>` to use another file.
