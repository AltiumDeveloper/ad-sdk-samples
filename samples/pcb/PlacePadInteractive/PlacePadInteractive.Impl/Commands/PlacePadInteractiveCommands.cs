using PCB;

namespace AdSamples.PlacePadInteractive
{
    /// <summary>
    /// Interactive placement of a pad the extension creates itself: the pad follows the cursor until the user clicks.
    /// The placement runs in <see cref="PadPlacement"/>; these commands start, inspect and cancel it.
    /// </summary>
    internal static class PlacePadInteractiveCommands
    {
        [HotCommand("PlacePad", "Place a bottom test-point pad that follows the cursor")]
        public static string PlacePad(CommandContext ctx)
        {
            if (PadPlacement.Active != null)
                throw new UserException("A pad placement is already in progress. Finish it (left-click, right-click or Escape) or run PlacePadCancel.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            PadPlacement placement = PadPlacement.Start(pcbServer, board);

            // The command returns now, before the user clicks: its text cannot say where the pad ended up.
            // Altium Designer keeps running; the hooks finish the placement later. PlacePadStatus reports the outcome.
            return $"Placing a bottom-layer test-point pad, now at {PadPlacement.FormatLocation(board, placement.Pad)}. " +
                   "Move the mouse over the board, left-click to place it, right-click or Escape to cancel. " +
                   "PlacePadStatus shows the outcome.";
        }

        [HotCommand("PlacePadStatus", "Show the state of the pad placement")]
        public static string PlacePadStatus(CommandContext ctx)
        {
            IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard();
            string pads = board == null ? "no PCB document is active" : $"{CountPads(board)} pads on the board";

            PadPlacement active = PadPlacement.Active;
            if (active == null)
                return $"No placement in progress. Last placement: {PadPlacement.LastOutcome}. {pads}.";

            IPCB_Pad pad = active.Pad;
            return $"Placement in progress: pad on {pad.GetState_Layer()}, IsTestPoint_Bottom = {pad.GetState_IsTestPoint_Bottom()}, " +
                   $"selected = {pad.GetState_Selected()}, at {(board == null ? "?" : PadPlacement.FormatLocation(board, pad))}. {pads}.";
        }

        [HotCommand("PlacePadCancel", "Cancel a pad placement in progress")]
        public static string PlacePadCancel(CommandContext ctx)
        {
            PadPlacement active = PadPlacement.Active;
            if (active == null)
                return $"No placement in progress. Last placement: {PadPlacement.LastOutcome}.";
            return "Placement " + active.Finish(keep: false, "PlacePadCancel") + ".";
        }

        /// <summary>Counts the pads of the board (free pads and component pads), for a read-back of add/remove.</summary>
        private static int CountPads(IPCB_Board board)
        {
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.ePadObject });
                iterator.AddFilter_AllLayers();
                int count = 0;
                for (IPCB_Primitive obj = iterator.FirstPCBObject(); obj != null; obj = iterator.NextPCBObject())
                    count++;
                return count;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }
    }
}
