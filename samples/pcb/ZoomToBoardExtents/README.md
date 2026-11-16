# Zoom the PCB view to the board extents

Reads the bounding rectangle of the current board and zooms the PCB editor view onto it, a "zoom to board
extents". It shows how to get a board's bounding rectangle and how to drive the board's graphical view.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the document in the current view, or none.
- `PCB.IPCB_Primitive.BoundingRectangle`: the board inherits it from `PCB.IPCB_Primitive`; in .NET it is an
  extension method that returns a `PCB.CoordRect`.
- `PCB.CoordRect`: `Lx`, `Ly`, `Hx`, `Hy` corners, in internal units (10,000 per mil).
- `PCB.IPCB_Board.GraphicalView_ZoomOnRect`: zooms the board's view onto a rectangle given by two corners.
- `PCB.IPCB_Board.GraphicalView_ViewportRect`: the area the view shows, read before and after the zoom.
- `EDP.Utils.CoordToMils`: converts the coordinates for the returned text.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium Designer)
   and make it the focused document.
2. **ZoomToBoardExtents › Zoom To Board Extents**, or `tools/ad.ps1 -Sample ZoomToBoardExtents run ZoomToBoardExtents`
3. The view zooms so the board's bounding rectangle (all objects on it) fills it. The command returns the board
   extents and the viewport before and after the zoom, in mils.

| Command | Parameters | Result |
|---|---|---|
| `ZoomToBoardExtents` | none | `<file>: zoomed to board extents (<Lx>, <Ly>) - (<Hx>, <Hy>) mil.` plus `Viewport before:` / `Viewport after:` lines |

## How it works
`ZoomToBoardExtentsCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`. A `UserException` covers
the cases where no PCB editor or no PCB document is active. It reads `BoundingRectangle()` and refuses an empty
rectangle. Then it reads `GraphicalView_ViewportRect()`, calls `GraphicalView_ZoomOnRect(Lx, Ly, Hx, Hy)`, reads the
viewport again and returns the three rectangles converted to mils.

## Notes
- The zoom changes only the view, not the document: nothing to undo or save.
- `GetCurrentPCBBoard` returns the board of the current view, so run the command with the PcbDoc focused. With
  another document focused, the command fails with "No PCB document is active".
- `GraphicalView_ZoomOnRect` takes internal units, the same values `BoundingRectangle` returns.
  `GraphicalView_ViewportRect` returns the visible area in internal units and reflects the zoom immediately. The
  view keeps its aspect ratio: on the SpiritLevel board the width matched exactly and the extra height was split
  evenly, with a 6 mil shift in X.
- The board's `BoundingRectangle` covers all objects on the board, including drawing-sheet
  annotations such as fabrication notes and tables, not just the board outline. On the SpiritLevel board the zoom
  therefore shows the whole drawing, not just the 180 x 50 mm board. For the outline, use
  `GetState_BoardOutline().BoundingRectangle()`.
- Open point: what `BoundingRectangle` returns for a board with no objects.
