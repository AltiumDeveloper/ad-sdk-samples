using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;

namespace AdSamples.SavePcbAsAscii
{
    /// <summary>
    /// Saves a copy of the active PCB document in the "PCB ASCII File" format, then puts the document back as it was:
    /// same file name, same Modified flag, same file modified date. The technique: point the document at a temporary
    /// file name, save it with IServerDocument.DoFileSave(kind), and restore the name, all inside
    /// IClient.BeginRecoverySave / EndRecoverySave (documented to suppress the client's notification of document name
    /// changes).
    /// </summary>
    internal static class SavePcbAsAsciiCommands
    {
        /// <summary>The save format name passed to DoFileSave (not listed in the SDK reference).</summary>
        private const string AsciiKind = "PCB ASCII File";

        /// <summary>
        /// SaveAsAscii "Out=&lt;folder or .PcbDoc file&gt;|AutoDismiss=false|DialogTitle=&lt;title&gt;".
        /// Out defaults to the temp folder; the copy is named &lt;document&gt;.ascii.PcbDoc there.
        /// </summary>
        [HotCommand("SaveAsAscii", "Save a copy of the PCB as ASCII")]
        public static string SaveAsAscii(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client;
            IServerDocument document = CurrentPcbDocument(client);

            string oldFileName = document.GetFileName();
            bool oldModified = document.GetModified();
            double oldModifiedDate = document.GetFileModifiedDate();

            string outParam = ctx.Get("Out");
            string target = TargetPath(string.IsNullOrWhiteSpace(outParam) ? Path.GetTempPath() : outParam, oldFileName);
            // The copy must never replace the board itself (an ASCII file under the binary board's name), nor
            // any other document that is open in AD.
            if (string.Equals(target, Path.GetFullPath(oldFileName), StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Out points at the document itself ({target}). Choose another file or folder.");
            if (client.IsDocumentOpen(target))
                throw new UserException($"{target} is open in Altium Designer. Choose another file or folder.");
            if (client.InRecoverySave())
                throw new UserException("Altium Designer is already in a recovery save. Try again later.");

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            // Start from no file, so the checks below see what this save wrote.
            if (File.Exists(target))
                File.Delete(target);

            bool autoDismiss = !string.Equals(ctx.Get("AutoDismiss", "true"), "false", StringComparison.OrdinalIgnoreCase);
            string dialogTitle = ctx.Get("DialogTitle", "Information");

            bool saved;
            DialogDismisser dismisser = null;
            client.BeginRecoverySave();
            try
            {
                document.SetFileName(target);
                document.UpdateModifiedDate();
                if (autoDismiss)
                    dismisser = new DialogDismisser(dialogTitle);
                ctx.Log($"Saving as '{AsciiKind}' to {target}");
                saved = document.DoFileSave(AsciiKind);
            }
            finally
            {
                // Always undo the swap and close the recovery-save bracket, even if the save threw.
                dismisser?.Dispose();
                document.SetFileName(oldFileName);
                document.SetModified(oldModified);
                document.SetFileModifiedDate(oldModifiedDate);
                client.EndRecoverySave();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"DoFileSave('{AsciiKind}') returned {saved}.");
            sb.AppendLine(DescribeOutput(target));
            sb.AppendLine(CompareState(document, oldFileName, oldModified, oldModifiedDate));
            if (dismisser == null)
                sb.AppendLine("Dialog dismisser: off (AutoDismiss=false).");
            else
            {
                var dismissed = dismisser.Dismissed;
                sb.AppendLine(dismissed.Count == 0
                    ? $"Dialog dismisser: no '{dialogTitle}' dialog seen."
                    : "Dialog dismisser: " + string.Join("; ", dismissed));
                if (!dismisser.Stopped)
                    sb.AppendLine("Dialog dismisser: the thread did not stop within 2 s.");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>DocumentState: the file name, Modified flag and file modified date of the active PCB document.</summary>
        [HotCommand("DocumentState", "Show the PCB document's file name, Modified flag and date")]
        public static string DocumentState(CommandContext ctx)
        {
            IServerDocument document = CurrentPcbDocument(DXP.GlobalVars.Client);
            return $"{document.GetFileName()}: Modified={document.GetModified()}, " +
                   $"FileModifiedDate={FormatDate(document.GetFileModifiedDate())}";
        }

        /// <summary>SetModified "Modified=true|false": test setup, so SaveAsAscii can be tried with both flag values.</summary>
        [HotCommand("SetModified", "Set the PCB document's Modified flag (test setup)")]
        public static string SetModified(CommandContext ctx)
        {
            string value = ctx.Get("Modified", "true");
            if (!bool.TryParse(value, out bool modified))
                throw new UserException($"Modified must be true or false, not '{value}'.");

            IServerDocument document = CurrentPcbDocument(DXP.GlobalVars.Client);
            document.SetModified(modified);
            return $"{Path.GetFileName(document.GetFileName())}: Modified={document.GetModified()}";
        }

        private static IServerDocument CurrentPcbDocument(IClient client)
        {
            IServerDocument document = client?.GetCurrentView()?.GetOwnerDocument()
                ?? throw new UserException("No document is active. Open and focus a .PcbDoc first.");
            if (!string.Equals(document.GetKind(), "PCB", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The active document is a {document.GetKind()} document, not a PCB. Focus a .PcbDoc first.");
            return document;
        }

        /// <summary>Out is a .PcbDoc file, or a folder that gets &lt;document&gt;.ascii.PcbDoc.</summary>
        private static string TargetPath(string outParam, string documentFileName)
        {
            if (outParam.EndsWith(".PcbDoc", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(outParam);
            return Path.GetFullPath(Path.Combine(outParam, Path.GetFileNameWithoutExtension(documentFileName) + ".ascii.PcbDoc"));
        }

        /// <summary>Size and first line of the written file, and whether its start is plain ASCII text.</summary>
        private static string DescribeOutput(string path)
        {
            if (!File.Exists(path))
                return $"No file was written at {path}.";

            long size = new FileInfo(path).Length;
            byte[] head = new byte[Math.Min(size, 512)];
            using (var stream = File.OpenRead(path))
                stream.ReadExactly(head);

            bool ascii = head.All(b => b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127));
            string firstLine = Encoding.ASCII.GetString(head).Split('\r', '\n')[0];
            if (firstLine.Length > 80)
                firstLine = firstLine.Substring(0, 80) + "...";
            return $"Wrote {path} ({size} bytes); first 512 bytes ASCII text: {ascii}; first line: {firstLine}";
        }

        private static string CompareState(IServerDocument document, string oldFileName, bool oldModified, double oldModifiedDate)
        {
            string fileName = document.GetFileName();
            bool modified = document.GetModified();
            double modifiedDate = document.GetFileModifiedDate();
            bool same = fileName == oldFileName && modified == oldModified && modifiedDate == oldModifiedDate;
            return $"Document state {(same ? "unchanged" : "CHANGED")}: FileName={fileName}" +
                   (fileName == oldFileName ? "" : $" (was {oldFileName})") +
                   $", Modified={modified}" + (modified == oldModified ? "" : $" (was {oldModified})") +
                   $", FileModifiedDate={FormatDate(modifiedDate)}" +
                   (modifiedDate == oldModifiedDate ? "" : $" (was {FormatDate(oldModifiedDate)})");
        }

        /// <summary>FileModifiedDate is shown as an OLE Automation date, with the raw value.</summary>
        private static string FormatDate(double oaDate)
        {
            string raw = oaDate.ToString("R", CultureInfo.InvariantCulture);
            try { return DateTime.FromOADate(oaDate).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + $" ({raw})"; }
            catch (ArgumentException) { return raw; }
        }
    }
}
