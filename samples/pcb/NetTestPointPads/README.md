# Add a bottom test-point pad to every net

Adds one pad per net of the current board, flags it as a bottom test point and assigns it to the net. It shows how
to create a primitive with the PCB object factory, set its net and test-point flag, place it from a net's tracks,
and add it to the board.

## What it shows
- `PCB.IPCB_ServerInterface.PCBObjectFactory`: create a new pad (`PCB.TObjectId.ePadObject`, `PCB.TDimensionKind.eNoDimension`,
  `PCB.TObjectCreationMode.eCreate_Default`). Size, shape and hole are the factory defaults.
- `PCB.IPCB_Primitive.IsTestPoint_Bottom`: flag the pad as a bottom test point.
- `PCB.IPCB_Primitive.Net`: assign the pad to a net with `SetState_Net`.
- `PCB.IPCB_Primitive.Layer`: `PCB.TV6_Layer.eV6_BottomLayer` or `PCB.TV6_Layer.eV6_MultiLayer`.
- `PCB.IPCB_Net.GetPrimitiveCount` / `PCB.IPCB_Net.GetPrimitiveAt`: the tracks of a net, by position starting at 1.
- `PCB.IPCB_Track.X1` / `Y1` / `X2` / `Y2`, `PCB.IPCB_Pad.XLocation` / `YLocation`: place the pad at a track's midpoint.
- `PCB.IPCB_Board.BoardOutline` and `PCB.IPCB_Primitive.BoundingRectangle`: the outline's extents (`PCB.CoordRect.Lx`,
  `Hx`, `Hy`) for the fallback grid.
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eNetObject`: iterate the nets.
- `PCB.IPCB_Board.AddPCBObject` between `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess`, then
  `PCB.IPCB_Board.ViewManager_FullUpdate` to redraw.
- `EDP.Utils.MMsToCoord`: millimetres to internal units.

## Run it
1. Open a `.PcbDoc` with nets (a copy of the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer works) and make it the active document.
2. **NetTestPointPads › Create Test Point**, or `tools/ad.ps1 -Sample NetTestPointPads run CreateTestPoint`
3. The result reads `<board>: created <n> test-point pads (<a> on a bottom-layer track, <b> on another track
   (multi-layer), <c> in the grid above the board outline). First pad: ...`, with n = the number of nets.
   **NetTestPointPads › Count Test Point Pads** (`tools/ad.ps1 -Sample NetTestPointPads run CountTestPointPads`)
   reads them back.

| Command | Parameters | Result |
|---|---|---|
| `CreateTestPoint` | `OffsetMm=10`, `PitchMm=5`, `RowMm=10` (all optional: grid start above the outline's top edge, pad spacing, row spacing) | Number of pads created, split by placement, and the first pad's net, layer, location, size and hole |
| `CountTestPointPads` | none | Number of pads flagged as bottom test points by layer, with a net, above the outline, and the board's net count |

## How it works
`NetTestPointPadsCommands.cs`: `CreateTestPoint` gets the current board and the bounding rectangle of its outline.
`CollectNets` walks the nets with a board iterator (`eNetObject`, all layers, `eProcessAll`) into a list before any
pad is added. Between `PreProcess` and `PostProcess`, for each net it creates a pad with `PCBObjectFactory`, sets the
bottom layer, the bottom test-point flag and the net. `FirstTrack` looks through the net's tracks with
`GetPrimitiveAt(1..count, eTrackObject)` for the first one on the bottom layer. The pad goes to that track's
midpoint. If no track is on the bottom layer, it goes to the midpoint of the first track and is moved to the
multi-layer. If the net has no tracks, it goes into a grid above the outline (on the bottom layer). Then
`AddPCBObject` adds the pad. After `PostProcess`, `ViewManager_FullUpdate` redraws the board.

`CountTestPointPads` iterates the pads (`ePadObject`) and counts those with `IsTestPoint_Bottom` set.

## Notes
- The board is changed in memory and not saved, but it is marked modified. Close the document without saving to
  discard the pads. Whether one undo removes all pads is not known (`AddPCBObject` notifies the undo history per
  object).
- Running `CreateTestPoint` twice adds a second set of pads. `CountTestPointPads` counts every flagged pad, including
  test points the board already had.
- The pads keep the factory's default size, shape and hole. The returned text shows the first pad's size and hole.
  The default is 1.524 x 1.524 mm with a 0.762 mm hole, and the hole is kept when the pad is moved to the bottom
  layer, so it stays a through-hole pad. Grid pads lie outside the board outline, and their net may report DRC
  violations.
- The pad is assigned to the net with `SetState_Net` only. The pad then reports its net, but the net's own primitive
  list (`GetPrimitiveCount` with `ePadObject`) does **not** include the new pad, so the created pads cannot be found
  by walking the net.
- Coordinates are `int` internal units (10,000 per mil). Track midpoints are computed in `long` so the sum cannot
  overflow.
