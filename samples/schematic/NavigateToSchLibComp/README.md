# Open a placed component's source SchLib symbol

Adds **Open SchLib Component** to the schematic right-click menu. It opens the `.SchLib` that a placed component
came from and shows that component's symbol in the library editor. It shows how to open a library as a document
and make one of its symbols the current one, and how a context menu item finds the component that was right-clicked.

## What it shows
- `SCH.ISch_Document.PopupMenuHitTest`: the objects under the right-click position, as an `SCH.ISch_HitTest`
  (`SCH.ISch_HitTest.HitTestCount`, `SCH.ISch_HitTest.HitObject`). Used both in the menu item's GetState and in the command.
- `SCH.ISch_BasicContainer.ObjectId`: check the object kind before casting a hit object to `SCH.ISch_Component`
  (a C# type test succeeds for every SDK object).
- `SCH.ISch_Component.SourceLibraryName` and `SCH.ISch_Component.LibReference`: the library file name and the
  symbol name stored on the placed component.
- `DXP.IClient.OpenDocument` with kind `"SCHLIB"`, after `DXP.IClient.GetDocumentByPath` to reuse a library that
  is already open.
- `SCH.ISch_ServerInterface.GetSchDocumentByPath`: the schematic editor's object for the opened file, checked to be
  an `SCH.ISch_Lib` by its `ObjectId` (`eSchLib`).
- `SCH.ISch_Lib.SchComponentByLibRef`: find the symbol by name.
- `SCH.ISch_Lib.Current_SchComponent`, `SCH.ISch_GraphicalObject.GraphicallyInvalidate`, `DXP.IClient.ShowDocument`:
  make the symbol the library's current component, redraw, bring the library to the front.
- `DXP.CommandLauncher.RegisterCommand` with a GetState procedure (`DXP.GetStateProc`), and a `PUSCHMENU`
  insertion in the `.rcs`: a right-click menu item that is shown only over a component.

## Run it
The context menu item lives in the extension itself: deploy it with `tools/deploy.ps1 -Sample NavigateToSchLibComp`
and restart Altium Designer.

1. Open a `.SchDoc` whose components come from a `.SchLib` you have on disk, for example `SL_Power.SchDoc` of the
   SpiritLevel-SL1 example project that ships with Altium Designer (its components have `SourceLibraryName`
   `SpiritLevel_2E.SchLib`, which is in the `Libraries` folder beside the sheet).
2. Right-click a component, then **Open SchLib Component**. Or select one component and use
   **NavigateToSchLibComp › Open SchLib Component (Selected)**, or
   `tools/ad.ps1 -Sample NavigateToSchLibComp run NavigateToSchLibComp "Designator=U3"`.
3. `SpiritLevel_2E.SchLib` opens (or comes to the front) with the component's symbol (`LM1084IS-ADJ` for U3) shown.
   The command returns the component, the library path and the library's current component as read back.

| Command | Parameters | Result |
|---|---|---|
| `NavigateToSchLibComp` | `Designator=<x>` (wins when given), `Component=popup\|selected` (default `selected`; the context menu item passes `popup`), `LibraryFolders=<folder>[;<folder>...]` | Three lines: the component and its source library, the library path (opened or already open, folders searched), and `current component of the library is '<LibReference>'`. |
| `ListSheetComponents` | `Designator=<x>`, `Max=<n>` (default 50) | Helper: designator, `LibReference`, `SourceLibraryName` and selection of each component on the current sheet. |
| `CurrentLibraryComponent` | none | Helper: the current schematic document and, if it is a library, its current component. |

## How it works
`NavigateToSchLibComp/ComponentContextMenu.cs` is shim code. It registers an extra process,
`RunOnComponent`, with `CommandLauncher.RegisterCommand` and the GetState `GetState_ComponentUnderCursor`. The
GetState takes the current sheet from `SCH.GlobalVars.SchServer`, walks `PopupMenuHitTest` and sets `enabled` and
`visible` only when a hit object's `ObjectId` is `eSchComponent`. It catches every exception (and then hides the
item), because it runs inside AD's menu code. `RunOnComponent` forwards to the framework's `Run`. The `.ins`
declares `RunOnComponent`, and the `.rcs` links a PL item with `Cmd=NavigateToSchLibComp|Component=popup` into
`PUSCHMENU` after its first separator `MNSchematic_PUSCHMENU30`, followed by a separator.

`NavigateToSchLibComp.Impl/Commands/NavigateToSchLibCompCommands.cs` has the command. It keeps the current sheet,
finds the component (`Designator`, else the popup hit test or the single selected component), and reads
`LibReference` and `SourceLibraryName`. `ResolveLibrary` takes a rooted `SourceLibraryName` as it is; otherwise it
tries each `LibraryFolders` entry, then the sheet's folder and its `Libraries` subfolder, and takes the first file
that exists. The command reuses the library document if `GetDocumentByPath` finds it, else opens it with
`OpenDocument("SCHLIB", path)`. It gets the `ISch_Lib` with `GetSchDocumentByPath`, finds the symbol with
`GetState_SchComponentByLibRef`, calls `SetState_Current_SchComponent`, `GraphicallyInvalidate` and
`ShowDocument` in that order, and reads `GetState_Current_SchComponent` back for the result.

## Notes
- Read-only for the design: neither the sheet nor the library is changed. The library is left open.
- Only `.SchLib` sources are handled. A component from an integrated, database or server library gives an error
  that names its `SourceLibraryName`.
- The GetState is in the shim, not in the hot-reloaded implementation: it is called whenever AD builds the menu,
  and must not depend on the hot-reload host. Changing it, the `.ins` or the `.rcs` needs a redeploy and an AD restart.
- `RunOnComponent` is registered in the partial method `RegisterViews`, which the framework's constructor calls
  after `InitializeCommands`. AD finds a process registered there and dispatches its GetState.
- `PopupMenuHitTest` with no context menu open (scripted run, main menu) returns a non-null `ISch_HitTest` with
  `HitTestCount` 0, without an exception. For that reason the main menu item and `tools/ad.ps1` use the selection
  or `Designator`, never the hit test.
- `Current_SchComponent` still reads back the chosen symbol after `GraphicallyInvalidate` and `ShowDocument`.
  `CurrentLibraryComponent` reports what the library object says.
- The GetState hides the item when the current schematic document is a library, so the item never offers to open
  a library from inside one.
