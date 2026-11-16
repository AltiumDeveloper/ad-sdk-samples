using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using DXP; // the IClient.OpenDocument/ShowDocument/CloseDocument/GetDocumentByPath extension methods live in namespace DXP
using PCB;

namespace AdSamples.PcbLibToImage
{
    /// <summary>
    /// Exports every footprint of a PCB library as a PNG file. The SDK lesson: IPCB_ServerInterface.PaintFootprintThumbnail
    /// renders a footprint (by name and library file path) into a GDI bitmap and returns its handle; .NET turns the
    /// handle into a Bitmap with Image.FromHbitmap.
    /// </summary>
    internal static class PcbLibToImageCommands
    {
        private const int DefaultSize = 500;
        private const int MaxSize = 4096;

        /// <summary>
        /// GenerateImage "Lib=&lt;.PcbLib path&gt;|Out=&lt;folder&gt;|Size=500": writes
        /// &lt;Out&gt;/&lt;library name&gt;/&lt;footprint&gt;.png for every footprint. Without Lib, the focused PCB library is used.
        /// </summary>
        [HotCommand("GenerateImage", "Export the footprints of a PCB library as PNG images")]
        public static string GenerateImage(CommandContext ctx)
        {
            int size = ParseSize(ctx.Get("Size", DefaultSize.ToString(CultureInfo.InvariantCulture)));

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            string libValue = ctx.Get("Lib");
            IServerDocument openedHere = null;
            IPCB_Library library;
            string libPath;
            string how;

            if (string.IsNullOrWhiteSpace(libValue))
            {
                library = pcbServer.GetCurrentPCBLibrary()
                    ?? throw new UserException("No PCB library is active. Focus a .PcbLib, or pass Lib=<path to .PcbLib>.");
                libPath = library.GetState_Board()?.GetState_FileName() ?? "";
                how = "focused library";
            }
            else
            {
                libPath = Path.GetFullPath(libValue.Trim().Trim('"'));
                if (!File.Exists(libPath))
                    throw new UserException($"PCB library not found: {libPath}");

                // Only close at the end what this command opened, so that a document the user had open before
                // stays open.
                if (client.GetDocumentByPath(libPath) == null)
                {
                    openedHere = client.OpenDocument("PCBLib", libPath)
                        ?? throw new UserException($"Altium Designer could not open {libPath} as a PCB library.");
                }
                try
                {
                    (library, how) = FindLibrary(pcbServer, client, libPath);
                }
                catch
                {
                    CloseIfOpenedHere(client, openedHere);
                    throw;
                }
            }

            // PaintFootprintThumbnail takes the library *file* path, so the library must exist on disk.
            if (!File.Exists(libPath))
            {
                CloseIfOpenedHere(client, openedHere);
                throw new UserException($"The PCB library has no file on disk ('{libPath}'). Save it first.");
            }

            string folder = Path.Combine(Path.GetFullPath(ctx.Get("Out", Path.GetTempPath())),
                                         Path.GetFileNameWithoutExtension(libPath));
            var written = new List<string>();
            var failed = new List<string>();
            int footprints = 0;

            IPCB_LibraryIterator iterator = library.LibraryIterator_Create();
            try
            {
                Directory.CreateDirectory(folder);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (IPCB_Primitive item = iterator.FirstPCBObject(); item != null; item = iterator.NextPCBObject())
                {
                    if (item is not IPCB_LibComponent footprint)
                        continue;
                    footprints++;

                    string name = footprint.GetState_Pattern();
                    string file = Path.Combine(folder, UniqueFileName(name, usedNames) + ".png");
                    ctx.Log($"{footprints}: {name}");

                    // Returns 0 when the footprint cannot be loaded from the file (reference remarks).
                    IntPtr hBitmap = PaintFootprintThumbnailLateBound(pcbServer, name, libPath, size);
                    if (hBitmap == IntPtr.Zero)
                    {
                        failed.Add(name);
                        continue;
                    }

                    // FromHbitmap copies the pixels into a new Bitmap; disposing it does not free hBitmap. Who frees
                    // the handle is not documented, and the .NET API has no call for it, so the sample leaves it
                    // alone (README Notes). Save with ImageFormat.Png: Save(path) without a format writes BMP
                    // data into the .png files.
                    using (Bitmap bitmap = Image.FromHbitmap(hBitmap))
                        bitmap.Save(file, ImageFormat.Png);
                    written.Add(file);
                }
            }
            finally
            {
                library.LibraryIterator_Destroy(ref iterator);
                CloseIfOpenedHere(client, openedHere);
            }

            string result = $"{Path.GetFileName(libPath)} ({how}): {written.Count} of {footprints} footprints written " +
                            $"as {size}x{size} PNG to {folder}";
            if (failed.Count > 0)
                result += $"\nPaintFootprintThumbnail returned 0 for: {string.Join(", ", failed)}";
            if (openedHere != null)
                result += "\nThe library was opened for the export and closed again.";
            return result;
        }

