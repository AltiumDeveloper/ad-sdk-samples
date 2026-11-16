using System;
using System.Runtime.InteropServices;

namespace AdSamples.DocumentViewBitmap
{
    /// <summary>
    /// The three Win32 GDI calls the capture needs. The SDK hands out the view's window handle and bounds; copying
    /// the pixels that window shows is plain Windows, not an SDK API.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>BitBlt raster operation: copy the source rectangle directly to the destination.</summary>
        internal const int SRCCOPY = 0x00CC0020;

        /// <summary>DC for the whole window, frame included (GetDC would give the client area only).</summary>
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
                                           IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    }
}
