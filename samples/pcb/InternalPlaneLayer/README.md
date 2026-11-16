# Iterate a board's internal planes and read pull-back

Lists the internal plane layers of the current PCB board with their pull-back distance. It shows the board-level
layer iterator `PCB.IPCB_Board.InternalPlaneLayerIterator` and how to turn each layer object into a typed plane.

## What it shows
- `PCB.IPCB_Board.InternalPlaneLayerIterator`: an iterator already scoped to the board's internal plane layers.
- `PCB.IPCB_LayerObjectIterator.SetBeforeFirst` / `Next` / `LayerObject`: the forward walk, and the layer object at
  the current position.
- `PCB.IPCB_InternalPlane_V7`: the typed plane interface. It derives from `PCB.IPCB_InternalPlane`, which declares
  `PCB.IPCB_InternalPlane.PullBackDistance`.
- `PCB.IPCB_LayerObject.LayerName`: the layer's name as shown in the layer stack.
- `EDP.Utils.CoordToMMs`: converts a coordinate in internal units (10,000 per mil) to millimetres.

## Run it
1. Open a `.PcbDoc` and make it the active document. To see plane lines, the board needs at least one internal plane
   (add one in **Design › Layer Stack Manager**). On a board without planes the command says so.
2. **InternalPlaneLayer › Iterator**, or `tools/ad.ps1 -Sample InternalPlaneLayer run Iterator`
3. The result is a line with the board name and the plane count, then one line per plane, for example
   `<layer name> : Pullback 0.5080 mm (200000)` for a 20 mil pull-back.

| Command | Parameters | Result |
|---|---|---|
| `Iterator` | none | `<file>: <n> internal plane layer(s)`, then `<layer> : Pullback <mm> mm (<internal units>)` per plane; or `<file>: the board has no internal plane layers.` |

## How it works
`InternalPlaneLayerCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`. A `UserException` covers
the case where no PCB editor or no PCB document is active. It calls `InternalPlaneLayerIterator` on the board, then
`SetBeforeFirst` and `Next` in a loop. Each `LayerObject` is cast to `IPCB_InternalPlane_V7`. It reads
`GetState_LayerName` and `GetState_PullBackDistance`, and converts the distance with `EDP.Utils.CoordToMMs`. The lines
are returned as text. The command changes nothing.

## Notes
- The pull-back distance is in internal units (10,000 per mil). The result shows both the millimetre value and the
  raw value.
- The cast is direct, not an `as` test with a null check. SDK interfaces are dispatch interfaces,
  so `as` succeeds for any layer object and filters nothing. The iterator only visits planes anyway.
- On a board whose layer stack has no planes, the iterator visits no layer object, although
  the `.PcbDoc` stores pull-back entries for every plane slot: it does not walk unused plane slots.
- `LayerStackInfo` (`InternalPlaneInfo`) reads the same value through the master layer stack
  (`IPCB_LayerStackBase.Iterator_1`). This sample shows the board-level iterator instead.
