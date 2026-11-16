# Delete a named component from a schematic library

Deletes the component with a given library reference from the active schematic library. It shows how to check
that the active document really is a `.SchLib`, find a component by its library reference, and hand the deletion
to the built-in process `Sch:DeleteComponentFromLibrary` through `DXP.Utils.RunCommand`.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document, or none.
- `SCH.ISch_Document.ObjectId`: tells a library (`SCH.TObjectId.eSchLib`) from a sheet. A type test
  (`document as ISch_Lib`) succeeds for every schematic document, so it can't be used for this check.
- `SCH.ISch_Lib.LibIsEmpty`: guard before looking anything up.
- `SCH.ISch_Lib.SchComponentByLibRef`: finds a component by its library reference (`GetState_SchComponentByLibRef`).
- `SCH.ISch_Lib.SchLibIterator_Create` with `SCH.ISch_Iterator.AddFilter_ObjectSet`: walks the library's
  components (to count them before and after, and to list some names).
- `DXP.Utils.RunCommand`: launches the built-in process `Sch:DeleteComponentFromLibrary` with
  `Name=<library reference>|NoDialog=True`.

## Run it
1. Open a schematic library (for example a **copy** of `SpiritLevel_2E.SchLib` from the `Libraries` folder of the
   SpiritLevel-SL1 example project that ships with Altium Designer) and make it the active document.
2. `tools/ad.ps1 -Sample RemoveSchLibComponent run RemoveComponent "Name=<library reference>"`. The menu item
   **RemoveSchLibComponent › Remove Component** passes no name, so it only shows the "No component specified"
   message: use `tools/ad.ps1`, or edit the menu's parameters.
3. The command returns `Deleted '<name>' from the library (components: <n> before, <n-1> after).` and the
   component disappears from the library. An unknown name returns an error that lists up to 10 library
   references of the library.

| Command | Parameters | Result |
|---|---|---|
| `RemoveComponent` | `Name=<library reference>` (required) | `Deleted '<name>' ... (components: <before> before, <after> after)`, or `'<name>' is still in the library after Sch:DeleteComponentFromLibrary (...)`; errors for no name, no SchLib, an empty library, an unknown name |

## How it works
`RemoveSchLibComponentCommands.cs` reads the `Name` parameter (required, and must not contain `|`, the process
parameter separator). `CurrentLibrary` gets `SCH.GlobalVars.SchServer`, the current schematic document, and
checks its object ID for `eSchLib`. The command then checks `LibIsEmpty` and `GetState_SchComponentByLibRef`.
`CountComponents` iterates the library with `SchLibIterator_Create` filtered to `eSchComponent` (and destroys
the iterator). The deletion itself is `DXP.Utils.RunCommand("Sch:DeleteComponentFromLibrary", ...)`. Because
`RunCommand` returns nothing, the command re-queries the current library afterwards and reports whether the
component is gone and how the component count changed.

## Notes
- The library is changed in memory only: save it to keep the change, or close it without saving. Try it on a copy.
- `Sch:DeleteComponentFromLibrary` and its parameters `Name` and `NoDialog` are not in the SDK reference. Launched
  with `Name=<ref>|NoDialog=True`, it deletes the named component (the count drops by one and the name is no longer
  found) without a confirmation dialog, and it accepts the name in another letter case.
- `SCH.ISch_Lib.RemoveSchComponent` would remove the component directly. This sample goes through the editor
  command instead.
- The menu is inserted into the schematic editor's menu (`MNSchematicMenu`). `tools/ad.ps1` works regardless of
  menus.
- `GetState_SchComponentByLibRef` returns null (no exception) for an unknown name, and matches case-insensitively
  (`pcb_mount_switch` finds `PCB_MOUNT_SWITCH`).
