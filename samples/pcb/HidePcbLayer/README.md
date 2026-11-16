# Hide a PCB layer in the board view

Hides one electrical layer of the current board's view (the top layer by default) and shows it again on request.
It shows how to walk the V7 layer stack, identify a layer by its `PCB.TV6_Layer` ID, and switch its display state
with `PCB.IPCB_LayerObject_V7.IsDisplayed`.

## What it shows
- `PCB.IPCB_Board.LayerStack_V7`: the board's layer stack in the V7 layer model.
- `PCB.IPCB_LayerStack_V7.FirstLayer` / `PCB.IPCB_LayerStack_V7.NextLayer`: the walk over the electrical layers
  (signal layers and internal planes), ending when `NextLayer` returns null.
- `PCB.IPCB_LayerObject_V7.LayerId`: the layer's `PCB.TV6_Layer` ID. The .NET `PCB.V7_LayerBase` has no
  `IsTopLayer` method, so the top layer is found by comparing with `PCB.TV6_Layer.eV6_TopLayer`.
- `PCB.IPCB_LayerObject_V7.IsDisplayed`: an indexed property whose index is the board, because the display state
  belongs to the board's view. Read with `GetState_IsDisplayed(board)`, set with `SetState_IsDisplayed(board, value)`.
- `PCB.IPCB_Board.Update_PCBGraphicalView`: redraws the view so the change shows.

## Run it
1. Open any `.PcbDoc` with a top layer (a two-layer board is enough), for example the board of the SpiritLevel-SL1
   example project that ships with Altium Designer, and make it the active document.
2. **HidePcbLayer › Hide Layer**, or `tools/ad.ps1 -Sample HidePcbLayer run HideLayer`
3. The command returns `<top layer name> (eV6_TopLayer): hidden (was displayed).`, for example
   `Top Layer (eV6_TopLayer): hidden (was displayed).` on the SpiritLevel-SL1 board. Show it again with
   `tools/ad.ps1 -Sample HidePcbLayer run HideLayer "Show=true"`.

| Command | Parameters | Result |
|---|---|---|
| `HideLayer` | `Layer=<name>` (optional, default the top layer), `Show=true` (optional, show instead of hide) | `<layer name> (<TV6_Layer>): hidden\|displayed (was hidden\|displayed).` |
| `ListLayerDisplay` | none | `<file>: <n> electrical layers, <k> hidden`, then one line per layer: `<layer name> (<TV6_Layer>): displayed\|hidden` |

`Layer` accepts the layer's name as `ListLayerDisplay` prints it (`Bottom Layer`, case-insensitive), or a
`PCB.TV6_Layer` name (`eV6_BottomLayer`, or `BottomLayer` / `Bottom Layer` without the prefix). A name match wins over
an ID match.

## How it works
`HidePcbLayerCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer` and its stack with
`GetState_LayerStack_V7`. `FindLayer` walks the stack with `FirstLayer`/`NextLayer`, first by `GetState_LayerName`,
then by `LayerId()`. `HideLayer` reads `GetState_IsDisplayed(board)`, sets `SetState_IsDisplayed(board, show)`,
calls `Update_PCBGraphicalView(false, true)` and reads the state back for the returned text. `ListLayerDisplay`
walks the stack once and prints each layer's state.

## Notes
- Only electrical layers (signal layers and internal planes) are in the V7 walk. Mechanical, overlay, mask and other
  layers cannot be chosen with this command; `PCB.IPCB_Board.LayerIsDisplayed` covers those.
- The change is not bracketed with `PreProcess`/`PostProcess`, because it is a view setting, not a design change. Hiding
  a layer does not mark the document modified.
- The reference says that `IsDisplayed` reads false for a null board, and setting it then has no effect. It doesn't
  say what happens for a board that has no view. The PCB basics guide ("Boards without a view") says that setting
  `PCB.IPCB_Board.LayerIsDisplayed` then has no effect. Whether `IsDisplayed` behaves the same is not documented.
- `ListLayerDisplay` is a helper; it reports the state after a change.
