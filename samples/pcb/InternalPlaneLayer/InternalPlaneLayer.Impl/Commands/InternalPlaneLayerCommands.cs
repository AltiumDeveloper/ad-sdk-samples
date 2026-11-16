using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB; // InternalPlaneLayerIterator, LayerObject and GetCurrentPCBBoard are typed extension methods

namespace AdSamples.InternalPlaneLayer
{
    /// <summary>
    /// Visits the internal plane layers of the current board with the board-level IPCB_Board.InternalPlaneLayerIterator
    /// and reads each plane's pull-back distance.
    /// </summary>
    internal static class InternalPlaneLayerCommands
    {
        [HotCommand("Iterator", "List the internal planes and their pull-back distance")]
        public static string Iterator(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            var lines = new List<string>();

            // The board hands out an iterator that is already scoped to internal planes, so every layer object it
            // returns is a plane and a direct cast is enough. A C# "as" test would not filter anything: SDK
            // interfaces are dispatch interfaces and the cast succeeds for any layer object.
            IPCB_LayerObjectIterator iterator = board.InternalPlaneLayerIterator();
            iterator.SetBeforeFirst();
            while (iterator.Next())
            {
                var plane = (IPCB_InternalPlane_V7)iterator.LayerObject();

                // PullBackDistance is a coordinate in internal units (10,000 per mil); CoordToMMs converts it.
                int pullBack = plane.GetState_PullBackDistance();
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} : Pullback {1:0.0000} mm ({2})",
                    plane.GetState_LayerName(), EDP.Utils.CoordToMMs(pullBack), pullBack));
            }

            string boardName = Path.GetFileName(board.GetState_FileName());
            if (lines.Count == 0)
                return $"{boardName}: the board has no internal plane layers.";

            lines.Insert(0, $"{boardName}: {lines.Count} internal plane layer(s)");
            return string.Join("\n", lines);
        }

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
