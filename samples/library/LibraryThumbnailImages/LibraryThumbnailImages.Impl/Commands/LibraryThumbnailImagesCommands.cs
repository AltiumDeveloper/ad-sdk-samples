using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using DXP; // IClient.OpenDocument/ShowDocument/CloseDocument/GetDocumentByPath are extension methods in namespace DXP
using PCB;
using SCH;

namespace AdSamples.LibraryThumbnailImages
{
    /// <summary>
    /// Renders a PNG thumbnail of every symbol of the .SchLib files in one folder and of every footprint of the .PcbLib
    /// files in another. The SDK lesson: ISch_ServerInterface.PaintLoadedComponentThumbnail (a loaded symbol object)
    /// and IPCB_ServerInterface.PaintFootprintThumbnail (a footprint name plus the library file path) both draw into
    /// a new GDI bitmap and return its handle; .NET copies the pixels with Image.FromHbitmap.
    /// </summary>
    internal static class LibraryThumbnailImagesCommands
    {
        // Default sizes: 2000x2000 for symbols, 500x500 for footprints.
        private const int DefaultSchSize = 2000;
        private const int DefaultPcbSize = 500;
        private const int MaxSize = 4096;

        /// <summary>
        /// GenerateThumbnails "SchFolder=&lt;folder&gt;|PcbFolder=&lt;folder&gt;|Out=&lt;folder&gt;|SchSize=2000|PcbSize=500":
        /// writes &lt;Out&gt;\SCH\&lt;file.SchLib&gt;\&lt;SymbolReference&gt;.png and &lt;Out&gt;\PCB\&lt;file.PcbLib&gt;\&lt;footprint&gt;.png.
        /// </summary>
        [HotCommand("GenerateThumbnails", "Render PNG thumbnails of every symbol and footprint in library folders")]
        public static string GenerateThumbnails(CommandContext ctx)
        {
            string schFolder = OptionalFolder(ctx.Get("SchFolder"), "SchFolder");
            string pcbFolder = OptionalFolder(ctx.Get("PcbFolder"), "PcbFolder");
            if (schFolder == null && pcbFolder == null)
                throw new UserException("Pass SchFolder=<folder with .SchLib files> and/or PcbFolder=<folder with .PcbLib files>.");

            int schSize = ParseSize(ctx.Get("SchSize", DefaultSchSize.ToString(CultureInfo.InvariantCulture)), "SchSize");
            int pcbSize = ParseSize(ctx.Get("PcbSize", DefaultPcbSize.ToString(CultureInfo.InvariantCulture)), "PcbSize");
            string outFolder = Path.GetFullPath(ctx.Get("Out", Path.Combine(Path.GetTempPath(), "LibraryThumbnailImages")));

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            var report = new List<string>();
            int images = 0, items = 0;

            if (schFolder != null)
            {
                string[] files = Directory.GetFiles(schFolder, "*.SchLib");
                report.Add($"SCH: {files.Length} .SchLib file(s) in {schFolder}");
                foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    LibraryResult r = RunForLibrary(client, "SCHLib", file, Path.Combine(outFolder, "SCH"),
                                                    (lib, folder) => RenderSymbols(ctx, lib, folder, schSize));
                    report.AddRange(r.Lines);
                    images += r.Written;
                    items += r.Items;
                }
            }

            if (pcbFolder != null)
            {
                string[] files = Directory.GetFiles(pcbFolder, "*.PcbLib");
                report.Add($"PCB: {files.Length} .PcbLib file(s) in {pcbFolder}");
                foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    LibraryResult r = RunForLibrary(client, "PCBLib", file, Path.Combine(outFolder, "PCB"),
                                                    (lib, folder) => RenderFootprints(ctx, lib, folder, pcbSize));
                    report.AddRange(r.Lines);
                    images += r.Written;
                    items += r.Items;
                }
            }

