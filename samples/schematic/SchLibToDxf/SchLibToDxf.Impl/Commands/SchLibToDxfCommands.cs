using System;
using System.IO;
using System.Text;
using DXP; // the IClient.OpenDocument/CloseDocument/GetDocumentByPath/GetCurrentView extension methods live in namespace DXP
using SCH;

namespace AdSamples.SchLibToDxf
{
    /// <summary>
    /// Attempts an AutoCAD DXF export of a schematic library: open the library, point the open
    /// document at the .dxf path with IServerDocument.SetFileName, then call IServerDocument.DoFileSave with the
    /// name of a save format. The SDK lesson: DoFileSave(kind) writes to the document's current FileName, but it
    /// only honours the editor's registered SaveFilters names - an unknown name (such as the AutoCAD
    /// one) is silently ignored, DoFileSave still returns true, and the editor's default format is written. So no
    /// DXF comes out, and the result text sniffs the file to show that.
    /// </summary>
    internal static class SchLibToDxfCommands
    {
        // An AutoCAD format name. DoFileSave ignores it: only the SchLib editor's SaveFilters names are
        // honoured, so this writes the native binary
        // library under the .dxf name. The parameter Filter lets a caller pass a real SaveFilters name instead.
        private const string DefaultFilter = "AutoCAD Files";

        /// <summary>
        /// SchLibToDxf "SchLibFileName=&lt;.SchLib&gt;|DXFFileName=&lt;.dxf&gt;|Filter=AutoCAD Files": without
        /// SchLibFileName the focused schematic library is used; without DXFFileName the file is
        /// &lt;Out&gt;/&lt;library name&gt;.dxf (Out defaults to the temp folder).
        /// </summary>
        [HotCommand("SchLibToDxf", "Attempt the AutoCAD DXF export of a schematic library and report what was written")]
        public static string SchLibToDxf(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running.");
            string filter = ctx.Get("Filter", DefaultFilter);

            // 1. Find the library document: the one named by SchLibFileName (opened if needed) or the focused one.
            string libValue = ctx.Get("SchLibFileName");
            IServerDocument document;
            IServerDocument openedHere = null;
            string libPath;
            if (string.IsNullOrWhiteSpace(libValue))
            {
                document = client.GetCurrentView()?.GetOwnerDocument()
                    ?? throw new UserException("No document is focused. Focus a .SchLib, or pass SchLibFileName=<path to .SchLib>.");
                libPath = document.GetFileName();
            }
            else
            {
                libPath = Path.GetFullPath(libValue.Trim().Trim('"'));
                if (!File.Exists(libPath))
                    throw new UserException($"Schematic library not found: {libPath}");

                document = client.GetDocumentByPath(libPath);
                if (document == null)
                {
                    ctx.Log($"Opening {libPath}");
                    // "SCHLib" is the document kind passed to OpenDocument.
                    document = openedHere = client.OpenDocument("SCHLib", libPath)
                        ?? throw new UserException($"Altium Designer could not open {libPath} as a schematic library.");
                }
            }

            try
            {
                // The schematic server's view of the same file must be a library (ISch_Lib), not a sheet.
                if (!(schServer.GetSchDocumentByPath(libPath) is ISch_Lib))
                    throw new UserException($"'{libPath}' is not an open schematic library. Focus a .SchLib, or pass SchLibFileName=<path to .SchLib>.");

                string dxfPath = Path.GetFullPath(ctx.Get("DXFFileName",
                    Path.Combine(ctx.Get("Out", Path.GetTempPath()), Path.GetFileNameWithoutExtension(libPath) + ".dxf")));
                if (string.Equals(dxfPath, libPath, StringComparison.OrdinalIgnoreCase))
                    throw new UserException("DXFFileName must differ from the library's own file name.");
                Directory.CreateDirectory(Path.GetDirectoryName(dxfPath));

                // Remember the previous state of the target to tell a fresh write from an old file.
                DateTime? before = File.Exists(dxfPath) ? File.GetLastWriteTimeUtc(dxfPath) : (DateTime?)null;

                // 2. DoFileSave writes to the document's FileName, so the document is re-pointed at the DXF path.
                // Without pointing it back the library would stay open under the .dxf name, so the name and the
                // Modified flag are restored afterwards.
                string originalName = document.GetFileName();
                bool wasModified = document.GetModified();
                bool saved;
                try
                {
                    document.SetFileName(dxfPath);
                    ctx.Log($"DoFileSave('{filter}') to {dxfPath}");
                    saved = document.DoFileSave(filter);
                }
                finally
                {
                    document.SetFileName(originalName);
                    document.SetModified(wasModified);
                }

                // 3. Report what is on disk: DoFileSave's return value alone does not say which format was written.
                return Describe(libPath, dxfPath, filter, saved, before, openedHere != null);
            }
            finally
            {
                // Close only what this command opened.
                if (openedHere != null)
                    client.CloseDocument(openedHere);
            }
        }

        private static string Describe(string libPath, string dxfPath, string filter, bool saved, DateTime? before, bool opened)
        {
            var text = new StringBuilder();
            text.Append($"{Path.GetFileName(libPath)}: DoFileSave('{filter}') returned {saved.ToString().ToLowerInvariant()}. ");

            if (!File.Exists(dxfPath))
                text.Append($"No file was written at {dxfPath}.");
            else if (before.HasValue && File.GetLastWriteTimeUtc(dxfPath) == before.Value)
                text.Append($"{dxfPath} was not changed (it existed before; content: {Sniff(dxfPath)}).");
            else
                text.Append($"Wrote {dxfPath}, {new FileInfo(dxfPath).Length} bytes, content: {Sniff(dxfPath)}.");

            text.Append(opened
                ? " The library was opened for the export and closed again."
                : " The document's file name was restored.");
            return text.ToString();
        }

        /// <summary>
        /// Classifies a file by its first bytes, so the result shows whether the save format really was DXF.
        /// ASCII DXF starts with group code 0 and SECTION, binary DXF with "AutoCAD Binary DXF", DWG with "AC10",
        /// and an Altium binary library is an OLE compound file (D0 CF 11 E0).
        /// </summary>
        private static string Sniff(string path)
        {
            byte[] head = new byte[32];
            int read;
            using (FileStream stream = File.OpenRead(path))
                read = stream.Read(head, 0, head.Length);
            if (read == 0)
                return "empty";

            string ascii = Encoding.ASCII.GetString(head, 0, read);
            if (ascii.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal))
                return "binary DXF";
            if (ascii.StartsWith("AC10", StringComparison.Ordinal))
                return "DWG (" + ascii.Substring(0, Math.Min(6, ascii.Length)) + ")";
            if (read >= 4 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
                return "OLE compound file (an Altium binary library, not DXF)";
            string trimmed = ascii.TrimStart();
            if (trimmed.StartsWith("0", StringComparison.Ordinal) && trimmed.Contains("SECTION"))
                return "ASCII DXF";
            return "unrecognised (first bytes: " + BitConverter.ToString(head, 0, Math.Min(read, 8)) + ")";
        }
    }
}
