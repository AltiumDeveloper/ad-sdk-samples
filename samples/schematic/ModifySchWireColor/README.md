# Recolor every wire on the active schematic sheet

Gives the wires of the current schematic sheet colours from a list, in turn, and returns what it changed. It shows
the basic pattern for changing schematic objects: a filtered iterator, the robot manager's begin/end modify
notifications around each change, and a redraw.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document, or none.
- `SCH.ISch_BasicContainer.SchIterator_Create` / `SchIterator_Destroy` with `SCH.ISch_Iterator.AddFilter_ObjectSet`
  and `SCH.TObjectSet`: visit only the wires (`SCH.TObjectId.eWire`), and destroy the iterator in a `finally`.
- `SCH.ISch_RobotManager.SendMessage` with `SCH.SCHConstant.SCHMBeginModify` / `SCHMEndModify`,
  `SCH.SCHConstant.BroadCast` and `SCH.SCHConstant.NoEventData`: tell the editor before and after each change.
- `SCH.ISch_GraphicalObject.Color` (`SetState_Color`): the wire's colour, in BGR format (`0x0000C8` is red).
- `SCH.ISch_Wire.UnderlineColor` (`SetState_UnderlineColor`): an alternative property to set; `Property=UnderlineColor`.
- `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: redraw the changed wire.
- `DXP.IProcessControl.PreProcess` / `PostProcess`: bracket the loop, as the docs example does (see Notes on undo).

## Run it
1. Open a schematic sheet with wires (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that
   ships with Altium Designer) and make it the active document.
2. **ModifySchWireColor › Modify Sch Wire Color**, or
   `tools/ad.ps1 -Sample ModifySchWireColor run ModifySchWireColor`
3. The wires turn red, green, blue in turn. The result names the sheet, the number of wires changed and the read-back
   colours of the first ten. **List Wire Colors** (`run ListWireColors`) shows the colours of all wires.

| Command | Parameters | Result |
|---|---|---|
| `ModifySchWireColor` | `Colors=<BGR hex,...>` (default `0000C8,00C800,C80000`), `Property=Color` (default) or `UnderlineColor`, `Zoom=true\|false` (default: true from the menu, false from `tools/ad.ps1`) | `<file>: <n> wire(s) recoloured (...). Read back:` and one line per wire (first ten) |
| `ListWireColors` | `Max=<n>` (default 200) | `<file>: <n> wire(s):` and `#<i>: Color=0x...... UnderlineColor=0x......` per wire |

## How it works
`ModifySchWireColorCommands.cs` gets the active sheet from `SCH.GlobalVars.SchServer` and refuses a schematic
library (checked with `GetState_ObjectId`, since a type test succeeds for every document). It calls
`PreProcess` on the client's process control, creates an iterator filtered to wires, and for each wire sends
`SCHMBeginModify`, sets the next colour of the list with `SetState_Color` (or `SetState_UnderlineColor`), sends
`SCHMEndModify` and calls `GraphicallyInvalidate`. The iterator is destroyed and `PostProcess` called in a
`finally`. The returned text reads each colour back with `GetState_Color` / `GetState_UnderlineColor`. Finally,
for menu clicks or with `Zoom=true`, it runs the `SCH:Zoom` process with `Action=Document`.
`ListWireColors` walks the wires the same way, read-only.

## Notes
- The change is made in memory only. Save the document to keep it, or undo it.
- Colours are BGR integers (blue in the high byte, red in the low byte), as `SCH.ISch_GraphicalObject.Color`
  describes. `Colors` takes six hex digits per colour, with or without `0x`.
- `Color` is the colour the wire line is drawn in. `UnderlineColor` draws a wide band of that colour underneath the
  wire, behind the line, which keeps its `Color`.
- `SendMessage` with `SCHConstant.BroadCast` and `SCHConstant.NoEventData` around each change works: no error, and
  the document is marked modified.
- Undo: one undo can revert all wires of two runs at once, and a later undo may then revert nothing, so do not rely
  on `PreProcess`/`PostProcess` making one run exactly one undo step.
- A colour computed as `0x0000C8 << (n * 8)` does not cycle: C# masks the shift count to five bits, so wire 4 would
  get `0xC8000000` (not a valid BGR value) and wire 5 would start over at `0x0000C8`. This sample cycles through a
  list of valid colours instead.
- The zoom runs on the active view; from `tools/ad.ps1` it is off by default so a scripted run does not change the
  view. The sample does not call `SetFocus`/`Show` on the view.