            report.Insert(0, $"{images} of {items} thumbnails written to {outFolder}");
            return string.Join(Environment.NewLine, report);
        }

        private sealed class LibraryResult
        {
            public readonly List<string> Lines = new List<string>();
            public int Items;
            public int Written;
        }

        /// <summary>
        /// Opens one library (unless the user already has it open), renders it into &lt;kindFolder&gt;\&lt;file name&gt;\ and
        /// closes it again if this command opened it. A failure is reported for that library and the next one runs.
        /// </summary>
        private static LibraryResult RunForLibrary(IClient client, string kind, string file, string kindFolder,
                                                   Func<string, string, LibraryResult> render)
        {
            string name = Path.GetFileName(file);
            IServerDocument openedHere = null;
            try
            {
                // Closing every library afterwards would also close a library the user
                // had open (and discard what it showed). Only close what this command opened.
                if (client.GetDocumentByPath(file) == null)
                {
                    openedHere = client.OpenDocument(kind, file);
                    if (openedHere == null)
                        return Single($"  {name}: Altium Designer could not open it as {kind}.");
                }

                // The folder is named after the library file, extension included.
                string folder = Path.Combine(kindFolder, CleanFileName(name));
                Directory.CreateDirectory(folder);
                LibraryResult result = render(file, folder);
                if (openedHere == null)
                    result.Lines.Add($"    ({name} was already open: left open)");
                return result;
            }
            catch (UserException ex)
            {
                return Single($"  {name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Single($"  {name}: failed with {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (openedHere != null)
                    client.CloseDocument(openedHere);
            }
        }

        private static LibraryResult Single(string line)
        {
            var r = new LibraryResult();
            r.Lines.Add(line);
            return r;
        }

        // ---------------------------------------------------------------- schematic symbols

        private static LibraryResult RenderSymbols(CommandContext ctx, string file, string folder, int size)
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("the schematic editor is not running.");
            ISch_Lib library = FindSchLib(schServer, file);

            var result = new LibraryResult();
            var failed = new List<string>();
            var sizes = new SortedSet<string>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ISch_Iterator iterator = library.SchLibIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });
                for (ISch_BasicContainer item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                {
                    // A type test succeeds for every SDK object (interfaces-at-run-time guide); the filter admits
                    // components only, and the object ID check makes that explicit.
                    if (item.GetState_ObjectId() != SCH.TObjectId.eSchComponent)
                        continue;
                    var symbol = (ISch_Component)item;
                    result.Items++;

                    string reference = symbol.GetState_SymbolReference() ?? "";
                    ctx.Log($"{Path.GetFileName(file)} {result.Items}: {reference}");

                    // Part index 0: one image per symbol, multi-part symbols show one part only.
                    IntPtr handle = PaintLateBound(schServer, "PaintLoadedComponentThumbnail", symbol, 0, size, size);
                    if (handle == IntPtr.Zero)
                    {
                        // No bitmap for this symbol.
                        failed.Add(reference);
                        continue;
                    }

                    // Clean SymbolReference: a '/' or ':' in it would break the path.
                    string target = Path.Combine(folder, UniqueFileName(reference, usedNames) + ".png");
                    sizes.Add(SaveAsPng(handle, target));
                    result.Written++;
                }
            }
            finally
            {
                library.SchIterator_Destroy(ref iterator);
            }

            result.Lines.Insert(0, Summary(file, result, "symbols", size, sizes, folder));
            if (failed.Count > 0)
                result.Lines.Add("    PaintLoadedComponentThumbnail returned 0 for: " + string.Join(", ", failed));
            return result;
        }

        /// <summary>
        /// The ISch_Lib of an open library file. GetSchDocumentByPath leaves the focus alone; if it finds nothing, the
        /// document is shown and taken as the current schematic document, then checked.
        /// </summary>
        private static ISch_Lib FindSchLib(ISch_ServerInterface schServer, string file)
        {
            ISch_Document document = schServer.GetSchDocumentByPath(file);
            if (document == null)
            {
                IClient client = DXP.GlobalVars.Client;
                IServerDocument serverDocument = client.GetDocumentByPath(file)
                    ?? throw new UserException("it is not open in the workspace.");
                client.ShowDocument(serverDocument);
                document = schServer.GetCurrentSchDocument()
                    ?? throw new UserException("it is open but is not a schematic document.");
                string current = document.GetState_DocumentName() ?? "";
                if (!SameFile(current, file))
                    throw new UserException($"the current schematic document is '{current}', not this library.");
            }

            // `document as ISch_Lib` would succeed for a sheet too (schematic-basics guide): check the object ID.
            if (document.GetState_ObjectId() != SCH.TObjectId.eSchLib)
                throw new UserException("it is not a schematic library.");
            return (ISch_Lib)document;
        }

        // ---------------------------------------------------------------- PCB footprints

        private static LibraryResult RenderFootprints(CommandContext ctx, string file, string folder, int size)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("the PCB editor is not running.");
            // GetPCBLibraryByPath; give up when it returns nothing.
            IPCB_Library library = pcbServer.GetPCBLibraryByPath(file)
                ?? throw new UserException("GetPCBLibraryByPath found no PCB library for this file.");

            var result = new LibraryResult();
            var failed = new List<string>();
            var sizes = new SortedSet<string>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IPCB_LibraryIterator iterator = library.LibraryIterator_Create();
            try
            {
                for (IPCB_Primitive item = iterator.FirstPCBObject(); item != null; item = iterator.NextPCBObject())
                {
                    // The library iterator walks the library's footprints; each item is cast to
                    // IPCB_LibComponent without a check (a type test would prove nothing).
                    var footprint = (IPCB_LibComponent)item;
                    result.Items++;

                    // IPCB_LibComponent has no Name in the .NET API: the footprint name is Pattern.
                    string name = footprint.GetState_Pattern() ?? "";
                    ctx.Log($"{Path.GetFileName(file)} {result.Items}: {name}");

                    // PaintFootprintThumbnail loads the footprint by name from the library file; 0 when it cannot.
                    IntPtr handle = PaintLateBound(pcbServer, "PaintFootprintThumbnail", name, file, size, size);
                    if (handle == IntPtr.Zero)
                    {
                        failed.Add(name);
                        continue;
                    }

                    string target = Path.Combine(folder, UniqueFileName(name, usedNames) + ".png");
                    sizes.Add(SaveAsPng(handle, target));
                    result.Written++;
                }
            }
            finally
            {
                library.LibraryIterator_Destroy(ref iterator);
            }

            result.Lines.Insert(0, Summary(file, result, "footprints", size, sizes, folder));
            if (failed.Count > 0)
                result.Lines.Add("    PaintFootprintThumbnail returned 0 for: " + string.Join(", ", failed));
            return result;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Copies the bitmap behind the handle into a PNG file and returns its size as "WxH".</summary>
        /// <summary>
        /// Calls a Paint...Thumbnail method late-bound and returns the HBITMAP. Workaround: the typed wrappers declare
        /// a uint result, but the call returns a UInt64 holding the sign-extended handle (for example
        /// 18446744073659555991), and converting it to uint throws OverflowException inside the wrapper.
        /// </summary>
        private static IntPtr PaintLateBound(object server, string method, params object[] args)
        {
            object raw = server.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, server, args);
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

        private static string SaveAsPng(IntPtr hBitmap, string target)
        {

            // FromHbitmap copies the pixels; disposing the Bitmap right away keeps one image in memory at a time
            // (a 2000x2000 image is about 16 MB). It does not free hBitmap: the caller owns it (one
            // more GDI object in AD per image), but the .NET API has no call to free it, so it leaks (README Notes).
            // ImageFormat.Png writes real PNG data.
            using (Bitmap bitmap = Image.FromHbitmap(hBitmap))
            {
                bitmap.Save(target, ImageFormat.Png);
                return $"{bitmap.Width}x{bitmap.Height}";
            }
        }

        private static string Summary(string file, LibraryResult r, string what, int size, SortedSet<string> sizes, string folder)
        {
            string actual = sizes.Count == 0 ? "" : $" (images {string.Join(", ", sizes)})";
            return $"  {Path.GetFileName(file)}: {r.Written} of {r.Items} {what} as {size}x{size} PNG{actual} -> {folder}";
        }

        private static bool SameFile(string documentName, string file) =>
            string.Equals(documentName, file, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(documentName), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);

        private static string OptionalFolder(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string folder = Path.GetFullPath(value.Trim().Trim('"'));
            if (!Directory.Exists(folder))
                throw new UserException($"{name}: folder not found: {folder}");
            return folder;
        }

        private static int ParseSize(string value, string name)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ||
                size < 1 || size > MaxSize)
                throw new UserException($"{name} must be a whole number of pixels from 1 to {MaxSize} (got '{value}').");
            return size;
        }

        /// <summary>The text without characters Windows forbids in file names; "_" when nothing is left.</summary>
        private static string CleanFileName(string name)
        {
            string clean = name ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c.ToString(), "");
            clean = clean.Trim();
            return clean.Length == 0 ? "_" : clean;
        }

        /// <summary>The cleaned name, made unique within one library folder with a _2, _3 suffix.</summary>
        private static string UniqueFileName(string name, HashSet<string> used)
        {
            string clean = CleanFileName(name);
            string candidate = clean;
            for (int i = 2; !used.Add(candidate); i++)
                candidate = $"{clean}_{i}";
            return candidate;
        }
    }
}
