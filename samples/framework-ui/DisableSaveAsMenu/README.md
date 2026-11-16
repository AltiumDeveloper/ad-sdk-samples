# Remove Save As entries from the schematic File menu

Removes the Save As and Save As Copy entries from the schematic editor's File menu, without any code: two
`Deletion` elements in the extension's `.rcs` file edit a menu that Altium Designer itself defines. A stub command
reports what the extension removes.

## What it shows
- `.rcs` `Deletion` element (`TargetID` / `DeleteID`): remove a link from a menu tree that another server
  defines. Here `MNSchematic_File90` and `MNSchematic_File100` from the tree `MNSchematic_File` of AD's
  `System\advsch.rcs`, the links to `PLWorkspaceManager:SaveDocumentAs` and `PLWorkspaceManager:SaveDocumentAsCopy`.
- `.ins` `Updates = 'AdvSch'`: makes AD merge this extension's resources into the schematic menus at start-up,
  which is when the deletions take effect.
- A `[HotCommand]` stub that returns text.

## Run it
1. Deploy the extension with `tools/deploy.ps1 -Sample DisableSaveAsMenu` and restart Altium Designer (menu changes
   are merged at start-up; hot reload cannot show them).
2. Open any `.SchDoc` (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with
   Altium Designer) and open its **File** menu:
   Save As and Save As Copy are gone, Save and Save All still there.
3. **DisableSaveAsMenu › Disable Save As**, or `tools/ad.ps1 -Sample DisableSaveAsMenu run DisableSaveAs`: returns
   the text below.

| Command | Parameters | Result |
|---|---|---|
| `DisableSaveAs` | none | `Save As is disabled: this extension's .rcs deletes MNSchematic_File90 and MNSchematic_File100 (Save As, Save As Copy) from MNSchematic_File ...` |

## How it works
`DisableSaveAsMenu/Installation/DisableSaveAsMenu.rcs` ends with two `Deletion` lines, written in the same form as
the `Deletion` lines AD's own `advsch.rcs` uses. Each names the tree to edit (`TargetID`) and the item to remove
(`DeleteID`), and has its own element ID. Because the `.ins` lists `Updates = 'AdvSch'`, AD applies them when it
builds the schematic menus at start-up. `DisableSaveAsMenu.Impl/Commands/DisableSaveAsMenuCommands.cs` holds the
only command: it returns a fixed text naming the deleted IDs. Only the sample's own **DisableSaveAsMenu** menu runs
it; no entry of AD's File menu does.

## Notes
- This hides two menu entries; it does not prevent saving. Only the schematic editor's File menu is targeted.
  The PCB, library and other editors have their own File menus (`MNPCB_File100` in `AdvPcb.rcs`, for example)
  and keep Save As. Shortcuts, document tab menus and the Projects panel are not touched by this `.rcs`.
- `MNSchematic_File90` / `MNSchematic_File100` are AD's internal menu IDs (from AD's `advsch.rcs`), not a
  published contract. They may change between releases.
- `MNSchematic_File` is a tree nested in `MNSchematicMenu`. A `Deletion` may target such a nested tree: the
  start-up merge accepts it, although AD's own deletions target a top-level tree.
- The P-CAD-style schematic menu is a `TreeCopy` of `MNSchematicMenu` with the ID suffix `_PCAD`. It is not
  targeted here and keeps its Save As entries (`MNSchematic_File_PCAD` still has both). A `Deletion` applies to the
  named tree only, not to copies of it.
- The extension is not a system extension (`SystemExtension`). A .NET extension loaded during start-up can stop AD
  from starting if its shim fails. The menu merge still happens at start-up through `Updates`.
- The command's text is a fixed stub: it says "Save As is disabled" whether or not the start-up merge applied the
  deletions. Only the File menu itself shows the real effect.
- To undo: undeploy the extension and restart AD.
