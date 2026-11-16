# Redefine the board outline as a rectangle

Reads the outline of the current PCB board segment by segment and replaces it by a rectangle of four line segments,
then reports the new width and height in both unit systems. It shows how to write a polygon's vertices with
`PCB.PolySegment` and how to make the board pick up the new outline.

## What it shows
- `PCB.IPCB_Board.BoardOutline`: the board shape, an `PCB.IPCB_BoardOutline` (which derives from `PCB.IPCB_Polygon`).
- `PCB.IPCB_Polygon.PointCount` and `PCB.IPCB_Polygon.Segments`: read and write the vertices. Set the point count
  first, then one `PCB.PolySegment` per index. `PCB.PolySegment` is a class, so each index gets its own object.
- `PCB.TPolySegmentType.ePolySegmentLine`: a straight segment given by its vertex `Vx`, `Vy`.
- `PCB.IPCB_Polygon.SetState_XSizeYSize_1`, `PCB.IPCB_BoardOutline.Invalidate` and
  `PCB.IPCB_Board.UpdateBoardOutline`: the calls that make the board use the new outline.
- `PCB.IPCB_Board.RebuildBoardOutline` (optional, `Rebuild=true`): it recomputes the outline from the objects on the layer and throws the new rectangle away (see Notes).
- `PCB.IPCB_Primitive.V7Layer`: the outline's layer as a `PCB.V7_LayerBase`, the type `RebuildBoardOutline` takes.
- `PCB.IPCB_Board.XOrigin` / `YOrigin` with `EDP.Utils.MilsToCoord`: place absolute coordinates relative to the
  board origin.
- `PCB.IPCB_Primitive.BoundingRectangle`, `PCB.IPCB_Board.DisplayUnit` and `EDP.Utils.CoordUnitToString`: the size
  in the board's display unit and in the other unit.

## Run it
1. Open a scratch copy of any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships
   with Altium Designer) and make it the active document. The change rewrites the board shape.
2. **BoardOutlineRect › Read Board Outline**, or `tools/ad.ps1 -Sample BoardOutlineRect run ReadBoardOutline`
3. **BoardOutlineRect › Update Board Region** (1000 x 1000 mil), or
   `tools/ad.ps1 -Sample BoardOutlineRect run UpdateBoardRegion "Width=2000|Height=1000|CenterX=1000|CenterY=500"`
4. The result lists the outline read back from the board: 4 points, the vertices in mil from the board origin
   ((0, 0), (0, 1000), (2000, 1000), (2000, 0) for the parameters above), and the width and height formatted by
   `CoordUnitToString` in both units, plus the raw size (20000000 x 10000000).

| Command | Parameters | Result |
|---|---|---|
| `ReadBoardOutline` | none | point count, layer, origin, one line per segment index 0..PointCount, width and height |
| `UpdateBoardRegion` | `Width`, `Height` (mil, default 1000), `CenterX`, `CenterY` (mil from the board origin, default 0), `Rebuild=true` (optional, also calls `RebuildBoardOutline`) | the new size and segments, read back from the board |

## How it works
`BoardOutlineRectCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer` and its outline with
`GetState_BoardOutline`. `ReadBoardOutline` walks `GetState_Segments(i)` for i from 0 to `PointCount` and prints
line or arc data relative to the board origin, then the bounding rectangle.

`UpdateBoardRegion` parses the parameters (invariant culture), converts them with `EDP.Utils.MilsToCoord` and adds
the board origin. Between `PreProcess` / `BeginModify` and `EndModify` / `PostProcess` it calls
`SetState_PointCount(4)`, writes four new line segments (bottom-left, top-left, top-right, bottom-right) and a closing copy of the first at index 4, then calls `SetState_XSizeYSize_1`,
`SetState_EnableDraw(true)` and `Invalidate`. After `EndModify` it calls `UpdateBoardOutline` and, only with
`Rebuild=true`, `RebuildBoardOutline` with the outline's `V7Layer`. Finally it redraws the board with
`ViewManager_GraphicallyInvalidatePrimitive` and reports the outline as read back from the board.

## Notes
- All four written vertices stick, AD keeps a closing vertex at index
  `PointCount` (a copy of vertex 0, reported as a line even when vertex 0 is an arc), and
  `SetState_XSizeYSize_1` plus `UpdateBoardOutline` are enough for `BoundingRectangle` to follow the new outline.
- **`RebuildBoardOutline` discards the rectangle.** With `Rebuild=true` the outline comes back as a different
  rectangle computed from the objects on the layer (6911 x 1910 mil on the SpiritLevel board instead of the
  written 2000 x 1000). That is why the sample does not call it by default.
- The PCB view does not visibly redraw the board shape after the change, although the API reports the new
  outline; how to force the editor to redraw is an open point.
- Work on a copy and close it without saving to discard the change.
- Coordinates are internal units, 10,000 per mil. The sample takes the size and position in mils.
- The command refuses a rectangle that would reach a negative absolute coordinate. That is a precaution, not a
  documented limit.
- The outline is replaced by straight lines only; arcs of the previous outline are lost.
