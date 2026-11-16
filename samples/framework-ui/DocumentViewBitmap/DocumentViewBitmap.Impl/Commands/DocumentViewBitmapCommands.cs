using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DXP;

namespace AdSamples.DocumentViewBitmap
{
    /// <summary>
    /// Saves what the current document view shows as a BMP file. The SDK part is small: IClient.GetCurrentView,
    /// then the view's window handle (GetHandle) and bounds (GetBounds). The pixels are copied with Win32 GDI.
    /// </summary>
    internal static class DocumentViewBitmapCommands
    {
        private const string DefaultFileName = "DocumentViewBitmap.bmp";

        /// <summary>
        /// CreateImages "Out=&lt;file or folder&gt;": copies the on-screen pixels of the current document view window
        /// into a bitmap sized from the view's bounds and saves it as BMP. Changes nothing in the design.
        /// </summary>
        [HotCommand("CreateImages", "Save the current document view as a bitmap")]
        public static string CreateImages(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            // CurrentView is null when no document is open, so check it.
            IServerDocumentView view = client.GetCurrentView()
                ?? throw new UserException("No document view is active. Open and focus a document first.");

            // GetHandle returns the view's window handle as uint; widen it to IntPtr for the Win32 calls.
            IntPtr hwnd = new IntPtr(view.GetHandle());
            if (hwnd == IntPtr.Zero)
                throw new UserException("The active document view has no window handle.");

            int left = 0, top = 0, width = 0, height = 0;
            view.GetBounds(ref left, ref top, ref width, ref height);
            if (width <= 0 || height <= 0)
                throw new UserException($"The active view has empty bounds ({left}, {top}, {width} x {height}).");

            // BitBlt copies what is on screen: a minimised, hidden or covered view gives wrong pixels (not checked).
            string target = TargetPath(ctx.Get("Out"));

            // 24 bpp: no alpha channel, so the GDI copy cannot come out transparent.
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr destination = graphics.GetHdc();
                    IntPtr source = NativeMethods.GetWindowDC(hwnd);
                    try
                    {
                        if (source == IntPtr.Zero)
                            throw new UserException("Could not get a device context for the view window.");
                        if (!NativeMethods.BitBlt(destination, 0, 0, width, height, source, 0, 0, NativeMethods.SRCCOPY))
                            throw new UserException("BitBlt failed to copy the view's pixels.");
                    }
                    finally
                    {
                        // Release both DCs even on failure.
                        if (source != IntPtr.Zero)
                            NativeMethods.ReleaseDC(hwnd, source);
                        graphics.ReleaseHdc(destination);
                    }
                }

                bitmap.Save(target, ImageFormat.Bmp);
            }

            // The window's drawable area, as GDI+ sees it, is reported next to GetBounds: the reference does not say
            // whether the bounds include the window frame, nor which coordinate space left/top are in.
            RectangleF clip;
            using (Graphics windowGraphics = Graphics.FromHwnd(hwnd))
                clip = windowGraphics.VisibleClipBounds;
            string document = Path.GetFileName(view.GetOwnerDocument()?.GetFileName() ?? string.Empty);

            return $"Saved {width} x {height} bitmap of view '{view.GetViewName()}' ({document}) to {target}" +
                   Environment.NewLine +
                   $"GetBounds: left={left} top={top} width={width} height={height}; " +
                   $"Graphics.FromHwnd visible clip bounds: {clip.Width:0} x {clip.Height:0}";
        }

        /// <summary>Out may name a file or a folder (existing, or ending with a separator); default: temp folder.</summary>
        private static string TargetPath(string outParam)
        {
            string target = string.IsNullOrWhiteSpace(outParam) ? Path.GetTempPath() : outParam.Trim();
            bool isFolder = Directory.Exists(target)
                            || target.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                            || target.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            if (isFolder)
                target = Path.Combine(target, DefaultFileName);
            else if (string.IsNullOrEmpty(Path.GetExtension(target)))
                target += ".bmp";

            target = Path.GetFullPath(target);
            string folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            return target;
        }
    }
}
