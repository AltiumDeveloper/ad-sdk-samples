using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;

namespace AdSamples.TextDocumentCommands
{
    /// <summary>
    /// Creates a new Text document and fills it with editor commands (EDP.ITextCommands): cursor moves,
    /// InsertText, InsertReturn, InsertLine and SelectLine, the same calls a user makes with the keyboard.
    /// </summary>
    internal static class TextDocumentCommandsCommands
    {
        [HotCommand("TextCommand", "Create a text document and fill it through ITextCommands")]
        public static string TextCommand(CommandContext ctx)
        {
            // DXP.Utils.CreateNewDocumentFromDocumentKind returns the new document's IServerDocument.
            IServerDocument document = DXP.Utils.CreateNewDocumentFromDocumentKind("Text")
                ?? throw new UserException("Altium Designer did not create a Text document.");

            // The document is cast to ITextCommands. Every SDK interface is a dispatch interface with
            // the IDispatch GUID, so the cast itself always succeeds (docs: "Interfaces at run time"); only the
            // first call shows whether the object implements the commands. Which object does (the document or
            // its view) is not documented, so try the document first and fall back to its first view.
            ITextCommands commands = ResolveTextCommands(document, ctx, out string source);

            // Every ITextCommands method takes the command's parameter string by reference. An empty string is
            // passed; a fresh local is used for each call because the callee may write to it.
            Run(commands.MoveCursorToBeginningOfLine);
            Insert(commands, "First Line");
            Run(commands.MoveCursorToEndOfLine);
            Run(commands.InsertReturn);
            Insert(commands, "Second Line");
            Run(commands.MoveCursorToEndOfLine);
            Run(commands.InsertReturn);
            Run(commands.MoveCursorToTopOfDocument);
            Run(commands.InsertLine);                 // opens an empty line above the cursor's line
            Insert(commands, "Line before first line");
            Run(commands.SelectLine);

            var result = new StringBuilder();
            result.AppendLine($"Created a new Text document '{document.GetFileName()}' and filled it via ITextCommands on the {source}.");
            result.Append(ReadBack(document));
            return result.ToString().TrimEnd();
        }

        private delegate void TextCommandCall(ref string parameters);

        private static void Run(TextCommandCall call)
        {
            string parameters = string.Empty;
            call(ref parameters);
        }

        private static void Insert(ITextCommands commands, string text)
        {
            // InsertText takes the text to insert as its parameter string.
            string parameters = text;
            commands.InsertText(ref parameters);
        }

        private static ITextCommands ResolveTextCommands(IServerDocument document, CommandContext ctx, out string source)
        {
            // MoveCursorToBeginningOfLine on an empty document changes nothing, so it serves as the probe.
            var fromDocument = (ITextCommands)document;
            try
            {
                Run(fromDocument.MoveCursorToBeginningOfLine);
                source = "IServerDocument";
                return fromDocument;
            }
            catch (COMException ex)
            {
                ctx.Log($"The IServerDocument does not take ITextCommands ({ex.Message}); trying its first view.");
            }

            IServerDocumentView view = document.GetCount() > 0 ? document.GetView(0) : null;
            if (view == null)
                throw new UserException("The new Text document does not implement ITextCommands and has no view to try.");

            var fromView = (ITextCommands)view;
            try
            {
                Run(fromView.MoveCursorToBeginningOfLine);
            }
            catch (COMException ex)
            {
                throw new UserException($"Neither the Text document nor its view implements ITextCommands: {ex.Message}");
            }
            source = "IServerDocumentView";
            return fromView;
        }

        /// <summary>
        /// Reads the lines back through DXP.ITextDocument so the result can be checked
        /// without looking at the editor. Whether the document implements ITextDocument is not documented, so a
        /// failure is reported in the text instead of failing the command.
        /// </summary>
        private static string ReadBack(IServerDocument document)
        {
            try
            {
                var text = (ITextDocument)document;
                var lines = new StringBuilder();
                int count = text.LineCount();
                lines.AppendLine($"Line count: {count}");
                for (int i = 0; i < count; i++)
                {
                    string line = string.Empty;
                    text.GetLine(i, ref line);
                    lines.AppendLine($"  [{i}] '{line}'");
                }
                lines.AppendLine($"Selected text: '{text.GetSelectedText()}'");
                return lines.ToString();
            }
            catch (COMException ex)
            {
                return $"Read-back through ITextDocument is not available on this document: {ex.Message}";
            }
        }
    }
}
