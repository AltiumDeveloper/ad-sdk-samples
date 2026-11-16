using System;
using EDP;
using PCB;

namespace AdSamples.PlacePadInteractive
{
    /// <summary>
    /// One interactive pad placement: a new pad is added to the board, follows the cursor while the mouse moves, and
    /// is kept on a left-click or removed on a right-click or Escape.
    /// </summary>
    /// <remarks>
    /// The command that starts a placement returns at once; the placement ends later, inside a Windows hook call on
    /// Altium Designer's main thread. So this object (with the board and the pad) has to outlive the command, which is
    /// the one exception to "re-query the workspace in each command": it lives only until the placement ends, and
    /// <see cref="Active"/> is cleared then. <see cref="SampleHooks"/> ends it when the implementation is unloaded,
    /// because a hook left installed would call into an unloaded assembly and crash Altium Designer.
    /// </remarks>
    internal sealed class PadPlacement
    {
        /// <summary>The placement in progress, or null. At most one at a time.</summary>
        internal static PadPlacement Active { get; private set; }

        /// <summary>How the last placement ended (plain text, no COM object), for PlacePadStatus.</summary>
        internal static string LastOutcome { get; private set; } = "none in this session";

        private readonly IPCB_Board _board;
        private readonly IPCB_Pad _pad;

        // The hook procedures are passed to native code as function pointers: keep the delegates referenced for as
        // long as the hooks are installed, or the garbage collector frees them and the next message crashes.
        private readonly NativeMethods.HookProc _mouseProc;
        private readonly NativeMethods.HookProc _keyProc;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyHook = IntPtr.Zero;

        private PadPlacement(IPCB_Board board, IPCB_Pad pad)
        {
            _board = board;
            _pad = pad;
            _mouseProc = MouseHookProc;
            _keyProc = KeyHookProc;
        }

        internal IPCB_Pad Pad => _pad;

        /// <summary>Creates the pad, adds it to the board at the cursor and installs the hooks.</summary>
        internal static PadPlacement Start(IPCB_ServerInterface pcbServer, IPCB_Board board)
        {
            if (Active != null)
                throw new InvalidOperationException("A placement is already in progress.");

            IPCB_Pad pad;
            // A short PreProcess/PostProcess bracket around the creation only, not one held open until the click,
            // across all the messages Altium Designer processes in between.
            pcbServer.PreProcess();
            try
            {
                DXP.Utils.RunCommand("PCB:DeSelect", "Scope=All");

                pad = pcbServer.PCBObjectFactory(PCB.TObjectId.ePadObject, TDimensionKind.eNoDimension,
                    TObjectCreationMode.eCreate_Default) as IPCB_Pad
                    ?? throw new UserException("PCBObjectFactory did not create a pad.");
                pad.SetState_Layer(TV6_Layer.eV6_BottomLayer);
                pad.SetState_XLocation(board.GetState_XCursor());
                pad.SetState_YLocation(board.GetState_YCursor());
                pad.SetState_IsTestPoint_Bottom(true);

                // AddPCBObject registers the new pad with the board, including its undo history.
                board.AddPCBObject(pad);

                // Dim everything except the pad being placed, as visual feedback.
                board.AddObjectToHighlightObjectList(pad);
                board.SetState_Navigate_HighlightObjectList(new THighlightMethodSet { THighlightMethod.eHighlight_Dim }, true);

                // Select only after navigating: Navigate_HighlightObjectList (clear existing = true) clears the
                // selection, so a pad selected before it ends up deselected.
                pad.SetState_Selected(true);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            var placement = new PadPlacement(board, pad);
            Active = placement;
            try
            {
                placement.InstallHooks();
            }
            catch
            {
                placement.Finish(keep: false, "cancelled: the Windows hooks could not be installed");
                throw;
            }
            return placement;
        }

        /// <summary>Ends the placement: removes the hooks, then keeps or removes the pad. Safe to call twice.</summary>
        internal string Finish(bool keep, string reason)
        {
            // Unhook first, whatever happens next: a hook that outlives the placement would keep firing.
            RemoveHooks();
            if (!ReferenceEquals(Active, this))
                return LastOutcome;
            Active = null;

            string outcome;
            try
            {
                IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
                pcbServer.PreProcess();
                try
                {
                    if (keep)
                    {
                        // The moves while dragging were not bracketed (they are a preview). The final position is,
                        // so the board updates the pad's connectivity where it was dropped.
                        _pad.BeginModify();
                        _pad.SetState_XLocation(_board.GetState_XCursor());
                        _pad.SetState_YLocation(_board.GetState_YCursor());
                        _pad.EndModify();
                        outcome = $"placed at {FormatLocation(_board, _pad)}";
                    }
                    else
                    {
                        _board.RemovePCBObject(_pad);
                        outcome = "cancelled, pad removed";
                    }

                    // Redraw the highlight list with the normal (graphical) method and clear the dim.
                    _board.SetState_Navigate_HighlightObjectList(new THighlightMethodSet { THighlightMethod.eHighlight_Graph }, true);
                }
                finally
                {
                    pcbServer.PostProcess();
                }
            }
            catch (Exception ex)
            {
                outcome = $"{(keep ? "placing" : "removing")} the pad failed: {ex.Message}";
            }

            LastOutcome = $"{outcome} ({reason})";
            return LastOutcome;
        }

        internal static string FormatLocation(IPCB_Board board, IPCB_Pad pad) =>
            $"({EDP.Utils.CoordToMils(pad.GetState_XLocation() - board.GetState_XOrigin()):0.##} mil, " +
            $"{EDP.Utils.CoordToMils(pad.GetState_YLocation() - board.GetState_YOrigin()):0.##} mil) from the board origin";

        private void InstallHooks()
        {
            // Thread-local hooks on the thread that owns Altium Designer's main window (its UI thread). With
            // hMod = IntPtr.Zero the thread must belong to this process, which it does.
            var mainWindow = new IntPtr(DXP.GlobalVars.Client.GetMainWindowHandle());
            uint threadId = NativeMethods.GetWindowThreadProcessId(mainWindow, out _);
            if (threadId == 0)
                throw new UserException("Could not find Altium Designer's main window thread.");

            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE, _mouseProc, IntPtr.Zero, threadId);
            _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD, _keyProc, IntPtr.Zero, threadId);
            if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
                throw new UserException($"SetWindowsHookEx failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
        }

