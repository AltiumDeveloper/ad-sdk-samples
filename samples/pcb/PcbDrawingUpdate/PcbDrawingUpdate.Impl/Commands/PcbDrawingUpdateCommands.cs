using System;
using System.IO;
using System.Windows.Forms;
using DXP; // IClient.OpenDocument / GetDocumentByPath / ShowDocument / GetCurrentView and GetOwnerDocument are extension methods

namespace AdSamples.PcbDrawingUpdate
{
    /// <summary>
    /// Opens a Draftsman PCB drawing (.PCBDwf) and runs the Draftsman editor's own "Import Changes From Board"
    /// process on it. The SDK lesson: an extension opens a document of another editor with IClient.OpenDocument
    /// (by document kind), focuses it with IClient.ShowDocument, and then drives that editor through its process
    /// with DXP.Utils.RunCommand, which acts on the focused document.
    /// </summary>
    internal static class PcbDrawingUpdateCommands
    {
        // The document kind of a .PCBDwf: the EditorWindowKind Name of the Draftsman server, whose FileExtensions
        // entry maps 'PCBDwf = PcbDrawing' (Extensions\Draftsman\Altium.Designer.PcbDrawing.ins).
        private const string DrawingKind = "PcbDrawing";

        // The Draftsman process behind Tools > Import Changes From Board (Altium.Designer.PcbDrawing.rcs,
        // PL PLPcbDrawing:UpdateBoard). It runs with no parameters; none are documented.
        private const string UpdateBoardProcess = "PcbDrawing:UpdateBoard";

        /// <summary>
        /// OpenDrawing "File=&lt;.PCBDwf&gt;|Update=false": opens the drawing (or reuses it if already open), focuses it
        /// and, unless Update=false, runs PcbDrawing:UpdateBoard on it. Nothing is saved.
        /// </summary>
        [HotCommand("OpenDrawing", "Open a PCB drawing (.PCBDwf) and import changes from its board")]
        public static string OpenDrawing(CommandContext ctx)
        {
            // 1. The file. It is a parameter, and the menu (no File) asks for it with a file dialog. On the pipe path a dialog would block AD, so it is required.
            string file = ctx.Get("File", "").Trim().Trim('"');
            if (file.Length == 0)
            {
                if (!ctx.IsInteractive)
                    throw new UserException("No drawing specified. Pass it as a parameter: File=<path of a .PCBDwf file>.");
                file = PickDrawing();
                if (file == null)
                    return "Cancelled: no drawing selected.";
            }
            file = Path.GetFullPath(file);
            if (!File.Exists(file))
                throw new UserException($"'{file}' does not exist.");
            if (!Path.GetExtension(file).Equals(".PCBDwf", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"'{file}' is not a .PCBDwf file.");

            bool update = !string.Equals(ctx.Get("Update", "true"), "false", StringComparison.OrdinalIgnoreCase);

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // 2. Open the drawing by kind. If it is already open, OpenDocument is not needed: reuse that document, and
            // report it, because an update then also changes a document the user may have been editing.
            IServerDocument document = client.GetDocumentByPath(file);
            bool wasOpen = document != null;
            if (!wasOpen)
            {
                ctx.Log($"OpenDocument(\"{DrawingKind}\", \"{file}\")");
                // Without a document, stop here instead of running the process on
                // whatever document has the focus.
                document = client.OpenDocument(DrawingKind, file)
                    ?? throw new UserException($"IClient.OpenDocument(\"{DrawingKind}\", ...) returned no document for '{file}'.");
            }
            if (!string.Equals(document.GetKind(), DrawingKind, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"'{file}' opened as kind '{document.GetKind()}', not '{DrawingKind}'. Nothing was updated.");

            // 3. Show and focus it. IClient.ShowDocument names the document itself, so no index into the
            // hidden-documents list (WorkspaceManager:ShowHiddenDocument Index=...) is needed. OpenDocument alone
            // does not focus or raise the drawing.
            client.ShowDocument(document);

            // RunCommand acts on the focused document, so make sure it is this drawing before running the process:
            // otherwise the update would hit another drawing, or do nothing in another editor.
            IServerDocument focused = client.GetCurrentView()?.GetOwnerDocument();
            string focusedFile = focused?.GetFileName() ?? "";
            if (!string.Equals(focusedFile, document.GetFileName(), StringComparison.OrdinalIgnoreCase))
                throw new UserException(
                    $"After ShowDocument the focused document is '{(focusedFile.Length > 0 ? focusedFile : "(none)")}', " +
                    $"not the drawing '{file}'. {UpdateBoardProcess} was not run.");

            string opened = $"{Path.GetFileName(file)}: kind '{document.GetKind()}', {(wasOpen ? "was already open" : "opened")}, " +
                            $"shown and focused. Modified before update: {document.GetModified()}.";
            if (!update)
                return opened + $" {UpdateBoardProcess} not run (Update=false).";

            // 4. Import the board's changes into the drawing. The process belongs to the Draftsman editor; it updates
            // the drawing from the board file the drawing references (which board is used is not documented).
            ctx.Log($"RunCommand(\"{UpdateBoardProcess}\", \"\")");
            DXP.Utils.RunCommand(UpdateBoardProcess, "");

            // RunCommand returns nothing. The modified flag is the only read-back: it shows whether the update changed
            // the drawing in memory. Nothing is saved; close the drawing without saving to discard the update.
            return opened + $" Ran {UpdateBoardProcess}. Modified after update: {document.GetModified()}. " +
                   "Not saved: save the drawing to keep the update, or close it without saving.";
        }

        private static string PickDrawing()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select a PCB drawing",
                Filter = "Draftsman documents (*.PCBDwf)|*.PCBDwf|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }
    }
}
