# Count pads in an area with a spatial iterator

Counts the pads in a rectangle of the current board and reports the board's total pad count next to it. It shows
how to set up a `PCB.IPCB_SpatialIterator` with an object filter, a layer filter and an area filter, and how to turn
millimetres into PCB coordinates.

## What it shows
- `PCB.IPCB_Board.SpatialIterator_Create` / `PCB.IPCB_Board.SpatialIterator_Destroy`: create a spatial iterator
  and free it again (the destroy method takes the iterator by `ref`).
- `PCB.IPCB_AbstractIterator.AddFilter_ObjectSet` with `PCB.TObjectId.ePadObject`: fetch pads only.
- `PCB.IPCB_AbstractIterator.AddFilter_AllLayers`: search every layer, because pads are on the multi-layer
  (through-hole) or on a single copper layer (SMD).
- `PCB.IPCB_AbstractIterator.AddFilter_Area`: restrict the search to a rectangle given by two corners in internal
  units. A board iterator ignores this filter; only a spatial iterator applies it.
- `PCB.IPCB_AbstractIterator.FirstPCBObject` / `PCB.IPCB_AbstractIterator.NextPCBObject`: walk the results until
  `null`.
- `EDP.Utils.MMsToCoord` / `EDP.Utils.CoordToMMs`: convert between millimetres and internal units.
- `PCB.IPCB_Board.XOrigin` / `PCB.IPCB_Board.YOrigin`: PCB coordinates are absolute; add the board origin to
  give a rectangle relative to it.
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TIterationMethod.eProcessAll`: the board-wide count, for
  comparison.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document.
2. **BoardPadsInArea › Count Pads In Area**, or `tools/ad.ps1 -Sample BoardPadsInArea run CountPadsInArea`
3. With no parameters the rectangle is (-1000, -1000) to (2000, 2000) mm, so the count equals the
   board's total. Pass a small rectangle to see the filter work:
   `tools/ad.ps1 -Sample BoardPadsInArea run CountPadsInArea "X1=0|Y1=0|X2=30|Y2=30|FromOrigin"`.

| Command | Parameters | Result |
|---|---|---|
| `CountPadsInArea` | `X1`, `Y1`, `X2`, `Y2` in mm (defaults -1000, -1000, 2000, 2000); `FromOrigin` (flag): the values are offsets from the board origin | `<file>: Pad Count : N in the area (...) mm absolute.`, then how many of the N pads have their centre and their whole bounding rectangle inside the area, and the board's total pad count |

## How it works
`BoardPadsInAreaCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`, and throws a `UserException`
when no PCB editor or document is active. `ToCoord` parses each corner (invariant culture, mm), converts it with
`EDP.Utils.MMsToCoord` and, with `FromOrigin`, adds `GetState_XOrigin` / `GetState_YOrigin`. Values outside the
`int` coordinate range are rejected. The command then creates a spatial iterator, sets the pad, all-layers and
area filters, and counts the pads it returns. For each pad it also checks whether the pad's location
(`GetState_XLocation` / `GetState_YLocation`) and its `BoundingRectangle` lie inside the rectangle. The iterator is
destroyed in a `finally` block. A board iterator over all pads (`eProcessAll`) gives the total.

## Notes
- Coordinates are absolute board coordinates, not relative to the board origin. Use `FromOrigin` to enter values as
  they appear relative to the origin.
- `EDP.Utils.MMsToCoord` returns an `int`. About 5,454 mm is the largest value that fits, so larger values are
  rejected with an error.
- The reference says the two corners can be given in any order. The sample passes them unchanged; reversed
  corners give the same result.
- `AddFilter_Area` returns objects that **overlap** the rectangle, not only those inside it. The centre and
  bounding-rectangle counts in the result make this visible: a 5 x 5 mm rectangle on the SpiritLevel board
  returns one pad whose centre and whose whole bounding rectangle lie outside it. Code that needs pads
  fully inside an area must test them itself.
- A spatial iterator skips group objects such as components, but it does fetch the primitives those groups own:
  over the whole SpiritLevel board it returns all 642 component pads, the same count as a board iterator with
  `eProcessAll`.
- The command is read-only and does not change the document.
