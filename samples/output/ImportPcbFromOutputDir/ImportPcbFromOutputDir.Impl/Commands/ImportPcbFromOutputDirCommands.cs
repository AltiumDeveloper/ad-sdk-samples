using System;
using System.IO;
using System.Linq;
using DXP; // IClient.OpenDocument / GetDocumentByPath are extension methods in namespace DXP
using EDP;

namespace AdSamples.ImportPcbFromOutputDir
{
    /// <summary>
    /// FileImportDirectory can be the handler of an output job's generated files (OutputHandler entries in an
    /// .ins): AD passes the export folder as BaseExportPath, and the extension opens the board the outputs were made
    /// from (path to base folder to .PcbDoc to IClient.OpenDocument). A custom document kind, its editor, panel views
    /// and options page are not part of this sample (README, Notes).
    /// </summary>
    internal static class ImportPcbFromOutputDirCommands
    {
        /// <summary>The path segment to look for. Everything from it onwards is cut off.</summary>
        private const string OutputSegment = "Unit Output files";

        [HotCommand("FileImportDirectory", "Open the .PcbDoc of an output job's base folder")]
        public static string FileImportDirectory(CommandContext ctx)
        {
            // Each of the failures below says why.
            string exportPath = ctx.Get("BaseExportPath", "").Trim().Trim('"');
            if (exportPath.Length == 0)
                throw new UserException($"No BaseExportPath specified. Pass BaseExportPath=<a path containing '{OutputSegment}'>.");

            // Case-insensitive, because Windows paths are not case-sensitive.
            int pos = exportPath.IndexOf(OutputSegment, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
                throw new UserException($"BaseExportPath '{exportPath}' has no '{OutputSegment}' segment.");
            string baseText = exportPath.Substring(0, pos);
            if (baseText.Trim().Length == 0)
                throw new UserException($"BaseExportPath '{exportPath}' has nothing before '{OutputSegment}'.");

            // Path.GetFullPath + Directory.EnumerateFiles do not depend on whether the cut string ends with a
            // separator.
            string baseFolder = Path.GetFullPath(baseText);
            if (!Directory.Exists(baseFolder))
                throw new UserException($"The base folder '{baseFolder}' does not exist.");

            // Sorting by name makes the choice repeatable.
            // The extension test is exact because a "*.PcbDoc" pattern can also match longer extensions.
            string[] boards = Directory.EnumerateFiles(baseFolder, "*.PcbDoc", SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(Path.GetExtension(f), ".PcbDoc", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (boards.Length == 0)
                throw new UserException($"No .PcbDoc in '{baseFolder}'.");
            string board = boards[0];

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            // Report whether this command opened the board or found it open already, so a test can tell them apart.
            bool wasOpen = client.GetDocumentByPath(board) != null;
            IServerDocument document = client.OpenDocument("PCB", board)
                ?? throw new UserException($"Altium Designer could not open '{board}' as a PCB document.");

            string others = boards.Length > 1 ? $" ({boards.Length} .PcbDoc files in the folder, took the first by name)" : "";
            return $"{(wasOpen ? "Already open" : "Opened")}: {Path.GetFileName(board)} (kind {document.GetKind()}) " +
                   $"from base folder {baseFolder}{others}.";
        }

        /// <summary>
        /// The enable test of a "New CAM350 document" command (GetState_NewExtension_CAM350): offered only
        /// for a real project (not Free Documents) that does not hold a document of that kind yet. The command itself
        /// (WorkspaceManager:OpenObject ObjectKind=NewAnything|Kind=Extension_CAM350) needs a registered document kind,
        /// which this sample does not have, so only the test is here, as a report.
        /// </summary>
        [HotCommand("CheckDocumentKind", "Report whether the focused project holds a document of a kind")]
        public static string CheckDocumentKind(CommandContext ctx)
        {
            string kind = ctx.Get("Kind", "Extension_CAM350").Trim();
            if (kind.Length == 0)
                throw new UserException("Kind must not be empty (for example Kind=PCB or Kind=SCH).");

            IProject project = AltiumSession.Workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused.");
            string projectKind = project.DM_ObjectKindString();
            string name = Path.GetFileName(project.DM_ProjectFullPath());
            if (string.IsNullOrEmpty(name))
                name = projectKind;

            // DM_ObjectKindString is compared with "Free Documents", and DM_DocumentKind exactly.
            if (projectKind == "Free Documents")
                return $"Focused project: {name} (Free Documents). Its New menu item is hidden here.";

            int count = project.DM_LogicalDocumentCount();
            int matches = 0;
            string first = null;
            for (int i = 0; i < count; i++)
            {
                IDocument document = project.DM_LogicalDocuments(i);
                if (document != null && document.DM_DocumentKind() == kind)
                {
                    matches++;
                    first ??= document.DM_FileName();
                }
            }

            return matches > 0
                ? $"Focused project: {name} ({projectKind}), {count} logical document(s), {matches} of kind '{kind}' (first: {first}). Its New menu item is hidden."
                : $"Focused project: {name} ({projectKind}), {count} logical document(s), none of kind '{kind}'. Its New menu item is offered.";
        }
    }
}
