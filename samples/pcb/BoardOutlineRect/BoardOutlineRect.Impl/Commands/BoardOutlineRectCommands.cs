using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.BoardOutlineRect
{
    /// <summary>
    /// Reads the board outline (IPCB_Board.BoardOutline, an IPCB_Polygon) segment by segment, and replaces it by a
    /// rectangle of four line segments.
    /// </summary>
    internal static class BoardOutlineRectCommands
    {
        /// <summary>ReadBoardOutline: lists the outline's segments and its bounding-rectangle size. Read-only.</summary>
        [HotCommand("ReadBoardOutline", "Read the board outline segments and size")]
        public static string ReadBoardOutline(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_BoardOutline outline = Outline(board);

            var lines = new List<string>
            {
                $"{Path.GetFileName(board.GetState_FileName())}: board outline, {outline.GetState_PointCount()} points, " +
                $"layer {outline.GetState_Layer()}, origin at ({Mils(board.GetState_XOrigin())}, {Mils(board.GetState_YOrigin())}) mil absolute"
            };
            lines.AddRange(DescribeSegments(board, outline));
            lines.Add(DescribeSize(board, outline));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// UpdateBoardRegion "Width=&lt;mil&gt;|Height=&lt;mil&gt;|CenterX=&lt;mil&gt;|CenterY=&lt;mil&gt;|Rebuild=false":
        /// replaces the outline by a rectangle centred on (CenterX, CenterY), relative to the board origin.
        /// </summary>
        [HotCommand("UpdateBoardRegion", "Replace the board outline by a rectangle")]
        public static string UpdateBoardRegion(CommandContext ctx)
        {
            double widthMils = Number(ctx, "Width", 1000);
            double heightMils = Number(ctx, "Height", 1000);
            double centerXMils = Number(ctx, "CenterX", 0);
            double centerYMils = Number(ctx, "CenterY", 0);
            // RebuildBoardOutline, called after UpdateBoardOutline, throws the written rectangle away and
            // recomputes the outline from the objects on the given layer, so it is off by default here;
            // Rebuild=true adds the call and shows its effect.
            bool rebuild = string.Equals(ctx.Get("Rebuild", "false"), "true", StringComparison.OrdinalIgnoreCase);
            if (widthMils <= 0 || heightMils <= 0)
                throw new UserException("Width and Height must be greater than 0 (mils).");

            IPCB_Board board = CurrentBoard();
            IPCB_BoardOutline outline = Outline(board);

            // PCB coordinates are absolute, in internal units (10,000 per mil): add the board origin to place the
            // rectangle relative to it (docs: PCB basics, "Coordinates").
            int cx = board.GetState_XOrigin() + EDP.Utils.MilsToCoord(centerXMils);
            int cy = board.GetState_YOrigin() + EDP.Utils.MilsToCoord(centerYMils);
            int halfW = EDP.Utils.MilsToCoord(widthMils / 2);
            int halfH = EDP.Utils.MilsToCoord(heightMils / 2);
            // A precaution, not a documented limit: keep every vertex at a non-negative absolute coordinate.
            if (cx - halfW < 0 || cy - halfH < 0)
                throw new UserException("The rectangle would extend below absolute coordinate 0. Move it with CenterX/CenterY or make it smaller.");

            // Vertex order: bottom-left, top-left, top-right, bottom-right.
            var corners = new (int X, int Y)[]
            {
                (cx - halfW, cy - halfH),
                (cx - halfW, cy + halfH),
                (cx + halfW, cy + halfH),
                (cx + halfW, cy - halfH),
            };

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            pcbServer.PreProcess();
            try
            {
                outline.BeginModify();
                // Set the point count first, then each segment by index.
                outline.SetState_PointCount(corners.Length);
                for (int i = 0; i < corners.Length; i++)
                    outline.SetState_Segments(i, LineSegment(corners[i].X, corners[i].Y));
                // The reference says indexes 0 to PointCount inclusive are valid. Index PointCount is written as a
                // copy of the first vertex (closing the contour).
                outline.SetState_Segments(corners.Length, LineSegment(corners[0].X, corners[0].Y));

                // PolySegment is a class (a reference type, not a record copied by value), so a fresh object
                // is created for every index above instead of reusing one.

                // In .NET, the polygon version of SetState_XSizeYSize is IPCB_Polygon.SetState_XSizeYSize_1
                // (the reference: recalculates the bounding rectangle and AreaSize from the outline), while
                // SetState_XSizeYSize is IPCB_Group's (recalculates child primitives).
                // SetState_XSizeYSize_1 with UpdateBoardOutline is enough for BoundingRectangle to follow the outline.
                outline.SetState_XSizeYSize_1();
                outline.SetState_EnableDraw(true);
                outline.Invalidate();
                outline.EndModify();

                // Recalculates the outline's size and region (reference: IPCB_Board.UpdateBoardOutline).
                board.UpdateBoardOutline();
                if (rebuild)
                    board.RebuildBoardOutline(outline.GetState_V7Layer());
            }
            finally
            {
                pcbServer.PostProcess();
            }
            board.ViewManager_GraphicallyInvalidatePrimitive(board);

            // Re-read the outline from the board: the result reports what AD holds now, not what was written.
            IPCB_BoardOutline after = Outline(board);
            var lines = new List<string>
            {
                $"Board outline written as a {Num(widthMils)} x {Num(heightMils)} mil rectangle centred at " +
                $"({Num(centerXMils)}, {Num(centerYMils)}) mil from the board origin" +
                (rebuild
                    ? ", then RebuildBoardOutline was called: it recomputes the outline from the objects on the layer, so the segments below are not the rectangle."
                    : " (RebuildBoardOutline skipped).") +
                $" Outline now has {after.GetState_PointCount()} points.",
                DescribeSize(board, after),
            };
            lines.AddRange(DescribeSegments(board, after));
            lines.Add("Close the document without saving to discard the change.");
            return string.Join(Environment.NewLine, lines);
        }

        private static PolySegment LineSegment(int x, int y) => new PolySegment
        {
            Kind = TPolySegmentType.ePolySegmentLine,
            Vx = x,
            Vy = y,
            Cx = 0,
            Cy = 0,
            Radius = 0,
            Angle1 = 0,
            Angle2 = 0,
        };

        /// <summary>One line per segment index 0..PointCount (inclusive, see the Segments reference), relative to the origin.</summary>
        private static IEnumerable<string> DescribeSegments(IPCB_Board board, IPCB_BoardOutline outline)
        {
            int x0 = board.GetState_XOrigin(), y0 = board.GetState_YOrigin();
            int count = outline.GetState_PointCount();
            for (int i = 0; i <= count; i++)
            {
                PolySegment s = outline.GetState_Segments(i);
                string label = i == count ? $"  [{i}] (index PointCount)" : $"  [{i}]";
                yield return s.Kind == TPolySegmentType.ePolySegmentLine
                    ? $"{label} Line  V=({Mils(s.Vx - x0)}, {Mils(s.Vy - y0)}) mil"
                    : $"{label} Arc   V=({Mils(s.Vx - x0)}, {Mils(s.Vy - y0)}) mil, C=({Mils(s.Cx - x0)}, {Mils(s.Cy - y0)}) mil, " +
                      $"R={Mils(s.Radius)} mil, Angle1={Num(s.Angle1)}, Angle2={Num(s.Angle2)}";
            }
        }

        /// <summary>Width and height in the board's display unit and in the other unit.</summary>
        private static string DescribeSize(IPCB_Board board, IPCB_BoardOutline outline)
        {
            CoordRect br = outline.BoundingRectangle();
            TUnit unit = board.GetState_DisplayUnit();
            TUnit other = unit == TUnit.eImperial ? TUnit.eMetric : TUnit.eImperial;
            int w = br.Right - br.Left, h = br.Top - br.Bottom;
            return $"Board Outline Width : {EDP.Utils.CoordUnitToString(w, unit)} ({EDP.Utils.CoordUnitToString(w, other)}), " +
                   $"Height : {EDP.Utils.CoordUnitToString(h, unit)} ({EDP.Utils.CoordUnitToString(h, other)}), raw {w} x {h}";
        }

        private static double Number(CommandContext ctx, string name, double defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new UserException($"{name}='{text}' is not a number (mils, '.' as decimal separator).");
            return value;
        }

        private static string Mils(int coord) => Num(EDP.Utils.CoordToMils(coord));

        private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static IPCB_BoardOutline Outline(IPCB_Board board) =>
            board.GetState_BoardOutline() ?? throw new UserException("The board has no outline.");

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
