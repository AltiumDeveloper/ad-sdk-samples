# Replace sheet components with a library component

Replaces the components on the current schematic sheet with a component loaded from a library. Each new one is
placed at the old component's location. The sample shows how to load a library component as a free object, take
objects off a sheet and put new ones on it, and tell the schematic editor about both changes.

## What it shows
- `SCH.ISch_ServerInterface.LoadComponentFromLibrary`: loads a component by library reference and library name.
  It returns a new component that belongs to no document yet, or null.
- `SCH.ISch_ServerInterface.LoadComponent`: the same through an `EDP.TLibIdentifierKind` (for example
  `eLibIdentifierKind_FullPath`), with the library reference passed as the design item ID (`Loader=` parameter).
- `SCH.ISch_Iterator.AddFilter_ObjectSet` with `SCH.TObjectId.eSchComponent`: collect the sheet's components
  *before* changing the sheet. Never remove objects while an iterator is still walking the sheet.
- `SCH.ISch_BasicContainer.RemoveSchObject` / `AddSchObject`: take the old component off the sheet and put the
  new one on it.
- `SCH.ISch_GraphicalObject.MoveToXY` / `SetState_xSizeySize` / `Location`: place the new component at the old
  component's origin and recompute its bounds.
- `SCH.ISch_RobotManager.SendMessage` with `SCH.SCHConstant.SCHMPrimitiveRegistration`, bracketed by
  `DXP.IProcessControl.PreProcess` / `PostProcess`: announce the removed and the added component to the editor.

## Run it
1. **Work on a copy.** Open a copy of a `.SchDoc` with components (for example `SL_Power.SchDoc` of the
   SpiritLevel-SL1 example that ships with Altium Designer) and make it the active document. Find a library that
   contains the component you want: by default `Battery` from `Miscellaneous Devices.IntLib`. Any `.SchLib` or
   `.IntLib` works if you pass one of its library references.
2. **ChangeSchComponent › List Sheet Components** to see what is there, then
   `tools/ad.ps1 -Sample ChangeSchComponent run ChangeSchComponent "LibRef=Battery|Library=<full path>\Miscellaneous Devices.IntLib"`
   (or **ChangeSchComponent › Change Sch Component** for the defaults).
3. Every component (or the first `Max`) is now a `Battery` at the old location. The text lists the first ten
   replacements and a read-back count. **List Sheet Components** shows `LibReference='Battery'` for them.

| Command | Parameters | Result |
|---|---|---|
| `ChangeSchComponent` | `LibRef=<name>` (default `Battery`), `Library=<file name or full path>` (default `Miscellaneous Devices.IntLib`), `Loader=Library\|FullPath\|Any\|NameNoType\|NameWithType` (default `Library`), `Max=<n>` (default all) | `<file>: replaced <k> of <n> component(s) with '<LibRef>' ...`, up to ten `<designator> '<old>' at (x, y) -> '<new>'` lines, `Read back: <m> component(s) on the sheet, <j> with LibReference '<LibRef>'.` |
| `ListSheetComponents` | `Max=<n>` (default 200) | `<file>: <n> component(s):` and one line per component with designator, `LibReference`, `DesignItemId`, `SourceLibrary` and location |

## How it works
`ChangeSchComponentCommands.cs` gets the current sheet from `SCH.GlobalVars.SchServer` and rejects schematic
libraries by their object ID. It collects the components with a filtered `SCH.ISch_Iterator` and destroys the
iterator. Then it loads one replacement. If that fails it stops with an error before anything is changed.
Inside `PreProcess`/`PostProcess` it does this for each old component: read its location, `RemoveSchObject`, send
`SCHMPrimitiveRegistration` for the removed component, `AddSchObject` the new one, `MoveToXY`,
`SetState_xSizeySize`, send `SCHMPrimitiveRegistration` for the new one, and `GraphicallyInvalidate`. Each
replacement after the first is loaded again, because one component object can sit on the sheet only once.
Finally it iterates the sheet again and reports what is actually there.

## Notes
- This destroys sheet content. The new component keeps only the old component's location. Designator,
  parameters, orientation and part are not carried over (the replacement gets the library's default designator,
  such as `D?`), and wires no longer meet the new pins. The change is in memory only:
  close the sheet without saving to discard it.
- `Library` form: a **full file path** works, both for `LoadComponentFromLibrary`
  (`Loader=Library`) and for `Loader=FullPath`. A bare library file name returns null for a library that is not
  installed, with every `Loader` value.
  A failed load (also an unknown `LibRef`) returns null without a dialog: the command reports it and leaves the
  sheet unchanged.
- `SourceLibrary` of a replacement is not always the file it was loaded from: loaded with `Loader=Library` a
  symbol can report the source library recorded inside the symbol (for example an `.IntLib` it was once extracted
  from); loaded with `Loader=FullPath` it reports the file actually loaded from.
- The remove/add sequence with `SCHMPrimitiveRegistration` inside `PreProcess`/`PostProcess` leaves the sheet
  consistent (count, positions, redraw, modified flag, no dialogs). `GraphicallyInvalidate` is enough to redraw.
- `LoadComponentFromDatabaseLibrary` is not the call for a file library: in the .NET API it takes a database table
  and keys (`argLibraryName`, `argDatabaseTableName`, `argDatabaseKeys`), so it loads from a database library, not
  an `.IntLib`. Use `LoadComponentFromLibrary`.
- Locations are shown in internal schematic coordinates, unconverted (10000 units per mil).
