using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). The template's OnCreated hook, called once on AD's UI thread when AD
    /// creates the server module, publishes the idle watcher's switches for the implementation.
    /// </summary>
    public partial class PluginServerModule
    {
        // Template hook OnCreated: called once, when Altium Designer creates the server module.
        // Publishes the idle watcher's switches for the implementation.
        partial void OnCreated(IClient client) => IdleWatcher.Publish();
    }

    /// <summary>
    /// Tracks keyboard and mouse input on AD's UI thread and, once the user has been idle for the threshold, asks the
    /// implementation's "AutoRelease.Idle" command to return the leased licences.
    ///
    /// Why the shim: the input hooks are native callbacks into managed code. In Debug builds the implementation is
    /// loaded into an unloadable context and swapped on every rebuild; a hook procedure living there would be left
    /// pointing at unloaded code and take AD down on the next key press. The shim is loaded once and stays, so the
    /// hooks, their delegates and the timer live here. The SDK call itself stays in the implementation.
    ///
    /// The implementation cannot reference the shim, so the switches are published as BCL delegates in AppDomain
    /// data (keys carry the sample name, because AppDomain data is shared by every extension in AD).
    /// </summary>
    internal static class IdleWatcher
    {
        /// <summary>Func&lt;int, bool, string&gt;: start(idleSeconds, dryRun); returns what it did.</summary>
        public const string StartKey = SampleInfo.Name + ".Start";

        /// <summary>Func&lt;string&gt;: stop; returns what it did.</summary>
        public const string StopKey = SampleInfo.Name + ".Stop";

        /// <summary>Func&lt;IReadOnlyDictionary&lt;string, object&gt;&gt;: the watcher's state, for the status command.</summary>
        public const string StatusKey = SampleInfo.Name + ".Status";

        /// <summary>The implementation command the timer runs when the idle threshold is crossed.</summary>
        private const string IdleCommand = "AutoRelease.Idle";

        // Native thread ID of AD's UI thread, recorded when AD created the server module. The hooks are thread hooks
        // on exactly this thread, and the WinForms timer must be created on it to tick there.
        private static uint _uiThreadId;

        // The delegates are kept in static fields: the native hook holds only a function pointer, so a delegate the
        // garbage collector could reclaim would crash AD on the next input event.
        private static NativeMethods.HookProc _mouseProc;
        private static NativeMethods.HookProc _keyboardProc;
        private static IntPtr _mouseHook;
        private static IntPtr _keyboardHook;
        private static System.Windows.Forms.Timer _timer;

        private static int _idleSeconds;
        private static bool _dryRun;
        private static long _lastInputTick;       // Environment.TickCount64 of the last input (64-bit: no 49.7-day wrap)
        private static long _releasedForInput = -1; // _lastInputTick of the idle period already handled
        private static long _inputEvents;
        private static int _idleTriggers;
        private static DateTime? _startedAt;
        private static DateTime? _lastTriggerAt;
        private static string _lastResult;
        private static string _lastError;
        private static bool _inTick;

        internal static void Publish()
        {
            _uiThreadId = NativeMethods.GetCurrentThreadId();
            AppDomain.CurrentDomain.SetData(StartKey, (Func<int, bool, string>)Start);
            AppDomain.CurrentDomain.SetData(StopKey, (Func<string>)Stop);
            AppDomain.CurrentDomain.SetData(StatusKey, (Func<IReadOnlyDictionary<string, object>>)Status);
        }

        private static string Start(int idleSeconds, bool dryRun)
        {
            if (NativeMethods.GetCurrentThreadId() != _uiThreadId)
                throw new InvalidOperationException("StartAutoRelease must run on AD's UI thread.");

            if (_timer != null)
            {
                // Already running: only the settings change, the hooks stay.
                _idleSeconds = idleSeconds;
                _dryRun = dryRun;
                return "already running; settings updated";
            }

            _mouseProc = OnInput;
            _keyboardProc = OnInput;
            // Thread hooks (dwThreadId = AD's UI thread, hMod = 0): only input that AD's own windows receive counts.
            // Mouse and keyboard input to other applications does not reset the idle time.
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE, _mouseProc, IntPtr.Zero, _uiThreadId);
            if (_mouseHook == IntPtr.Zero)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                _mouseProc = _keyboardProc = null;
                throw new InvalidOperationException("SetWindowsHookEx(WH_MOUSE) failed: " + error.Message);
            }

            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD, _keyboardProc, IntPtr.Zero, _uiThreadId);
            if (_keyboardHook == IntPtr.Zero)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                RemoveHooks();
                throw new InvalidOperationException("SetWindowsHookEx(WH_KEYBOARD) failed: " + error.Message);
            }

            _idleSeconds = idleSeconds;
            _dryRun = dryRun;
            // The idle clock starts now: starting the watcher is itself a user action.
            _lastInputTick = Environment.TickCount64;
            _releasedForInput = -1;
            _inputEvents = 0;
            _idleTriggers = 0;
            _lastTriggerAt = null;
            _lastResult = null;
            _lastError = null;
            _startedAt = DateTime.Now;

            // A UI-thread timer, not a background thread: the SDK may only be called on AD's main
            // thread (docs: runtime-and-hosting, "The main thread"), and WM_TIMER is dispatched by AD's message loop.
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += OnTick;
            _timer.Start();
            return "started";
        }

        private static string Stop()
        {
            if (_timer == null)
                return "not running";
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Dispose();
            _timer = null;
            RemoveHooks();
            _startedAt = null;
            return "stopped";
        }

        private static IReadOnlyDictionary<string, object> Status() => new Dictionary<string, object>
        {
            ["Running"] = _timer != null,
            ["StartedAt"] = _startedAt,
            ["IdleSeconds"] = _idleSeconds,
            ["DryRun"] = _dryRun,
            ["SecondsSinceInput"] = _timer != null ? (Environment.TickCount64 - _lastInputTick) / 1000.0 : null,
            ["InputEvents"] = _inputEvents,
            ["IdleTriggers"] = _idleTriggers,
            ["LastTriggerAt"] = _lastTriggerAt,
            ["LastResult"] = _lastResult,
            ["LastError"] = _lastError,
        };

        private static void RemoveHooks()
        {
            if (_mouseHook != IntPtr.Zero)
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
            if (_keyboardHook != IntPtr.Zero)
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _mouseHook = _keyboardHook = IntPtr.Zero;
            _mouseProc = _keyboardProc = null;
        }

        /// <summary>Hook procedure for WH_MOUSE and WH_KEYBOARD. Must stay minimal and must never throw.</summary>
        private static IntPtr OnInput(int code, IntPtr wParam, IntPtr lParam)
        {
            // code < 0: the hook must pass the message on without processing it.
            if (code >= 0)
            {
                _lastInputTick = Environment.TickCount64;
                _inputEvents++;
            }
            return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static void OnTick(object sender, EventArgs e)
        {
            // A modal dialog inside the command would pump messages and tick again: never run twice at once.
            if (_inTick)
                return;

            long idleMs = Environment.TickCount64 - _lastInputTick;
            // Once per idle period, not every second while idle.
            if (idleMs < _idleSeconds * 1000L || _releasedForInput == _lastInputTick)
                return;

            _releasedForInput = _lastInputTick;
            _inTick = true;
            try
            {
                _idleTriggers++;
                _lastTriggerAt = DateTime.Now;
                _lastResult = Commands.Invoke(IdleCommand, $"DryRun={_dryRun}|IdleSeconds={idleMs / 1000}", null);
            }
            catch (Exception ex)
            {
                // Never let an exception escape into AD's message loop: keep it for the status command.
                _lastError = DateTime.Now.ToString("HH:mm:ss") + " " + ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                _inTick = false;
            }
        }

        private static class NativeMethods
        {
            internal const int WH_KEYBOARD = 2;
            internal const int WH_MOUSE = 7;

            internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll")]
            internal static extern uint GetCurrentThreadId();
        }
    }
}
