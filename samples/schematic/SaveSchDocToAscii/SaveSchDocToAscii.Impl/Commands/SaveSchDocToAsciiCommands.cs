using System;
using System.IO;
using System.Text;
using DXP;

namespace AdSamples.SaveSchDocToAscii
{
    /// <summary>
    /// Saves the active schematic document in another file format through the generic document container,
    /// DXP.IServerDocument: point the document at a new file name, then DoFileSave with the name of a save filter.
    /// </summary>
    internal static class SaveSchDocToAsciiCommands
    {
        // The default save filter name passed to DoFileSave. The DoFileSave reference lists it among the
        // schematic formats; the editor's .ins SaveFilters blocks hold the exact names.
        private const string DefaultFormat = "Advanced Schematic ascii";

        /// <summary>
        /// SaveToAscii "Out=&lt;file or folder&gt;|Format=&lt;save filter&gt;|Keep=true": writes the active schematic
        /// in ASCII format and reports the file written.
        /// </summary>
        [HotCommand("SaveToAscii", "Save the active schematic in ASCII format")]
        public static string SaveToAscii(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            IServerDocumentView view = client.GetCurrentView()
                ?? throw new UserException("No document is active. Open and focus a .SchDoc first.");
            IServerDocument document = view.GetOwnerDocument()
                ?? throw new UserException("The active view has no document. Focus a .SchDoc first.");

            // GetKind() returns the document kind string; "SCH" is a schematic sheet.
            string kind = document.GetKind() ?? string.Empty;
            if (!kind.Equals("SCH", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The active document is not a schematic (kind '{kind}'). Focus a .SchDoc first.");

            string originalName = document.GetFileName();
            string target = TargetPath(ctx, originalName);
            string format = NonEmpty(ctx.Get("Format"), DefaultFormat);
            bool keep = ctx.Flag("Keep");
            bool wasModified = document.GetModified();
            bool existed = File.Exists(target);

            // DoFileSave(kind) takes no file name: it saves to the document's FileName. So the
            // document is first pointed at the target. Unless Keep is set, the open document is pointed back at its
            // own file afterwards, with its modified flag as it was: the copy on disk does not save the open document.
            document.SetFileName(target);
            bool saved;
            try
            {
                saved = document.DoFileSave(format);
            }
            finally
            {
                if (!keep)
                {
                    document.SetFileName(originalName);
                    document.SetModified(wasModified);
                }
            }

            var result = new StringBuilder();
            result.Append($"DoFileSave('{format}') returned {saved}. ");
            if (File.Exists(target))
            {
                var info = new FileInfo(target);
                result.Append($"{(existed ? "Overwrote" : "Wrote")} {target} ({info.Length} bytes, {DescribeContent(target)}).");
            }
            else
            {
                result.Append($"No file at {target}.");
            }
            result.Append(keep
                ? $" The open document now points at {target}."
                : $" The open document still points at {Path.GetFileName(originalName)}.");
            return result.ToString();
        }

        /// <summary>Out may be a file or an existing folder; default: the temp folder, named after the sheet.</summary>
        private static string TargetPath(CommandContext ctx, string originalName)
        {
            string fileName = Path.GetFileNameWithoutExtension(NonEmpty(originalName, "Sheet")) + "_Ascii.SchDoc";
            string outParam = ctx.Get("Out");
            if (string.IsNullOrWhiteSpace(outParam))
                return Path.Combine(Path.GetTempPath(), fileName);

            string full = Path.GetFullPath(outParam);
            string target = Directory.Exists(full) ? Path.Combine(full, fileName) : full;

            // Checked on the final target, so a folder that holds a sheet already named <x>_Ascii.SchDoc is caught too.
            if (!string.IsNullOrWhiteSpace(originalName) &&
                string.Equals(target, Path.GetFullPath(originalName), StringComparison.OrdinalIgnoreCase))
                throw new UserException("The target is the document's own file. Give another file name or a folder.");

            string folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                throw new UserException($"The folder {folder} does not exist.");
            return target;
        }

        /// <summary>
        /// Classifies the file by its first bytes: binary SchDocs are OLE compound files (D0 CF 11 E0), an ASCII
        /// save should be plain text. Returns the first characters of a text file so the format can be checked.
        /// </summary>
        private static string DescribeContent(string path)
        {
            byte[] head = new byte[64];
            int read;
            using (var stream = File.OpenRead(path))
                read = stream.Read(head, 0, head.Length);

            if (read >= 4 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
                return "binary: OLE compound file";

            for (int i = 0; i < read; i++)
            {
                byte b = head[i];
                if (b < 0x09 || (b > 0x0D && b < 0x20))
                    return "binary: unknown format";
            }
            string start = Encoding.ASCII.GetString(head, 0, read).Replace("\r", " ").Replace("\n", " ");
            return $"text, starts with '{start}'";
        }

        private static string NonEmpty(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
