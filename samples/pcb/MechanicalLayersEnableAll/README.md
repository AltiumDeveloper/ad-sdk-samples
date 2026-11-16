# Enable all mechanical layers of the board

Switches every mechanical layer of the current PCB board on (or off) and lists the layers with their state. It
shows how to reach every mechanical layer of a board - including the disabled ones - and how to get at the
mechanical-layer-specific properties from the generic layer object the SDK hands out.

## What it shows
- `PCB.IPCB_Board.MechanicalLayerIterator` **visits only the mechanical layers that are currently enabled**.
  It is fine for reporting or disabling, but a loop built on it can never *enable* a
  layer: a disabled layer is not among the objects it returns.
- `PCB.IPCB_LayerStack.LayerObject`: therefore the commands walk `PCB.TV6_Layer.eV6_Mechanical1` ..
  `eV6_Mechanical16` through the board's layer stack, which also returns disabled layers.
- `PCB.IPCB_LayerObject`: `LayerObject` is typed as the generic layer object, so the result is narrowed to
  `PCB.IPCB_MechanicalLayer` with a type pattern (`is IPCB_MechanicalLayer layer`) to reach `MechLayerEnabled`.
- `PCB.IPCB_MechanicalLayer.MechLayerEnabled`: read with `GetState_MechLayerEnabled`, set with
  `SetState_MechLayerEnabled`.
- `PCB.IPCB_LayerIterator.First` / `PCB.IPCB_LayerIterator.Next`: the iterator moves with `bool` results; there is
  no `Destroy` call for layer object iterators.
- `PCB.IPCB_LayerObject.LayerName`: the layer's name for the report.
- `PCB.IPCB_Board.ViewManager_UpdateLayerTabs`: refreshes the editor's layer tabs after the change.

## Run it
1. Open any `.PcbDoc` (ideally one with some mechanical layers disabled), for example the board of the
   SpiritLevel-SL1 example project that ships with Altium Designer, and make it the active document.
2. **MechanicalLayersEnableAll › List Mechanical Layers**, or
   `tools/ad.ps1 -Sample MechanicalLayersEnableAll run ListMechanicalLayers`
3. **MechanicalLayersEnableAll › Enable All Mechanical Layers**, or
   `tools/ad.ps1 -Sample MechanicalLayersEnableAll run EnableAllMechanicalLayers`, then list the layers again:
   every layer now reads `enabled`.

| Command | Parameters | Result |
|---|---|---|
| `ListMechanicalLayers` | none | `<file>: <n> mechanical layers, <k> enabled.` then one line `#<i>: <name> - enabled/disabled` per layer, then how many of them `MechanicalLayerIterator` visits |
| `EnableAllMechanicalLayers` | `Enabled=true` (default) or `Enabled=false` (`1` / `0` also accepted) | `<file>: <c> of <n> mechanical layers enabled (<a> already enabled).` (or `disabled`), plus a `Failed:` line if a layer threw |

## How it works
`MechanicalLayersEnableAllCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`, with a clear
`UserException` when no PCB editor or PCB document is active. `MechanicalLayers(board)` yields the mechanical
layers: for each id from `eV6_Mechanical1` to `eV6_Mechanical16` it asks `GetState_LayerStack().LayerObject(id)`
and keeps the result if it is an `IPCB_MechanicalLayer`. `ListMechanicalLayers` reads `GetState_LayerName()` and
`GetState_MechLayerEnabled()` for each, and adds the count that `MechanicalLayerIterator` returns, so the
difference between the two is visible. `EnableAllMechanicalLayers` skips layers already in the requested state,
calls `SetState_MechLayerEnabled` on the others (catching a `COMException` per layer instead of stopping), then
calls `ViewManager_UpdateLayerTabs()` and returns the counts.

## Notes
- A loop that walks the mechanical layers with `MechanicalLayerIterator` and enables each one enables nothing: on
  the SpiritLevel-SL1 board the iterator returns only the enabled layers, and once they are disabled it returns none
  at all. With all 16 layers enabled it returns all 16. The layer-stack loop avoids this;
  the iterator is kept so the sample still shows it and its limitation.
- The change is **not** bracketed with `PCBServer.PreProcess` / `PostProcess` (a layer is not a primitive
  with `BeginModify`). The document is then *not* marked modified, so the user is not prompted to save it.
- Mechanical layers above 16 (the V7 range) are not covered: the loop uses the `TV6_Layer` mechanical range, as
  the SDK's own `IPCB_MechanicalLayer` example does.
- The change is made in memory. Save the document to keep it.
- `Enabled=false` disables mechanical layers: objects on them are no longer shown. Use it on a copy of a board.
