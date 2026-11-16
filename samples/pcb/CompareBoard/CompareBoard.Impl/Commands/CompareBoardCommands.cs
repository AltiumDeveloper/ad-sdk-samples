using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using DXP; // the IClient.OpenDocument/ShowDocument extension methods live in namespace DXP
using PCB;

namespace AdSamples.CompareBoard
{
    /// <summary>
    /// Compares two PCB documents layer by layer with IPCB_PrimitiveComparator.Compare. The SDK lesson: open the files
    /// with IClient.OpenDocument, get their boards with IPCB_ServerInterface.GetPCBBoardByPath, then hand both boards
    /// to the comparator that PCBServer.PrimitiveComparator returns.
    /// </summary>
    internal static class CompareBoardCommands
    {
        /// <summary>
        /// CompareBoard "BoardA=&lt;path&gt;|BoardB=&lt;path&gt;|Tolerance=&lt;square internal units&gt;|Show=false".
        /// From a menu click without parameters the two files are picked with file dialogs.
        /// </summary>
        [HotCommand("CompareBoard", "Compare two PCB documents layer by layer")]
        public static string CompareBoard(CommandContext ctx)
        {
            string pathA = ResolvePath(ctx, "BoardA", "Select the first PCB document (BoardA)");
            string pathB = ResolvePath(ctx, "BoardB", "Select the second PCB document (BoardB)");
            if (string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase))
                throw new UserException("BoardA and BoardB are the same file. Pass two different .PcbDoc files.");

            double tolerance = ParseTolerance(ctx.Get("Tolerance", "0"));
            // Show=false only opens the documents. By default both are shown, so the
            // difference objects that Compare switches on can be seen.
            bool show = !string.Equals(ctx.Get("Show", "true"), "false", StringComparison.OrdinalIgnoreCase);

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            // OpenDocument loads the file (or returns the document when it is already open). GetPCBBoardByPath only
            // finds boards of open documents, so the files are opened first.
            ctx.Log($"Opening {pathA} and {pathB}");
            IServerDocument documentA = client.OpenDocument("PCB", pathA)
                ?? throw new UserException($"BoardA: Altium Designer could not open {pathA} as a PCB document.");
            IServerDocument documentB = client.OpenDocument("PCB", pathB)
                ?? throw new UserException($"BoardB: Altium Designer could not open {pathB} as a PCB document.");
            if (show)
            {
                // B first, so that A ends up as the focused document.
                client.ShowDocument(documentB);
                client.ShowDocument(documentA);
            }

            // Read the PCB server after opening: it is loaded with the first PCB document.
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running.");
            IPCB_Board boardA = pcbServer.GetPCBBoardByPath(pathA)
                ?? throw new UserException($"BoardA: no open PCB document has the file name {pathA}.");
            IPCB_Board boardB = pcbServer.GetPCBBoardByPath(pathB)
                ?? throw new UserException($"BoardB: no open PCB document has the file name {pathB}.");

            IPCB_PrimitiveComparator comparator = pcbServer.PrimitiveComparator()
                ?? throw new UserException("PCBServer.PrimitiveComparator returned no comparator.");

            string nameA = Path.GetFileName(boardA.GetState_FileName());
            string nameB = Path.GetFileName(boardB.GetState_FileName());
            ctx.Log($"Comparing {nameA} with {nameB}, tolerance {tolerance.ToString(CultureInfo.InvariantCulture)}");

            bool identical;
            try
            {
                // Shows progress while it runs; Esc cancels it and raises an exception (reference remarks).
                identical = comparator.Compare(boardA, boardB, tolerance);
            }
            catch (Exception ex)
            {
                throw new UserException($"Compare of {nameA} and {nameB} was cancelled or failed: {ex.Message}");
            }

            // The reference says true means no differences were found, but
            // it returns false for two byte-identical copies of the same file, so the
            // text names the raw value and does not claim that false proves the boards differ.
            string tol = tolerance.ToString(CultureInfo.InvariantCulture);
            return identical
                ? $"Compare({nameA}, {nameB}, {tol}) returned true: no differences found."
                : $"Compare({nameA}, {nameB}, {tol}) returned false: the comparator reports differences. " +
                  "The difference objects are shown in the PCB editor.";
        }

        /// <summary>
        /// Tolerance is the smallest area of difference that counts, in square internal units (1 square mil =
        /// 100,000,000).
        /// </summary>
        private static double ParseTolerance(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double tolerance)
                || double.IsNaN(tolerance) || tolerance < 0 || double.IsInfinity(tolerance))
                throw new UserException($"Tolerance must be a number >= 0 (square internal units), not '{text}'.");
            return tolerance;
        }

        /// <summary>
        /// Returns the full path of an existing file from parameter <paramref name="name"/>. From a menu click
        /// (no parameter) the user picks the file instead; on the pipe the parameter is required.
        /// </summary>
        private static string ResolvePath(CommandContext ctx, string name, string dialogTitle)
        {
            string path = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(path))
            {
                if (!ctx.IsInteractive)
                    throw new UserException($"No {name} specified. Pass BoardA=<path to .PcbDoc>|BoardB=<path to .PcbDoc>.");
                path = PickFile(dialogTitle) ?? throw new UserException("Cancelled.");
            }

            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(path))
                throw new UserException($"{name}: file not found: {path}");
            return path;
        }

        private static string PickFile(string title)
        {
            using var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "PCB documents (*.PcbDoc)|*.PcbDoc|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }
    }
}