        /// <summary>
        /// The library of a document that is open in the workspace. GetPCBLibraryByPath is tried first because it
        /// leaves the focus alone; ShowDocument and GetCurrentPCBLibrary are the fallback.
        /// </summary>
        private static (IPCB_Library, string) FindLibrary(IPCB_ServerInterface pcbServer, IClient client, string libPath)
        {
            IPCB_Library library = pcbServer.GetPCBLibraryByPath(libPath);
            if (library != null)
                return (library, "GetPCBLibraryByPath");

            IServerDocument document = client.GetDocumentByPath(libPath)
                ?? throw new UserException($"{libPath} is not open in the workspace.");
            client.ShowDocument(document);
            library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException($"{libPath} is open but is not a PCB library.");

            // Check that the focused library really is the requested one, not another document.
            string current = library.GetState_Board()?.GetState_FileName();
            if (!string.Equals(current, libPath, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The current PCB library is '{current}', not '{libPath}'.");
            return (library, "ShowDocument + GetCurrentPCBLibrary");
        }

        /// <summary>
        /// Calls PaintFootprintThumbnail late-bound and returns the HBITMAP. Workaround: the typed wrapper declares a
        /// uint result and forwards the call through IDispatch; it can throw
        /// OverflowException (OAVariantLib.ChangeType) on the third footprint, after two successful calls. Late-bound,
        /// the raw result is converted here; a 64-bit value is taken as the sign-extended handle.
        /// </summary>
        private static IntPtr PaintFootprintThumbnailLateBound(IPCB_ServerInterface server, string name, string libPath, int size)
        {
            object raw = server.GetType().InvokeMember("PaintFootprintThumbnail", System.Reflection.BindingFlags.InvokeMethod,
                                                       null, server, new object[] { name, libPath, size, size });
            switch (raw)
            {
                case null: return IntPtr.Zero;
                case ulong u64: return new IntPtr(unchecked((long)u64));
                case long i64: return new IntPtr(i64);
                case uint u32: return new IntPtr(unchecked((int)u32)); // sign-extend like a Windows handle
                case int i32: return new IntPtr(i32);
                default: return new IntPtr(Convert.ToInt64(raw, CultureInfo.InvariantCulture));
            }
        }

        private static void CloseIfOpenedHere(IClient client, IServerDocument document)
        {
            if (document != null)
                client.CloseDocument(document);
        }

        private static int ParseSize(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ||
                size < 1 || size > MaxSize)
                throw new UserException($"Size must be a whole number of pixels from 1 to {MaxSize} (got '{value}').");
            return size;
        }

        /// <summary>The footprint name without characters a file name can't have, made unique within the folder.</summary>
        private static string UniqueFileName(string name, HashSet<string> used)
        {
            string clean = name ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c.ToString(), "");
            if (clean.Length == 0)
                clean = "_";

            string candidate = clean;
            for (int i = 2; !used.Add(candidate); i++)
                candidate = $"{clean}_{i}";
            return candidate;
        }
    }
}
