# Enable a mechanical layer and make it current

Enables a mechanical layer of the current board, marks it used and displayed, and makes it the current layer. It
shows that a mechanical layer is enabled through its `PCB.IPCB_MechanicalLayer` object (the board's layer calls
do not enable it), and how a `PCB.TV6_Layer` becomes the `PCB.V7_LayerBase` the board's layer calls take.

## What it shows
- `PCB.IPCB_Board.MasterStack`: the board's master layer stack.
- `PCB.IPCB_MasterLayerStack.CreateLayer`: returns the layer object for a layer ID. For a mechanical layer it
  returns the board's existing `PCB.IPCB_MechanicalLayer` and does not enable it.
- `PCB.IPCB_MechanicalLayer.MechLayerEnabled`: the setter that enables the mechanical layer, called on the object
  `CreateLayer` returned. This is the call that enables the layer.
- `PCB.IPCB_Board.LayerIsUsed` / `PCB.IPCB_Board.LayerIsDisplayed`: indexed by a `PCB.V7_LayerBase`, built with
  `new V7_Layer(TV6_Layer.eV6_Mechanical3)`. `LayerIsUsed` takes effect on a mechanical layer but does not enable it.
- `PCB.IPCB_Board.CurrentLayerV7`: the setter of the current layer. `PCB.IPCB_Board.CurrentLayer` is read-only.
  `PCB.IPCB_Primitive.Layer`, set on the board (`Current=Layer`), does not change the current layer.
- `PCB.IPCB_LayerStack.LayerObject`: also returns the layer object of a mechanical layer, used to read
  `MechLayerEnabled` back.
- `PCB.IPCB_Board.LayerName` and `PCB.IPCB_Board.ViewManager_FullUpdate`: the layer's name, and the redraw after the
  change.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium Designer)
   and make it the active document.
2. **MechanicalLayerEnable › Set Layer Visible**, or `tools/ad.ps1 -Sample MechanicalLayerEnable run SetLayerVisible "Layer=12"`
3. The result names the method used for the current layer, what `CreateLayer` returned, whether
   `SetState_MechLayerEnabled` was called, and a `Before:` and an `After:` line. For a layer that is already
   enabled, as Mechanical 3 in the SpiritLevel-SL1 board, it reads
   `After:  Name = 'Overlay boundary', MechLayerEnabled = True, LayerIsUsed = True, LayerIsDisplayed = True, CurrentLayer = eV6_Mechanical3`.
   For a disabled layer (Mechanical 12 there) `MechLayerEnabled` goes from False to True. **Get Layer State** reads
   the same line again without changing anything.

| Command | Parameters | Result |
|---|---|---|
| `SetLayerVisible` | `Layer=<1..16>` (default 3); `Current=<V7, Layer or None>` (default `V7`: `SetState_CurrentLayerV7`; `Layer`: `SetState_Layer` on the board, which does not change the current layer; `None`: skip); `Enable=<True or False>` (default `True`; `False` makes only the board-level calls, which do not enable the layer) | `<board>: eV6_MechanicalN, current layer set with <method>.`, the `CreateLayer` result, the `MechLayerEnabled` action, `Before:` and `After:` state lines |
| `GetLayerState` | `Layer=<1..16>` (default 3) | `<board>: eV6_MechanicalN: Name = '...', MechLayerEnabled = ..., LayerIsUsed = ..., LayerIsDisplayed = ..., CurrentLayer = ...` |

## How it works
`MechanicalLayerEnableCommands.cs`: `SetLayerVisible` turns `Layer=n` into `TV6_Layer.eV6_Mechanical1 + (n - 1)`
and wraps it in a `PCB.V7_Layer`. It reads the state, then, between `PCB.IPCB_ServerInterface.PreProcess` and
`PostProcess`, calls `GetState_MasterStack().CreateLayer(layer)`, casts the result to `PCB.IPCB_MechanicalLayer`
and calls `SetState_MechLayerEnabled(true)` on it (skipped with `Enable=False`), `SetState_LayerIsUsed(layer, true)`,
`SetState_LayerIsDisplayed(layer, true)`, and makes the layer current with `SetState_CurrentLayerV7(layer)`
(`Current=V7`) or `SetState_Layer(eV6_MechanicalN)` (`Current=Layer`). It redraws the board with
`ViewManager_FullUpdate` and reads the state again. `Describe` builds the state line: the layer name from
`IPCB_Board.LayerName`, `MechLayerEnabled` from `GetState_LayerStack().LayerObject(v6)` cast to
`IPCB_MechanicalLayer` (`n/a` if the cast fails), `LayerIsUsed`, `LayerIsDisplayed` and `CurrentLayer`.
`GetLayerState` returns that line only.

## Notes
- Only mechanical layers 1 to 16 are accepted: `PCB.TV6_Layer` has 16 mechanical values. Higher layers need a
  `PCB.V7_Layer` built another way; `PCB.V7_Layer.MechanicalLayer` exists but its index base is undocumented, so the
  sample does not use it.
- The board-level calls do not enable a disabled mechanical layer. For Mechanical 12 of the SpiritLevel-SL1 board,
  `CreateLayer` returns its `IPCB_MechanicalLayer` (`IsInLayerStack = False`), `SetState_LayerIsUsed` changes
  `LayerIsUsed` from False to True (although the reference says it has no effect for a layer not in the layer stack),
  and `MechLayerEnabled` stays False. `LayerIsDisplayed` is already True for the disabled layer. That is why the sample
  calls `SetState_MechLayerEnabled(true)`, which does enable it.
- `SetState_Layer` on the board does not change the current layer; `SetState_CurrentLayerV7` does.
- Calling the command twice is harmless: `CreateLayer` returns the same layer and nothing changes.
- The changes are in memory, and neither the board-level calls nor `SetState_MechLayerEnabled` mark the document
  modified. Close the document without saving to discard them.
- `LayerIsDisplayed` and `CurrentLayerV7` need the board's view: on a board without a view, setting them has no
  effect (docs: PCB basics, "Boards without a view").
- `GetLayerState` is a helper.
