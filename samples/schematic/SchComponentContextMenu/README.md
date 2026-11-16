# Context menu item for the schematic component under the cursor

Adds an item to the schematic editor's right-click menu that appears only when the right-click lands on a
component, and reports that component's designator. It shows how a command's GetState procedure controls a context
menu item, and how to find the objects under the right-click position.

## What it shows
- `DXP.CommandLauncher.RegisterCommand`: register a process with its own GetState procedure (`DXP.GetStateProc`),
  which AD calls whenever it builds a menu holding the process, to set its enabled and visible state.
- `SCH.ISch_Document.CountContextMenuObjects`: how many objects of the given kinds (`SCH.TObjectSet` with
  `SCH.TObjectId.eSchComponent`) are under the right-click position. The GetState shows the item only when it is > 0.
- `SCH.ISch_Document.PopupMenuHitTest`: the objects under the right-click position, as an `SCH.ISch_HitTest`
  (`SCH.ISch_HitTest.HitTestCount`, `SCH.ISch_HitTest.HitObject`).
- `SCH.ISch_BasicContainer.ObjectId`: branch on the object kind before casting a hit object to
  `SCH.ISch_Component` (type tests succeed for every SDK object).
- `SCH.ISch_Component.SchDesignator` and `SCH.ISch_BasicContainer.Text`: the designator text.
- A `PUSCHMENU` insertion in the `.rcs`: the schematic editor's right-click menu.

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample SchComponentContextMenu` and Altium Designer
restarted.
1. Open a `.SchDoc` with placed components (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project
   that ships with Altium Designer).
2. Right-click a component: the first item of the menu is **My Sch Component Command**. Right-click empty space:
   the item is not shown.
3. Click it: a message box shows `<file>: component under the context menu: <designator> (LibReference <ref>) (hit
   objects: n, context-menu components: k).`

The same command runs from **SchComponentContextMenu › Component Under Context Menu**, or
`tools/ad.ps1 -Sample SchComponentContextMenu run ComponentUnderContextMenu`. No context menu is open then, so it
reports whatever AD keeps from the last right-click.

| Command | Parameters | Result |
|---|---|---|
| `ComponentUnderContextMenu` | none | `<file>: component under the context menu: <designator> (LibReference <ref>) (hit objects: n, context-menu components: k).` or `<file>: no component under the context-menu position (...)` |

## How it works
`SchComponentContextMenu/ComponentContextMenu.cs` is shim code. It registers an extra process, `RunOnComponent`,
with `CommandLauncher.RegisterCommand` and the GetState `GetState_ComponentUnderCursor`. The GetState
gets the current sheet from `SCH.GlobalVars.SchServer`, calls `CountContextMenuObjects` with a set holding
`eSchComponent`, and sets `enabled` and `visible` when the count is above zero. It catches every exception (and then
hides the item), because it runs inside AD's menu code. `RunOnComponent` itself forwards to the framework's `Run`.
The `.ins` declares `RunOnComponent`, and the `.rcs` links a PL item that calls it with `Cmd=ComponentUnderContextMenu`
into `PUSCHMENU`, before its first item `MNSchematic_PUSCHMENU10`.

`SchComponentContextMenu.Impl/Commands/SchComponentContextMenuCommands.cs` has the command. It takes the current
sheet, counts the context-menu components the same way, then walks `PopupMenuHitTest` from index 0 and takes the
first hit whose `ObjectId` is `eSchComponent`. It returns the designator (`GetState_SchDesignator().GetState_Text()`),
the `LibReference`, and both counts.

## Notes
- The GetState is in the shim, not in the hot-reloaded implementation: it is called often, and must not depend on
  the hot-reload host. Changing it, the `.ins` or the `.rcs` needs a redeploy and an AD restart.
- `RunOnComponent` is registered in the partial method `RegisterViews`, which the framework's constructor calls
  after `InitializeCommands` (the framework has no other hook for extra processes). AD finds a process registered there and calls its GetState: the server module's command launcher
  (`Server.GetCommandLauncher().GetCommandState` with the bare process name) reports `RunOnComponent` as
  `enabled=False visible=False` when no component is under the position, while an unknown process name reports the
  default `True/True`.
- Querying the same state through `Client.GetCommandLauncher().GetCommandState("Server:Process", ...)` from inside
  an extension returns `False/False` even for processes that exist; use the server module's own launcher.
- With no context menu opened, `PopupMenuHitTest` returns a non-null `ISch_HitTest` with
  `HitTestCount` 0 and `CountContextMenuObjects` returns 0, so a run from the main menu or `tools/ad.ps1` before any
  right-click reports no component.
- The command is read-only: it changes neither the selection nor the document.
- `PopupMenuHitTest` is a member of `SCH.ISch_Document` (which `SCH.ISch_Sheet` extends), so no cast to
  `ISch_Sheet` is needed.
