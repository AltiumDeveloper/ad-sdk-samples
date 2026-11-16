# Reload a placed component from its source SchLib

Replaces a component on a schematic sheet with a fresh copy of its symbol from the source schematic library, at the
same position, from an item in the schematic right-click menu. It shows how to load a library symbol as a free
object and swap it for a placed one, with the changes announced to the schematic editor through the robot manager.

## What it shows
- `SCH.ISch_Document.PopupMenuHitTest`: the objects under the right-click position, as an `SCH.ISch_HitTest`
  (`SCH.ISch_HitTest.HitTestCount`, `SCH.ISch_HitTest.HitObject`); branch on `SCH.ISch_BasicContainer.ObjectId`
  (`SCH.TObjectId.eSchComponent`) before casting to `SCH.ISch_Component`.
- `SCH.ISch_Document.CountContextMenuObjects`: used by the context menu item's GetState to show the item only over a
  component.
- `DXP.CommandLauncher.RegisterCommand`: an extra process with its own GetState procedure (`DXP.GetStateProc`).
- `SCH.ISch_Component.SourceLibraryName` and `SCH.ISch_Component.LibReference`: which library file and which
  symbol a placed component came from.
- `SCH.ISch_ServerInterface.LoadComponentFromLibrary`: load a copy of a symbol from a `.SchLib` file. The copy is
  not on any sheet until it is added.
- `SCH.ISch_BasicContainer.RemoveSchObject` / `SCH.ISch_BasicContainer.AddSchObject`: take the old component off
  the sheet and put the new one on.
- `SCH.ISch_GraphicalObject.MoveToXY` and `SCH.ISch_GraphicalObject.SetState_xSizeySize`: place the copy where the
  old component was and recompute its size.
- `SCH.ISch_RobotManager.SendMessage` with `SCH.SCHConstant.SCHMPrimitiveRegistration` and
  `SCH.SCHConstant.BroadCast`, inside `DXP.IProcessControl.PreProcess` / `DXP.IProcessControl.PostProcess`: tell
  the editor about the removed and the added component.
- `SCH.ISch_Component.CurrentPartID`, `SCH.ISch_Component.Orientation`, `SCH.ISch_Component.IsMirrored`,
  `SCH.ISch_Component.SchDesignator`, `SCH.ISch_BasicContainer.UniqueId`: what the sample carries over from the old
  component.

## Run it
The extension adds a context menu item: deploy it with `tools/deploy.ps1 -Sample UpdateComponentFromSchLib` and
restart Altium Designer before the first run.

1. Copy the SpiritLevel-SL1 example project that ships with Altium Designer (the whole folder) to a work folder and
   open `SL_Power.SchDoc` from it. Its components come from `SpiritLevel_2E.SchLib` (`SourceLibraryName`), which is
   in the `Libraries` subfolder next to the sheet, so no `LibraryFolders` parameter is needed. Move a pin or a
   graphic of a placed component (or edit the symbol in the library and save it), so the update is visible.
2. Right-click the component: **Update Component From SchLib** is the item after the first separator. Or select one
   component and use **UpdateComponentFromSchLib › Update Component From SchLib**, or
   `tools/ad.ps1 -Sample UpdateComponentFromSchLib run UpdateComponentFromSchLib "Designator=R13"`.
3. The component is drawn again as the library defines it, at the same place, with the same designator. The result
   text lists the component before and after (location, part, orientation, pins, unique ID) and the library used.

| Command | Parameters | Result |
|---|---|---|
| `UpdateComponentFromSchLib` | `Designator=<x>` (optional; else the right-click hit, else the one selected component), `LibraryFolders=<folder;folder>` (optional), `KeepUniqueId=false` (optional) | `<sheet>: replaced component <des> '<libref>' (found by ...) with a copy from <path> ...`, then `Before:` and `After:` lines |
| `ComponentInfo` | `Designator=<x>` (optional, same lookup) | `<sheet>: <des> '<libref>' at (x, y) mils, part n, <orientation>, k pins, UniqueId <id>. SourceLibraryName='...' LibraryPath='...'` (read-only helper) |

## How it works
`UpdateComponentFromSchLib/ComponentContextMenu.cs` is shim code. It registers an extra process,
`RunOnComponent`, with a GetState that asks `CountContextMenuObjects` for components and sets `enabled` and
`visible` when there is one; every exception is caught there, because it runs inside AD's menu code.
`RunOnComponent` forwards to the framework's `Run`. The `.ins` declares the process, and the `.rcs` links an item
that calls it with `Cmd=UpdateComponentFromSchLib` into `PUSCHMENU`, after `MNSchematic_PUSCHMENU30`, followed by
a separator.

`UpdateComponentFromSchLib.Impl/Commands/UpdateComponentFromSchLibCommands.cs` has the commands.
`UpdateComponentFromSchLib` takes the current sheet, finds the component (`Designator=`, else the first
`eSchComponent` in `PopupMenuHitTest`, else the one selected component), and resolves `SourceLibraryName`: as it is
when it is a full path, otherwise in each `LibraryFolders` folder in order, then the sheet's folder and its
`Libraries` subfolder. It calls `LoadComponentFromLibrary(LibReference, path)` and checks that the copy has the same
`LibReference`. It reads location, part, orientation, mirroring, designator and unique ID from the placed component,
then, between `PreProcess` and `PostProcess`: `RemoveSchObject` and `SCHMPrimitiveRegistration` for the old component,
`AddSchObject`, `MoveToXY`, the carried-over values, `SetState_xSizeySize` and `SCHMPrimitiveRegistration` for the new
one. `GraphicallyInvalidate` redraws the sheet. Finally it finds the component on the sheet again by designator and
reports it. `ComponentInfo` reports the same values without changing anything.

## Notes
- Parameter values changed on the placed component (Comment, Value, and so on) are not carried over: the copy has
  the library's parameters. Display mode and graphical overrides are not carried over either.
- `LoadComponentFromLibrary` returns the symbol as the library stores it, including the `SourceLibraryName` written
  into the symbol: for SpiritLevel-SL1 that is `PassiveSMD.IntLib`, not the `.SchLib` the copy came from. Without
  correcting it, a component could be updated only once. The sample therefore copies `SourceLibraryName` and
  `LibraryPath` from the placed component.
- The unique ID is carried over by default, so the component keeps its link to the PCB. `KeepUniqueId=false` gives
  the copy the ID AD assigned.
- The change is in memory only; the sample never saves, and the sheet is marked modified. The docs example
  `ISch_BasicContainer.RemoveSchObject` says the removal has undo support with this bracket.
- The robot messages follow the docs example for the removal; the addition is add, move, recompute the size,
  register. Which messages AD expects for which change is not documented.
- `PopupMenuHitTest` with no context menu open reports 0 hits, so there is no stale hit; `tools/ad.ps1` and the main
  menu take `Designator=` or the selection.
- The sample treats both a null result of `LoadComponentFromLibrary` and a different `LibReference` as failures and
  changes nothing.
- A multi-part component: `Designator=` takes the first placed part with that designator, and only that part is
  replaced. On `SL_Power.SchDoc`, the two-part `U3` (`LM1084IS-ADJ`) is a single placed component.
- Mils use `EDP.Utils.CoordToMils` (10,000 internal units per mil), which matches schematic coordinates.
