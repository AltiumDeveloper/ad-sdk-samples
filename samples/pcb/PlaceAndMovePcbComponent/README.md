# Place a footprint and start the interactive move

Creates a component from a PcbLib footprint, adds it to the current board, selects it and starts the PCB editor's
own move tool, so the user carries the new component on the cursor and clicks to drop it. It shows how an
extension prepares objects and a selection, then hands over to a built-in interactive process with
`DXP.Utils.RunCommand`.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document, or none.
- `DXP.Utils.RunCommand`: runs editor processes by name. `PCB:DeSelect` (`Scope=All`) clears the selection, and
  `PCB:MoveObject` (`Object=Component|Drag=True|ContextObject=Component`) starts the interactive move. Neither
  process nor its parameters is described in the SDK reference.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` with `PCB.TObjectId.eComponentObject`: creates an empty component.
- `PCB.IPCB_Primitive.Board` then `PCB.IPCB_Component.LoadFromLibrary` (`FileName=<path>|Footprint=<name>`):
  fills the component with the footprint's primitives. The `bool` result is checked.
- `PCB.IPCB_Primitive.Layer` (`PCB.TV6_Layer.eV6_TopLayer`) and `XLocation`/`YLocation`: where it goes.
- `PCB.IPCB_Board.AddPCBObject` between `PCB.IPCB_ServerInterface.PreProcess` and `PostProcess`: adds it.
- `PCB.IPCB_Primitive.Selected` and `PCB.IPCB_Primitive.GraphicallyInvalidate`: select and redraw it, so the move
  tool picks it up.

## Run it
1. Open a `.PcbDoc` that has a `.PcbLib` in its folder or a direct subfolder (for example a copy of the
   SpiritLevel-SL1 example project that ships with Altium Designer, whose `Libraries` folder holds
   `Spirit_Level_Project.PcbLib`), and make it the active document.
2. **PlaceAndMovePcbComponent › Place And Move Component**, or
   `tools/ad.ps1 -Sample PlaceAndMovePcbComponent run PlaceAndMoveComponent "Lib=Libraries/Spirit_Level_Project.PcbLib|X=100|Y=100"`
3. From the menu, the footprint of the board's first component is placed at the board origin and follows the
   cursor: click to drop it. From the command line, the move tool is not started (unless `Move=true`). The text
   gives the footprint, designator, pad count, location and component count before and after.

| Command | Parameters | Result |
|---|---|---|
| `PlaceAndMoveComponent` | `Lib=<.PcbLib>` (absolute or relative to the board folder; default the first `.PcbLib` next to the board), `Footprint=<name>` (default the footprint of the first component on the board), `X=<mil>`, `Y=<mil>` (from the board origin, default 0), `Move=true\|false` (default true from the menu, false from the command line) | `Placed '<footprint>' ... at X=.. Y=.. mil from the origin, selected True. Components on the board: n -> n+1.` With the move tool: also how long `PCB:MoveObject` took and where the component is afterwards. |

## How it works
`PlaceAndMovePcbComponentCommands.cs` gets the board from `PCB.GlobalVars.PCBServer`, resolves the library and
the footprint name (from the parameters, or from the board folder and the first component on the board), and
converts X/Y from mils to internal units (10,000 per mil) relative to the board origin. It runs `PCB:DeSelect`,
then, inside `PreProcess`/`PostProcess`, creates a component with `PCBObjectFactory`, assigns the board, calls
`LoadFromLibrary`, sets the top layer and the location, and adds it with `AddPCBObject`. If `LoadFromLibrary`
returns false, the component is destroyed with `DestroyPCBObject` and nothing is added. After the bracket it
selects the component, invalidates the view and, when `Move` is on, calls `RunCommand("PCB:MoveObject", ...)`
and times the call.

## Notes
- The move tool waits for a mouse click. Run it from the menu, or from the command line only with `Move=true`
  while someone is at Altium Designer. The returned text gives how long `RunCommand` took and where the component
  is afterwards.
- `LoadFromLibrary` does **not** need the `.PcbLib` open: an absolute `FileName` is
  enough. It returns `false` when the footprint is not in the library, so the empty component must be destroyed
  instead of added.
- The loaded component carries its pads and gets the designator `Designator1`. `GraphicallyInvalidate` on the
  board is enough to make it appear.
- `PCB:DeSelect` runs before the footprint is loaded, so a failed load still clears the current selection.
- The component stays on the board in memory only: close the document without saving to discard it.
