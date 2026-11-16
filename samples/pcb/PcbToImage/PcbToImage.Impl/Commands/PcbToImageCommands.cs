using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using DXP; // IClient.GetCurrentView / IServerDocumentView.GetOwnerDocument are extension methods in namespace DXP
using PCB;

namespace AdSamples.PcbToImage
{
    /// <summary>
    /// Switches the active PCB document to its 3D view with the built-in PCB:SwitchTo3D process, then asks the view's
    /// window to paint itself into a bitmap (Win32 PrintWindow) and saves it as PNG. The SDK parts: IServerDocument
    /// GetKind to check the document, IPCB_Board.ViewConfigIs3D to see the view mode, DXP.Utils.RunCommand to run the
    /// process, and IServerDocumentView.GetHandle for the window to capture.
    /// </summary>
    internal static class PcbToImageCommands
    {
        private const string DefaultFileName = "PcbToImage.png";

        /// <summary>
        /// PcbToImage "Out=&lt;file or folder&gt;|FullContent=true": switches the active PCB view to 3D (unless it
        /// already is) and saves the view window as PNG. The design is not changed; the view stays in 3D.
        /// </summary>
        [HotCommand("PcbToImage", "Switch the PCB view to 3D and save the view window as a PNG")]
        public static string PcbToImage(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // A hot command may be started from the command line without a view, so the current view is used, and
            // checked to be a PCB view.
            IServerDocumentView view = client.GetCurrentView()
                ?? throw new UserException("No document view is active. Open and focus a .PcbDoc first.");
            string kind = view.GetOwnerDocument()?.GetKind() ?? string.Empty;
            if (!string.Equals(kind, "PCB", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The active document is not a PCB document (kind '{kind}'). Focus a .PcbDoc first.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            bool was3D = board.GetState_ViewConfigIs3D();
            if (!was3D)
            {
                // A built-in process, run by its "Server:Process" id with an empty parameter string.
                // Whether the 3D view has finished rendering when RunCommand returns is not documented:
                // the result reports the state read right after the call (see README Notes).
                DXP.Utils.RunCommand("PCB:SwitchTo3D", "");
            }
            bool is3D = board.GetState_ViewConfigIs3D();

            // Re-read the current view after the switch.
            view = client.GetCurrentView()
                ?? throw new UserException("No document view is active after switching to 3D.");

            // GetHandle returns the window handle as uint; widen it to IntPtr for the Win32 calls.
            IntPtr hwnd = new IntPtr(view.GetHandle());
            if (hwnd == IntPtr.Zero)
                throw new UserException("The active document view has no window handle.");
            if (NativeMethods.IsIconic(hwnd))
                throw new UserException("The PCB view window is minimised. Restore it and run the command again.");

            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                throw new UserException($"GetWindowRect failed (Win32 error {Marshal.GetLastWin32Error()}).");
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                throw new UserException($"The view window has an empty size ({width} x {height}).");

            bool fullContent = ctx.Flag("FullContent");
            uint flags = fullContent ? NativeMethods.PW_RENDERFULLCONTENT : NativeMethods.PW_DEFAULT;
            string target = TargetPath(ctx.Get("Out"));

            // 24 bpp: no alpha channel, so pixels GDI leaves untouched cannot come out transparent in the PNG.
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                bool printed;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        printed = NativeMethods.PrintWindow(hwnd, hdc, flags);
                    }
                    finally
                    {
                        // Release the DC even when PrintWindow throws.
                        graphics.ReleaseHdc(hdc);
                    }
                }
                if (!printed)
                    throw new UserException("PrintWindow failed: the view window did not paint into the bitmap.");

                bitmap.Save(target, ImageFormat.Png);
            }

            string document = Path.GetFileName(board.GetState_FileName() ?? string.Empty);
            string mode = was3D
                ? "view was already 3D, PCB:SwitchTo3D not run"
                : $"ran PCB:SwitchTo3D, ViewConfigIs3D now {is3D}";

            return $"Saved {width} x {height} PNG of {document} to {target}" + Environment.NewLine +
                   $"3D: {mode}; PrintWindow flags {flags} ({(fullContent ? "PW_RENDERFULLCONTENT" : "default")}); " +
                   $"window rect left={rect.Left} top={rect.Top} right={rect.Right} bottom={rect.Bottom}";
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
                target += ".png";

            target = Path.GetFullPath(target);
            string folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            return target;
        }
    }
}
