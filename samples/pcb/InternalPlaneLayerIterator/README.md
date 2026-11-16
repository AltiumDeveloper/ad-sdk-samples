# List the tracks on each internal plane layer

Walks the internal plane layers of the current board and lists the tracks on each one. It shows how to visit only the
plane layers with `PCB.IPCB_Board.InternalPlaneLayerIterator`, and how to run a board iterator restricted to one of
those layers.

## What it shows
- `PCB.IPCB_Board.InternalPlaneLayerIterator`: a `PCB.IPCB_LayerObjectIterator` over the internal planes of the
  layer stack, so no general layer iterator has to be filtered.
- `PCB.IPCB_LayerIterator.SetBeforeFirst` / `PCB.IPCB_LayerIterator.Next` / `PCB.IPCB_LayerIterator.Layer`: the
  layer walk. `Layer` returns a `PCB.V7_LayerBase`, which `PCB.IPCB_Board.LayerName` turns into the layer's name.
- `PCB.IPCB_LayerObjectIterator.LayerObject` and `PCB.IPCB_LayerObject.V6_LayerID`: the `PCB.TV6_Layer` value of the
  plane, which the board iterator's layer filter needs.
- `PCB.IPCB_BoardIterator` with `AddFilter_LayerSet(new TV6_LayerSet { layer })`, `AddFilter_ObjectSet` with
  `PCB.TObjectId.eTrackObject` and `AddFilter_Method(TIterationMethod.eProcessAll)`: the tracks of one layer.
- `PCB.IPCB_Track.X1`, `Y1`, `X2`, `Y2`, `Width`: internal units, converted with `EDP.Utils.CoordToMMs` /
  `EDP.Utils.CoordToMils`.
- Helper command: `PCB.IPCB_LayerStack_V7.FirstAvailableInternalPlane` and `PCB.IPCB_LayerStack_V7.InsertLayer` add a
  plane, `PCB.IPCB_ServerInterface.PCBObjectFactory` and `PCB.IPCB_Board.AddPCBObject` place a track on it.

## Run it
1. Open a `.PcbDoc` with internal planes and make it the active document. The board of the SpiritLevel-SL1 example
   project that ships with Altium Designer has no internal planes: on a copy, run
   **InternalPlaneLayerIterator › Add Test Plane Track** with `AddPlane=true`, which adds a plane to the layer stack
   and places one track on it.
2. **InternalPlaneLayerIterator › Get Tracks**, or `tools/ad.ps1 -Sample InternalPlaneLayerIterator run GetTracks`
3. The first line is `<board>: <n> internal plane layer(s), <m> track(s) on them (coordinates in mm, relative to the
   board origin).`, followed by `<layer name> (eV6_InternalPlane<k>): <count> track(s)` per plane and one indented
   `(x1, y1) - (x2, y2), width w` line per track. A board without planes returns
   `<board>: the board has no internal plane layers.`

| Command | Parameters | Result |
|---|---|---|
| `GetTracks` | `Units=mm\|mil` (default `mm`), `Max=<n>` tracks listed per layer (default 20; counts are always complete), `Out=<file or folder>` (optional: every track to a file), `Check=true` (optional: also count each plane's tracks without the layer filter) | The per-plane report; with `Out` also `Report written to <path>` |
| `AddTestPlaneTrack` | `AddPlane=true` (optional: add the first free internal plane when the board has none) | `[Added eV6_InternalPlane<k> to the layer stack.] Placed a track (100, 100) - (600, 100) mil, width 10 mil, on eV6_InternalPlane<k> ...` |

## How it works
`InternalPlaneLayerIteratorCommands.cs`: `GetTracks` gets the current board and calls `InternalPlaneLayerIterator`,
then `SetBeforeFirst` and `Next` in a loop. For each plane it reads the name with `LayerName(Layer())` and the
`TV6_Layer` value with `LayerObject().V6_LayerID()`. It creates a board iterator filtered to that layer, to tracks and
to `eProcessAll`, reads each track's end points and width, and destroys the iterator in a `finally`. Coordinates are
printed relative to `IPCB_Board.XOrigin` / `YOrigin`. With `Check=true`, a second iterator over all tracks counts those
whose `Layer` is the plane, which shows whether the layer filter selected them all.

`AddTestPlaneTrack` looks for the first plane with the same iterator. When there is none and `AddPlane=true`, it takes
`FirstAvailableInternalPlane` of `IPCB_Board.LayerStack_V7` and adds it with `InsertLayer`, between `PreProcess` and
`PostProcess`. It then creates a track with `PCBObjectFactory`, sets its end points, width and layer, and adds it with
`AddPCBObject`. Without `AddPlane=true` on a board without planes, it reports an error.

## Notes
- Planes are normally solid copper defined by the plane itself (with split lines), so a real board often has no tracks
  on its plane layers: 0 tracks per plane is a normal result.
- The layer iterator has no destroy method; only the board iterators are destroyed.
- `LayerObject().V6_LayerID()` returns `eV6_InternalPlane1` for the first internal plane, and
  a board iterator filtered with `new TV6_LayerSet { that value }` finds the plane's tracks: `Check=true` reports the
  same count with and without the layer filter.
- Although `PCB.IPCB_LayerStack_V7` is deprecated, `FirstAvailableInternalPlane` + `InsertLayer` does add the plane
  to the stack, and the following plane walk sees it.
- `AddTestPlaneTrack` is a helper. It changes the board in memory only and marks it
  modified: close the document without saving to discard it.
- When no PCB document is active, the commands raise an error.
