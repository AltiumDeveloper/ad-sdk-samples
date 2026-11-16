# Measure the distance between every pair of tracks

Collects every track of the current PCB board and measures the distance of every unique pair with
`PCB.IPCB_Board.PrimPrimDistance`, then reports the pair count and the two closest pairs. It shows the
collect-then-measure pattern: iterate, destroy the iterator, then compare.

## What it shows
- `PCB.IPCB_Board.PrimPrimDistance`: the distance between two primitives, in internal units (10,000 per mil).
  `PCB.PCBConstant.MaxCoord` means it could not be measured.
- `PCB.IPCB_Board.BoardIterator_Create` / `PCB.IPCB_Board.BoardIterator_Destroy` with
  `PCB.IPCB_BoardIterator.AddFilter_ObjectSet` (`PCB.TObjectId.eTrackObject`), `PCB.IPCB_BoardIterator.AddFilter_AllLayers`
  and `PCB.IPCB_BoardIterator.AddFilter_Method` (`PCB.TIterationMethod.eProcessAll`): collect all tracks, including those
  inside components. The iterator is destroyed before the measuring starts.
- `PCB.IPCB_Track.X1`, `PCB.IPCB_Track.Y1`, `PCB.IPCB_Track.X2`, `PCB.IPCB_Track.Y2`, `PCB.IPCB_Track.Width`: the
  reported track geometry, relative to `PCB.IPCB_Board.XOrigin` / `PCB.IPCB_Board.YOrigin`.
- `EDP.Utils.CoordUnitToString` with `PCB.TUnit.eMetric`: format internal units as millimeters.
- `PCB.IPCB_LayerStack.LayerObject` / `PCB.IPCB_LayerObject.LayerName`: the layer name for the report.

## Run it
1. Open a `.PcbDoc` with at least two tracks and make it the active document. On a real board (the board of the
   SpiritLevel-SL1 example project that ships with Altium Designer has 8229 tracks) measure a subset with
   `Tracks=<n>`; measuring all of them is minutes of work.
2. **TrackPairDistance › Clearance Check**, or
   `tools/ad.ps1 -Sample TrackPairDistance run ClearanceCheck "Tracks=300"`
3. The first line reads `<board>: <u> of <n> tracks measured, <m> pairs measured, <k> touching or overlapping
   (distance <= 0)[, <p> not measured (MaxCoord)].`, followed by the smallest distance and the smallest distance
   between tracks that do not touch, each with both tracks (layer, net, end points, width).

| Command | Parameters | Result |
|---|---|---|
| `ClearanceCheck` | `Tracks=<n>` (how many of the board's tracks to measure, in iteration order; default 0 = all), `MaxTracks=<n>` (refuse to measure more than this many tracks; default 500, 0 = no limit), `SameLayerOnly=true` (optional: only pairs on the same layer) | The summary and the two closest pairs; above `MaxTracks` an error with the board's track count, the number that would be measured and the pair count |

## How it works
`TrackPairDistanceCommands.cs`: `ClearanceCheck` gets the current board and collects its tracks into a list with a board
iterator (tracks, all layers, `eProcessAll`), destroying it in a `finally`. `Tracks=<n>` then narrows the collected
list to its first `n` entries (iteration order), and the `MaxTracks` guard is applied to that subset: if more tracks
than `MaxTracks` would be measured it stops with an error naming the board's track count, the number that would be
measured and the resulting pair count. Otherwise a nested loop calls `PrimPrimDistance` once for every pair `i < j`
of the subset, skipping pairs on different layers when `SameLayerOnly` is set. It counts pairs with a distance of 0
or less, and keeps the smallest distance and the smallest positive one. Progress goes to `ctx.Log` every 50 tracks.
The report formats distances and coordinates in millimeters with `EDP.Utils.CoordUnitToString`, and adds the raw
internal value.

## Notes
- The work grows with the square of the track count: n tracks are n(n-1)/2 `PrimPrimDistance` calls, all on AD's main
  thread, so AD does not respond until the command returns. `Tracks` bounds the work (measure a subset) and
  `MaxTracks` refuses anything larger. The SDK must not be called from a background thread (docs:
  runtime-and-hosting, The main thread).
- Connected tracks touch, so on a routed board the smallest distance is usually 0. The second line, the closest pair
  that does not touch, is the clearance-like figure.
- `PrimPrimDistance` manages about 30,500 calls per second, so measuring a whole real board is
  minutes of frozen AD - the SpiritLevel board has 8229 tracks, 33.8 million pairs, roughly 18.5 minutes.
  `Tracks=300` on that board is 44,850 pairs, measured in 2.2 s end to end.
- Overlapping or touching tracks return exactly **0**, never a negative value. `PCBConstant.MaxCoord`
  **does** appear for two primitives of the same open board, and often (more than half of a large sample of
  all-layer pairs on the SpiritLevel board), so the "not measured" count is not an edge case.
- Which pairs give `MaxCoord` is not established. Over one 300-track subset, every same-layer pair returned a
  distance and every cross-layer pair returned `MaxCoord`; a larger sample did find cross-layer pairs with a real
  distance. Do not assume "MaxCoord means no shared layer": test every result for `MaxCoord`.
- Read-only: nothing on the board changes, and the document is not marked modified.
