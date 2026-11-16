# Place a pad that follows the cursor

Adds a new bottom-layer test-point pad to the current board and lets it follow the mouse until a left-click keeps it
or a right-click or Escape removes it. It shows how to create a PCB object with the factory, move it with the board's
cursor position, and drive an interactive placement from thread-local Windows mouse and keyboard hooks, without
blocking Altium Designer.

## What it shows
- `PCB.IPCB_ServerInterface.PCBObjectFactory`: create a new `PCB.IPCB_Pad` (`PCB.TObjectId.ePadObject`), then set
  its `PCB.IPCB_Primitive.Layer` (`eV6_BottomLayer`) and `PCB.IPCB_Primitive.IsTestPoint_Bottom` before adding it.
- `PCB.IPCB_Board.AddPCBObject` / `PCB.IPCB_Board.RemovePCBObject`: add the pad when the placement starts, remove it
  on cancel. Both notify the board, including its undo history.
- `PCB.IPCB_Board.XCursor` / `PCB.IPCB_Board.YCursor`: the cursor position in board coordinates, read on every mouse
  move to move the pad; `PCB.IPCB_Primitive.GraphicallyInvalidate` redraws it.
- `PCB.IPCB_Board.AddObjectToHighlightObjectList` / `PCB.IPCB_Board.Navigate_HighlightObjectList`: dim the rest of
  the board while the pad is being placed (`EDP.THighlightMethod.eHighlight_Dim`), and restore it afterwards.
- `PCB.IPCB_Primitive.Selected` and `DXP.Utils.RunCommand` (`PCB:DeSelect`, `Scope=All`): only the new pad is selected.
- `DXP.IClient.GetMainWindowHandle`: the main window, whose thread gets the Win32 `WH_MOUSE` and `WH_KEYBOARD` hooks.
- A placement that outlives its command: the command returns at once, the hooks finish the work later, and
  `SampleHooks.OnUnloadCore` removes the hooks when the implementation is unloaded.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document.
2. **PlacePadInteractive › Place Pad**, or `tools/ad.ps1 -Sample PlacePadInteractive run PlacePad`
3. The board is dimmed and a pad appears at the cursor. Move the mouse over the board: the pad follows the crosshair.
   Left-click to keep it; right-click or press Escape to remove it.
4. **PlacePadInteractive › Placement Status** reports how it ended, for example
   `Last placement: placed at (1250 mil, 830 mil) from the board origin (left-click)`, and the board's pad count.
   From the command line (nobody to click), end a placement with **Cancel Placement** (`PlacePadCancel`).

| Command | Parameters | Result |
|---|---|---|
| `PlacePad` | none | `Placing a bottom-layer test-point pad, now at (<x> mil, <y> mil) from the board origin. Move the mouse ...` (returns before the user clicks) |
| `PlacePadStatus` | none | `Placement in progress: pad on eV6_BottomLayer, IsTestPoint_Bottom = True, selected = True, at (...). <n> pads on the board.` or `No placement in progress. Last placement: <outcome>. <n> pads on the board.` |
| `PlacePadCancel` | none | `Placement cancelled, pad removed (PlacePadCancel).` or `No placement in progress. ...` |

## How it works
`Commands/PlacePadInteractiveCommands.cs` checks that a PCB document is active and that no placement is running, then
calls `PadPlacement.Start` and returns a short instruction text.

`PadPlacement.cs` holds one placement. `Start` deselects everything with `PCB:DeSelect`, creates the pad with
`PCBObjectFactory`, puts it on the bottom layer at `XCursor`/`YCursor`, marks it as a bottom test point, adds it with
`AddPCBObject`, dims the rest of the board and then selects the pad (the highlight navigation clears the selection,
so the order matters), all inside one `PreProcess`/`PostProcess` bracket. It then installs a `WH_MOUSE` and a
`WH_KEYBOARD` hook on the thread of the main window (`GetMainWindowHandle`, `NativeMethods.cs`). The hook delegates
are fields of the placement, so the garbage collector cannot free them while the hooks are installed.

The mouse hook moves the pad to `XCursor`/`YCursor` and calls `GraphicallyInvalidate` on `WM_MOUSEMOVE`. On
`WM_LBUTTONDOWN` it keeps the pad, on `WM_RBUTTONDOWN` it removes it; the keyboard hook removes it on an Escape key
press. Both go through `Finish`, which first removes the hooks, then, in a new `PreProcess`/`PostProcess` bracket,
either sets the final position between `BeginModify`/`EndModify` or calls `RemovePCBObject`, and restores the
highlighting with `eHighlight_Graph`. Exceptions never leave a hook procedure: they end the placement instead.
`PlacePadStatus` reads the placement state and counts the board's pads with a board iterator, as a read-back.
`SampleHooks.cs` cancels a running placement in `OnUnloadCore`.

## Notes
- **Why hooks.** The .NET API has no "drag this object until the user clicks" loop for an object the extension creates.
  `PCB.IPCB_Board.ChooseLocation` is the simpler built-in alternative: it shows a prompt and blocks until the user
  clicks a location, then returns it, but nothing follows the cursor. Running the editor's own `PCB:PlacePad` with
  `DXP.Utils.RunCommand`, then finding the new pad by comparing the pad lists before and after, is another.
- **Hooks must always be removed.** A hook whose procedure belongs to an unloaded assembly crashes Altium Designer
  on the next message. `Finish` removes the hooks before anything else, and `OnUnloadCore` calls it when the
  implementation is hot-reloaded or unloaded. Release builds are never unloaded while Altium Designer runs.
- **The command cannot report the result.** It returns before the user clicks; use `PlacePadStatus`. From the
  command line nobody can click, so the placement stays active until `PlacePadCancel`, an unload, or a click in
  Altium Designer.
- **Selection order.** `Navigate_HighlightObjectList` with "clear existing" clears the selection: select the pad
  after it, not before.
- When the mouse is outside Altium Designer, `XCursor`/`YCursor` hold the last position the PCB view saw, so a
  placement started from the command line puts the pad there.
- The clicks and the Escape key are passed on to Altium Designer (`CallNextHookEx`).
- The mouse hook runs before Altium Designer handles the message, so `XCursor`/`YCursor` may still hold the position
  of the previous move. The hook reacts to messages for every window of the thread, including
  panels and menus: a click on a panel also ends the placement.
- **Undo.** `AddPCBObject` and `RemovePCBObject` notify the board's undo history; a cancelled placement leaves an
  add and a remove there, and the document shows as modified. The moves while dragging are not bracketed (they are
  a preview); only the final position is, between `BeginModify` and `EndModify`.
- The pad keeps the factory's default size and shape; set them on the pad before `AddPCBObject` to change them.
- Restoring the highlight with `eHighlight_Graph` and "clear existing" returns the board to its normal colors, also
  after the pad was removed.
- Escape is handled on key press only.
