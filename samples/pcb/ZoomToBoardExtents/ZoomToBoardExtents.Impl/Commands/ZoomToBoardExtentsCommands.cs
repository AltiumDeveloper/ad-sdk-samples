using System.Globalization;
using PCB;

namespace AdSamples.ZoomToBoardExtents
{
    /// <summary>
    /// Zooms the PCB editor view onto the bounding rectangle of the current board, a "zoom to board extents".
    /// </summary>
    internal static class ZoomToBoardExtentsCommands
    {
        [HotCommand("ZoomToBoardExtents", "Zoom the PCB view to the board's bounding rectangle")]
        public static string ZoomToBoardExtents(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            // GetCurrentPCBBoard returns the board of the document in the current view, so a non-null board
            // also means there is a view to zoom.
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            // IPCB_Board inherits BoundingRectangle from IPCB_Primitive (an extension method in .NET).
            CoordRect extents = board.BoundingRectangle();
            if (extents.Hx <= extents.Lx || extents.Hy <= extents.Ly)
                throw new UserException($"The board's bounding rectangle is empty ({Describe(extents)}); nothing to zoom to.");

            // The viewport is read before and after the zoom only so the result shows that the view changed.
            CoordRect before = board.GraphicalView_ViewportRect();
            board.GraphicalView_ZoomOnRect(extents.Lx, extents.Ly, extents.Hx, extents.Hy);
            CoordRect after = board.GraphicalView_ViewportRect();

            return $"{System.IO.Path.GetFileName(board.GetState_FileName())}: zoomed to board extents {Describe(extents)}.\n"
                 + $"Viewport before: {Describe(before)}\n"
                 + $"Viewport after:  {Describe(after)}";
        }

        /// <summary>Formats a rectangle in mils (coordinates are internal units, 10,000 per mil).</summary>
        private static string Describe(CoordRect r)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}) - ({2:0.##}, {3:0.##}) mil",
                EDP.Utils.CoordToMils(r.Lx), EDP.Utils.CoordToMils(r.Ly),
                EDP.Utils.CoordToMils(r.Hx), EDP.Utils.CoordToMils(r.Hy));
        }
    }
}
