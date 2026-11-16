using System.Globalization;
using System.IO;
using DXP; // the IClient.OpenDocument/GetDocumentByPath extension methods live in namespace DXP
using PCB;

namespace AdSamples.BoardOutlineSize
{
    /// <summary>
    /// Reports the width and height of a board outline's bounding rectangle. The board is either opened by path
    /// (Client.OpenDocument, then PCBServer.GetPCBBoardByPath) or the current board.
    /// </summary>
    internal static class BoardOutlineSizeCommands
    {
        /// <summary>
        /// GetPcbSize "PCBFile=&lt;path&gt;|Out=&lt;file or folder&gt;": without PCBFile the current board is used. Out
        /// also writes a one-line "Width=n|Height=n" (internal units) to a file.
        /// </summary>
        [HotCommand("GetPcbSize", "Report the board outline width and height")]
        public static string GetPcbSize(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");

            string pcbFile = ctx.Get("PCBFile");
            string opened = "";
            IPCB_Board board;
            if (string.IsNullOrWhiteSpace(pcbFile))
            {
                board = pcbServer.GetCurrentPCBBoard()
                    ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc, or pass PCBFile=<path>.");
            }
            else
            {
                string path = Path.GetFullPath(pcbFile.Trim().Trim('"'));
                if (!File.Exists(path))
                    throw new UserException($"PCB file not found: {path}");

                // GetPCBBoardByPath only finds boards of documents that are already open, so the document is
                // opened (loaded into the workspace, not shown) first. It stays open.
                DXP.IClient client = DXP.GlobalVars.Client;
                bool wasOpen = client.GetDocumentByPath(path) != null;
                if (!wasOpen)
                    client.OpenDocument("PCB", path);
                board = pcbServer.GetPCBBoardByPath(path)
                    ?? throw new UserException($"Could not load a PCB board from {path}. Is it a .PcbDoc?");
                opened = wasOpen ? " (already open)" : " (opened, left open)";
            }

            // BoardOutline is a polygon (IPCB_BoardOutline : IPCB_Polygon); BoundingRectangle comes from
            // IPCB_Primitive and returns a PCB.CoordRect in internal units.
            IPCB_BoardOutline outline = board.GetState_BoardOutline()
                ?? throw new UserException("The board has no board outline.");
            CoordRect rect = outline.BoundingRectangle();
            int width = rect.Hx - rect.Lx;
            int height = rect.Hy - rect.Ly;

            string boardName = Path.GetFileName(board.GetState_FileName());
            string text = string.Format(CultureInfo.InvariantCulture,
                "{0}{1}: Width = {2:0.###} mil ({3:0.####} mm, {4}), Height = {5:0.###} mil ({6:0.####} mm, {7}). " +
                "Outline bounds Lx={8} Ly={9} Hx={10} Hy={11} (internal units, 10000 per mil).",
                boardName, opened,
                EDP.Utils.CoordToMils(width), EDP.Utils.CoordToMMs(width), width,
                EDP.Utils.CoordToMils(height), EDP.Utils.CoordToMMs(height), height,
                rect.Lx, rect.Ly, rect.Hx, rect.Hy);

            string written = WriteSize(ctx, boardName, width, height);
            return written == null ? text : text + " Written to " + written;
        }

        /// <summary>
        /// Writes "Width=n|Height=n" (internal units) when Out is given. Out is a file
        /// path or an existing folder.
        /// </summary>
        private static string WriteSize(CommandContext ctx, string boardName, int width, int height)
        {
            string path = ctx.Get("Out");
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim().Trim('"');
            if (Directory.Exists(path))
                path = Path.Combine(path, "BoardOutlineSize_" + Path.GetFileNameWithoutExtension(boardName) + ".txt");
            path = Path.GetFullPath(path);
            File.WriteAllText(path, string.Format(CultureInfo.InvariantCulture, "Width={0}|Height={1}", width, height)
                                    + System.Environment.NewLine);
            return path;
        }
    }
}
