using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DXP;
using PCB;

namespace AdSamples.Pcb3DImage
{
    /// <summary>
    /// Switches the active PCB document to its 3D view with the built-in PCB:SwitchTo3D process, then saves the
    /// pixels of the current view window as a BMP. The SDK parts: IServerDocument.Kind to check the document,
    /// IPCB_Board.ViewConfigIs3D to see the view mode, DXP.Utils.RunCommand to run the process, and the view's
    /// GetHandle / GetBounds for the capture. The pixel copy itself is Win32 GDI (NativeMethods.cs).
    /// </summary>
    internal static class Pcb3DImageCommands
    {
        private const string DefaultFileName = "Pcb3DImage.bmp";

        /// <summary>
        /// CreatePcb3DImage "Out=&lt;file or folder&gt;": switches the active PCB view to 3D (unless it already is)
        /// and saves the view window's on-screen pixels as BMP. The design is not changed; the view stays in 3D.
        /// </summary>
        [HotCommand("CreatePcb3DImage", "Switch the PCB view to 3D and save it as a bitmap")]
        public static string CreatePcb3DImage(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // A missing view is an error, as is a document that is not a PCB.
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

            // Re-read the current view after the switch: do not reuse the view from before.
            view = client.GetCurrentView()
                ?? throw new UserException("No document view is active after switching to 3D.");

            // GetHandle returns the view's window handle as uint; widen it to IntPtr for the Win32 calls.
            IntPtr hwnd = new IntPtr(view.GetHandle());
            if (hwnd == IntPtr.Zero)
                throw new UserException("The active document view has no window handle.");

            int left = 0, top = 0, width = 0, height = 0;
            view.GetBounds(ref left, ref top, ref width, ref height);
            if (width <= 0 || height <= 0)
                throw new UserException($"The active view has empty bounds ({left}, {top}, {width} x {height}).");

            string target = TargetPath(ctx.Get("Out"));

            // BitBlt copies what is on screen: a minimised, hidden or covered view gives wrong pixels (the command does not check this).
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

            string document = Path.GetFileName(board.GetState_FileName() ?? string.Empty);
            string mode = was3D
                ? "view was already 3D, PCB:SwitchTo3D not run"
                : $"ran PCB:SwitchTo3D, ViewConfigIs3D now {is3D}";

            return $"Saved {width} x {height} bitmap of {document} to {target}" + Environment.NewLine +
                   $"3D: {mode}; view '{view.GetViewName()}', GetBounds left={left} top={top} width={width} height={height}";
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
