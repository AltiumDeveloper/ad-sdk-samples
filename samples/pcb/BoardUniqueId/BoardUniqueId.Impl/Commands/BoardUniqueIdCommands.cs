using PCB;

namespace AdSamples.BoardUniqueId
{
    /// <summary>
    /// Reads and writes the unique ID of the current PCB board (IPCB_Board inherits it from IPCB_Primitive).
    /// </summary>
    internal static class BoardUniqueIdCommands
    {
        [HotCommand("GetUniqueId", "Show the unique ID of the current board")]
        public static string GetUniqueId(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            return $"{System.IO.Path.GetFileName(board.GetState_FileName())}: UniqueId = '{board.GetState_UniqueId()}'";
        }

        /// <summary>SetUniqueId "UniqueId=&lt;text&gt;": assigns the given string as the board's unique ID.</summary>
        [HotCommand("SetUniqueId", "Set the unique ID of the current board")]
        public static string SetUniqueId(CommandContext ctx)
        {
            string uniqueId = ctx.Get("UniqueId");
            if (string.IsNullOrEmpty(uniqueId))
                throw new UserException("No UniqueId specified. Pass it as a parameter: UniqueId=<text>.");

            IPCB_Board board = CurrentBoard();
            string previous = board.GetState_UniqueId();

            // Board changes are bracketed like any other PCB modification (docs: PCB basics, "Changing objects"),
            // so the board records the change and the document is marked modified.
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            pcbServer.PreProcess();
            try
            {
                board.BeginModify();
                // Note the accessor names: the getter is GetState_UniqueId, the setter SetState_UniqueID.
                board.SetState_UniqueID(uniqueId);
                board.EndModify();
            }
            finally
            {
                pcbServer.PostProcess();
            }

            return $"UniqueId changed from '{previous}' to '{board.GetState_UniqueId()}'. Save the document to keep it.";
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
