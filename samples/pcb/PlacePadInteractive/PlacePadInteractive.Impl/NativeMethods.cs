using System;
using System.Runtime.InteropServices;

namespace AdSamples.PlacePadInteractive
{
    /// <summary>
    /// The Win32 calls the sample needs. The .NET API has no "follow the cursor" placement loop for an object the
    /// extension creates itself, so this sample watches Altium Designer's mouse and keyboard messages with
    /// thread-local Windows hooks. Only the constants this sample uses are declared.
    /// </summary>
    internal static class NativeMethods
    {
        internal const int WH_KEYBOARD = 2;
        internal const int WH_MOUSE = 7;

        /// <summary>nCode of a hook call that the hook procedure must process (other codes are only passed on).</summary>
        internal const int HC_ACTION = 0;

        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_RBUTTONDOWN = 0x0204;

        internal const int VK_ESCAPE = 0x1B;

        internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