        private void RemoveHooks()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyHook);
                _keyHook = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            IntPtr hook = _mouseHook; // read before Finish clears it
            if (nCode == NativeMethods.HC_ACTION && ReferenceEquals(Active, this))
            {
                try
                {
                    switch (wParam.ToInt32())
                    {
                        case NativeMethods.WM_MOUSEMOVE:
                            // The SDK lesson: the board's cursor position, in board coordinates, is what Altium
                            // Designer itself uses (snapped to the grid), so the pad follows the crosshair.
                            _pad.SetState_XLocation(_board.GetState_XCursor());
                            _pad.SetState_YLocation(_board.GetState_YCursor());
                            _pad.GraphicallyInvalidate();
                            break;
                        case NativeMethods.WM_LBUTTONDOWN:
                            Finish(keep: true, "left-click");
                            break;
                        case NativeMethods.WM_RBUTTONDOWN:
                            Finish(keep: false, "right-click");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Never let an exception escape into native code; end the placement instead.
                    Finish(keep: false, $"error while placing: {ex.Message}");
                }
            }
            // Pass every message on: the click also reaches Altium Designer.
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private IntPtr KeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            IntPtr hook = _keyHook;
            // Bit 31 of lParam is the transition state: 0 = key pressed, 1 = key released. Only the press is
            // handled; reacting to both would see one Escape press twice.
            bool keyDown = (lParam.ToInt64() & 0x80000000L) == 0;
            if (nCode == NativeMethods.HC_ACTION && keyDown && wParam.ToInt32() == NativeMethods.VK_ESCAPE
                && ReferenceEquals(Active, this))
            {
                try
                {
                    Finish(keep: false, "Escape");
                }
                catch
                {
                    // Finish catches SDK errors itself; nothing may escape into native code.
                }
            }
            return NativeMethods.CallNextHookEx(hook, nCode, wParam, lParam);
        }
    }
}
