using System;
using System.Globalization;
using PCB;

namespace AdSamples.BoardPadsInArea
{
    /// <summary>
    /// Counts the pads in a rectangle of the current board with a spatial iterator (IPCB_SpatialIterator): an object
    /// filter (pads), a layer filter (all layers) and an area filter (AddFilter_Area). The board's total pad count, from
    /// a board iterator, is reported next to it so the effect of the area filter is visible.
    /// </summary>
    internal static class BoardPadsInAreaCommands
    {
        // Default rectangle: (-1000 mm, -1000 mm) to (2000 mm, 2000 mm), large enough for any board.
        private const double DefaultX1 = -1000, DefaultY1 = -1000, DefaultX2 = 2000, DefaultY2 = 2000;

        /// <summary>
        /// CountPadsInArea "X1=&lt;mm&gt;|Y1=&lt;mm&gt;|X2=&lt;mm&gt;|Y2=&lt;mm&gt;|FromOrigin": counts the pads the spatial
        /// iterator returns for the rectangle with corners (X1, Y1) and (X2, Y2). The values are absolute board
        /// coordinates in mm; with FromOrigin they are offsets from the board origin (IPCB_Board.XOrigin/YOrigin).
        /// </summary>
        [HotCommand("CountPadsInArea", "Count the pads in a rectangle with a spatial iterator")]
        public static string CountPadsInArea(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            bool fromOrigin = ctx.Flag("FromOrigin");

            // Corners in internal units (10,000 per mil). PCB coordinates are absolute, so an offset from the board
            // origin needs the origin added.
            int originX = fromOrigin ? board.GetState_XOrigin() : 0;
            int originY = fromOrigin ? board.GetState_YOrigin() : 0;
            int x1 = ToCoord(ctx, "X1", DefaultX1, originX);
            int y1 = ToCoord(ctx, "Y1", DefaultY1, originY);
            int x2 = ToCoord(ctx, "X2", DefaultX2, originX);
            int y2 = ToCoord(ctx, "Y2", DefaultY2, originY);

            int inArea = 0, centreInside = 0, fullyInside = 0;
            int left = Math.Min(x1, x2), right = Math.Max(x1, x2), bottom = Math.Min(y1, y2), top = Math.Max(y1, y2);

            IPCB_SpatialIterator iterator = board.SpatialIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                // Pads are on the multi-layer (through-hole) or on one copper layer (SMD): search all layers.
                iterator.AddFilter_AllLayers();
                // The corners are passed as given; the reference says they can be in any order.
                iterator.AddFilter_Area(x1, y1, x2, y2);

                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                {
                    inArea++;

                    // Whether the area filter returns pads that only touch the rectangle is not documented
                    // (it does). These two counts show it: compare them with the iterator's count.
                    int px = pad.GetState_XLocation(), py = pad.GetState_YLocation();
                    if (px >= left && px <= right && py >= bottom && py <= top)
                        centreInside++;
                    CoordRect bounds = pad.BoundingRectangle();
                    if (bounds != null && bounds.Left >= left && bounds.Right <= right && bounds.Bottom >= bottom && bounds.Top <= top)
                        fullyInside++;

                    if (inArea % 1000 == 0)
                        ctx.Log($"{inArea} pads so far...");
                }
            }
            finally
            {
                // Destroy takes the iterator by ref and sets the variable to null.
                board.SpatialIterator_Destroy(ref iterator);
            }

            int total = CountAllPads(board);
            string fileName = System.IO.Path.GetFileName(board.GetState_FileName());
            string area = $"({Mm(x1)}, {Mm(y1)}) to ({Mm(x2)}, {Mm(y2)}) mm absolute";
            if (fromOrigin)
                area += $", ({Mm(x1 - originX)}, {Mm(y1 - originY)}) to ({Mm(x2 - originX)}, {Mm(y2 - originY)}) mm from the board origin";

            return $"{fileName}: Pad Count : {inArea} in the area {area}." + Environment.NewLine +
                   $"Of these, {centreInside} have their centre and {fullyInside} their whole bounding rectangle inside the area. " +
                   $"The board has {total} pad(s) in total.";
        }

        /// <summary>All pads of the board with a board iterator (no area): free pads and component pads.</summary>
        private static int CountAllPads(IPCB_Board board)
        {
            int count = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive pad = iterator.FirstPCBObject(); pad != null; pad = iterator.NextPCBObject())
                    count++;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return count;
        }

        /// <summary>
        /// Reads a corner coordinate in mm and converts it to internal units. EDP.Utils.MMsToCoord returns an int
        /// (about 5,454 mm at most), so values outside the int range are rejected instead of overflowing.
        /// </summary>
        private static int ToCoord(CommandContext ctx, string name, double defaultMm, int origin)
        {
            string text = ctx.Get(name);
            double mm = defaultMm;
            if (!string.IsNullOrWhiteSpace(text) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out mm))
                throw new UserException($"{name}='{text}' is not a number. Give the coordinate in mm, for example {name}=25.4.");

            string outOfRange = $"{name}={mm.ToString(CultureInfo.InvariantCulture)} mm is outside the PCB coordinate range.";
            double raw = mm * 10000.0 * 1000.0 / 25.4; // 10,000 internal units per mil, 1000 mil per 25.4 mm
            if (double.IsNaN(raw) || raw < int.MinValue || raw > int.MaxValue)
                throw new UserException(outOfRange);

            long coord = (long)EDP.Utils.MMsToCoord(mm) + origin;
            if (coord < int.MinValue || coord > int.MaxValue)
                throw new UserException(outOfRange);
            return (int)coord;
        }

        private static string Mm(int coord) => EDP.Utils.CoordToMMs(coord).ToString("0.###", CultureInfo.InvariantCulture);

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
