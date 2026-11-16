using System;
using System.IO;
using System.Windows.Forms;

namespace AdSamples.CompareSchDocuments
{
    /// <summary>
    /// Opens Altium Designer's comparison of two schematic files (the Differences panel) by running the built-in
    /// process WorkspaceManager:Difference. The SDK lesson: any built-in process can be launched with
    /// DXP.Utils.RunCommand, with its parameters in the Altium "Key=Value|Key=Value" form.
    /// </summary>
    internal static class CompareSchDocumentsCommands
    {
        // VersionControl:CompareFiles with the same parameters throws NotSupportedException;
        // WorkspaceManager:Difference ("Show differences between documents" in WorkspaceManager.ins) with
        // Document1/Document2 opens both sheets and the Differences panel.
        // Neither process is documented in the SDK reference.
        private const string CompareProcess = "WorkspaceManager:Difference";

        [HotCommand("CompareSchFiles", "Open the diff viewer on two schematic files")]
        public static string CompareSchFiles(CommandContext ctx)
        {
            string document1 = ResolvePath(ctx, "Document1", "Select the first schematic");
            string document2 = ResolvePath(ctx, "Document2", "Select the second schematic");

            if (string.Equals(document1, document2, StringComparison.OrdinalIgnoreCase))
                throw new UserException("Document1 and Document2 are the same file. Pass two different files.");

            // The parameter string: Key=Value pairs separated by '|'. Windows paths cannot
            // contain '|', so no escaping is needed.
            string parameters = $"Document1={document1}|Document2={document2}";

            ctx.Log($"Running {CompareProcess} {parameters}");

            // RunCommand has no return value, and an unknown server or process is ignored silently (for example
            // NoSuchServer:NoSuchProcess). An error raised by the target server, however, comes back
            // as a .NET exception (for example NotSupportedException for VersionControl:CompareFiles). Callers of
            // RunCommand must therefore catch, or the user sees a raw stack trace.
            try
            {
                DXP.Utils.RunCommand(CompareProcess, parameters);
            }
            catch (Exception ex)
            {
                throw new UserException(
                    $"{CompareProcess} failed for '{document1}' and '{document2}': {ex.GetType().Name}: {ex.Message}. " +
                    "The comparison did not open.");
            }

            return $"Ran {CompareProcess} on '{Path.GetFileName(document1)}' and '{Path.GetFileName(document2)}'. " +
                   "RunCommand reports no result: the differences are listed in the Differences panel.";
        }

        /// <summary>
        /// Returns the full path of an existing file from parameter <paramref name="name"/>. From a menu click (no
        /// parameters) the user picks the file instead; on the pipe the parameter is required.
        /// </summary>
        private static string ResolvePath(CommandContext ctx, string name, string dialogTitle)
        {
            string path = ctx.Get(name);
            if (string.IsNullOrEmpty(path))
            {
                if (!ctx.IsInteractive)
                    throw new UserException($"No {name} specified. Pass Document1=<path to .SchDoc>|Document2=<path to .SchDoc>.");
                path = PickFile(dialogTitle) ?? throw new UserException("Cancelled.");
            }

            path = Path.GetFullPath(path.Trim('"'));
            if (!File.Exists(path))
                throw new UserException($"{name}: file not found: {path}");
            return path;
        }

        private static string PickFile(string title)
        {
            using var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "Schematic documents (*.SchDoc)|*.SchDoc|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }
    }
}
