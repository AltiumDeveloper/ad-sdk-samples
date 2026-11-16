using System;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;

namespace AdSamples.TextDocumentEdit
{
    /// <summary>
    /// Creates a new Text document, writes three lines with editor commands (EDP.ITextCommands), then edits the
    /// lines directly by index through DXP.ITextDocument (LineCount, GetLine, SetLine).
    /// </summary>
    internal static class TextDocumentEditCommands
    {
        // By default 'line' (case-insensitive) is replaced with 'LINE1' + CRLF + 'LINE2'.
        private const string DefaultFind = "line";
        private const string DefaultReplace = @"LINE1\r\nLINE2";

        [HotCommand("ReplaceText", "Create a text document and replace text line by line")]
        public static string ReplaceText(CommandContext ctx)
        {
            string find = ctx.Get("Find", DefaultFind);
            if (string.IsNullOrEmpty(find))
                throw new UserException("Find must not be empty.");
            // A parameter string cannot carry a line break, so the escapes \r, \n and \t are expanded here.
            string replace = Unescape(ctx.Get("Replace", DefaultReplace));

            // DXP.Utils.CreateNewDocumentFromDocumentKind returns the new document's IServerDocument.
            IServerDocument document = DXP.Utils.CreateNewDocumentFromDocumentKind("Text")
                ?? throw new UserException("Altium Designer did not create a Text document.");

            // Step 1: write the lines with editor commands at the cursor.
            ITextCommands commands = ResolveTextCommands(document, ctx, out string commandSource);
            Insert(commands, "First Line");
            Run(commands.MoveCursorToEndOfLine);
            Run(commands.InsertReturn);
            Insert(commands, "Second Line");
            Run(commands.MoveCursorToEndOfLine);
            Run(commands.InsertReturn);
            Run(commands.MoveCursorToTopOfDocument);
            Run(commands.InsertLine);
            Insert(commands, "Line before first line");
            Run(commands.SelectLine);

            // Step 2: edit by line index. The same IServerDocument is cast to ITextDocument. SDK
            // interfaces are dispatch interfaces, so the cast always succeeds; the first call (LineCount) shows
            // whether the document really implements it.
            var text = (ITextDocument)document;
            int before;
            try
            {
                before = text.LineCount();   // a method, not a property
            }
            catch (COMException ex)
            {
                throw new UserException($"The Text document does not implement ITextDocument: {ex.Message}");
            }

            var result = new StringBuilder();
            result.AppendLine($"Created Text document '{document.GetFileName()}' (ITextCommands on the {commandSource}).");
            result.AppendLine($"Before: {before} line(s)");
            AppendLines(result, text, before);

            // Walk backwards: if SetLine with an embedded line break splits the line, the
            // indexes of the lines not yet visited (all above) stay valid.
            int changed = 0;
            for (int i = before - 1; i >= 0; i--)
            {
                string line = string.Empty;
                text.GetLine(i, ref line);
                if (line == null)
                    continue;
                string edited = line.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
                if (edited == line)
                    continue;
                text.SetLine(i, edited);
                changed++;
            }

            int after = text.LineCount();
            result.AppendLine($"Replaced '{find}' in {changed} line(s). After: {after} line(s)");
            AppendLines(result, text, after);
            return result.ToString().TrimEnd();
        }

        private delegate void TextCommandCall(ref string parameters);

        // Every ITextCommands method takes its parameter string by ref; a fresh local is used per call because
        // the callee may write to it.
        private static void Run(TextCommandCall call)
        {
            string parameters = string.Empty;
            call(ref parameters);
        }

        private static void Insert(ITextCommands commands, string value)
        {
            // InsertText takes the text to insert as its parameter string.
            string parameters = value;
            commands.InsertText(ref parameters);
        }

        private static ITextCommands ResolveTextCommands(IServerDocument document, CommandContext ctx, out string source)
        {
            // Which object implements ITextCommands (the document or its view) is not documented, so the
            // document is tried first. MoveCursorToBeginningOfLine is the first call and changes nothing
            // in an empty document, so it serves as the probe; on failure the document's first view is tried.
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

        private static void AppendLines(StringBuilder result, ITextDocument text, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string line = string.Empty;
                text.GetLine(i, ref line);
                result.AppendLine(line == null ? $"  [{i}] (null)" : $"  [{i}] '{Escape(line)}'");
            }
        }

        // Shows embedded line breaks visibly, so a line that holds a CRLF is distinguishable from two lines.
        private static string Escape(string s) =>
            s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

        private static string Unescape(string s) =>
            s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
    }
}
