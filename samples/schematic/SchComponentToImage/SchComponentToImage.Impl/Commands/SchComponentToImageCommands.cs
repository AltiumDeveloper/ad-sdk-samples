using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SCH;

namespace AdSamples.SchComponentToImage
{
    /// <summary>
    /// Saves a thumbnail of every component on the current schematic sheet as a PNG file. The SDK lesson:
    /// ISch_ServerInterface.PaintLoadedComponentThumbnail draws a placed component into a GDI bitmap and returns its
    /// handle; .NET copies the handle into a Bitmap with Image.FromHbitmap.
    /// </summary>
    internal static class SchComponentToImageCommands
    {
        private const int DefaultSize = 500;
        private const int MaxSize = 4096;

        /// <summary>
        /// GenerateSchCompImages "Out=&lt;folder&gt;|Size=500|Part=0": writes &lt;Out&gt;/&lt;sheet name&gt;/&lt;SymbolReference&gt;.png
        /// for every component of the current sheet and lists the files.
        /// </summary>
        [HotCommand("GenerateSchCompImages", "Save an image of each component on the sheet")]
        public static string GenerateSchCompImages(CommandContext ctx)
        {
            int size = ParseInt(ctx.Get("Size", DefaultSize.ToString(CultureInfo.InvariantCulture)), "Size", 1, MaxSize);
            // What the part index selects for multi-part components is not documented, so it
            // is a parameter (README Notes).
            int part = ParseInt(ctx.Get("Part", "0"), "Part", 0, 1000);

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            // Null when the active document is not a schematic document (schematic-basics guide).
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("Open a .SchDoc and make it the active document.");

            string sheetName = Path.GetFileNameWithoutExtension(document.GetState_DocumentName() ?? "");
            string folder = Path.Combine(Path.GetFullPath(ctx.Get("Out", Path.GetTempPath())),
                                         CleanFileName(sheetName.Length == 0 ? "Sheet" : sheetName));
            Directory.CreateDirectory(folder);

            var lines = new List<string>();
            var failed = new List<string>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int components = 0;

            ISch_Iterator iterator = document.SchIterator_Create();
            iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });
            try
            {
                for (ISch_BasicContainer item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                {
                    // A type test succeeds for every SDK object (interfaces-at-run-time guide), so check the kind.
                    // The filter should already guarantee it.
                    if (item.GetState_ObjectId() != TObjectId.eSchComponent)
                        continue;
                    var component = (ISch_Component)item;
                    components++;

                    string symbol = component.GetState_SymbolReference() ?? "";
                    string designator = component.GetState_SchDesignator()?.GetState_Text() ?? "";
                    string label = $"{designator} ({symbol})";
                    ctx.Log($"{components}: {label}");

                    IntPtr hBitmap = PaintThumbnail(schServer, component, part, size, size);
                    if (hBitmap == IntPtr.Zero)
                    {
                        failed.Add(label);
                        continue;
                    }

                    // Files are named after SymbolReference, so components sharing a symbol would overwrite
                    // each other. Here a repeated name gets the designator appended.
                    string file = Path.Combine(folder, UniqueFileName(symbol, designator, usedNames) + ".png");

                    // FromHbitmap copies the pixels into a new Bitmap; disposing the Bitmap does not free hBitmap.
                    // The caller owns the handle: Altium Designer's GDI object count grows by exactly one per
                    // image and never comes back down when the handle is dropped, so
                    // the sample deletes it.
                    // ImageFormat.Png: the bitmap is encoded as real PNG data, not BMP data in a .png file.
                    try
                    {
                        using (Bitmap bitmap = Image.FromHbitmap(hBitmap))
                        {
                            bitmap.Save(file, ImageFormat.Png);
                            lines.Add($"  {label} -> {Path.GetFileName(file)} ({bitmap.Width}x{bitmap.Height})");
                        }
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            finally
            {
                document.SchIterator_Destroy(ref iterator);
            }

            var result = new List<string>
            {
                $"{sheetName}: {lines.Count} of {components} components written as {size}x{size} PNG (part index {part}) to {folder}",
            };
            result.AddRange(lines);
            if (failed.Count > 0)
                result.Add("PaintLoadedComponentThumbnail returned 0 for: " + string.Join(", ", failed));
            return string.Join(Environment.NewLine, result);
        }

        /// <summary>Frees a GDI bitmap. The SDK has no call for it: the handle is a plain Windows HBITMAP.</summary>
        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// The component's thumbnail as an HBITMAP, or IntPtr.Zero when the server returned no bitmap.
        /// </summary>
        /// <remarks>
        /// The SDK declares PaintLoadedComponentThumbnail as returning uint, but the server is a dispatch interface
        /// and it returns the HBITMAP as a 64-bit unsigned variant (for example
        /// 18446744071629510860, a sign-extended pointer). The declared uint cannot hold that, so the plain typed
        /// call
        ///     uint handle = schServer.PaintLoadedComponentThumbnail(component, part, size, size);
        /// never returns a value: the IDispatch forwarder throws System.OverflowException in
        /// Microsoft.Win32.OAVariantLib.ChangeType before the sample sees anything. Going through InvokeMember
        /// keeps the return value as object, so the raw variant survives and can be reinterpreted as a pointer.
        /// </remarks>
        private static IntPtr PaintThumbnail(ISch_ServerInterface server, ISch_Component component,
                                             int part, int width, int height)
        {
            object raw = server.GetType().InvokeMember(
                "PaintLoadedComponentThumbnail",
                BindingFlags.InvokeMethod,
                binder: null,
                target: server,
                args: new object[] { component, part, width, height },
                culture: CultureInfo.InvariantCulture);

            // The handle arrives as UInt64 here; the other cases keep the helper honest if a future build narrows
            // the variant. Unchecked casts: the bits are a handle, not a number.
            long handle = raw switch
            {
                null => 0L,
                ulong value => unchecked((long)value),
                long value => value,
                uint value => unchecked((int)value),
                int value => value,
                _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
            };
            return new IntPtr(handle);
        }

        private static int ParseInt(string value, string name, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ||
                number < min || number > max)
                throw new UserException($"{name} must be a whole number from {min} to {max} (got '{value}').");
            return number;
        }

        /// <summary>The text without characters Windows forbids in file names; "_" when nothing is left.</summary>
        private static string CleanFileName(string name)
        {
            string clean = name ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c, '_');
            clean = clean.Trim();
            return clean.Length == 0 ? "_" : clean;
        }

        /// <summary>
        /// SymbolReference as the file name; a name already written in this run gets
        /// "_&lt;designator&gt;", then "_2", "_3" if that is taken too.
        /// </summary>
        private static string UniqueFileName(string symbol, string designator, HashSet<string> used)
        {
            string baseName = CleanFileName(symbol);
            if (used.Add(baseName))
                return baseName;

            string withDesignator = designator.Length == 0 ? baseName : baseName + "_" + CleanFileName(designator);
            string candidate = withDesignator;
            for (int i = 2; !used.Add(candidate); i++)
                candidate = $"{withDesignator}_{i}";
            return candidate;
        }
    }
}
