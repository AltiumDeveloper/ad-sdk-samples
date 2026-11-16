using System;
using System.Runtime.InteropServices;

namespace AdSamples.PcbToImage
{
    /// <summary>
    /// The Win32 calls the capture needs. The SDK hands out the view's window handle (IServerDocumentView.GetHandle);
    /// asking that window to paint itself into a bitmap is plain Windows (user32), not an SDK API.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>PrintWindow flag: draw the whole window, frame included.</summary>
        internal const uint PW_DEFAULT = 0;

        /// <summary>
        /// PrintWindow flag (Windows 8.1 and later): also capture content rendered with DirectX / DirectComposition.
        /// Exposed as FullContent=true for the case where the 3D view comes out black with the default flag.
        /// </summary>
        internal const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>True when the window is minimised: a minimised window has nothing to print.</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);
    }
}
