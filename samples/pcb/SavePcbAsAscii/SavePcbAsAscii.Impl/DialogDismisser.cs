using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AdSamples.SavePcbAsAscii
{
    /// <summary>
    /// A background thread that, while it runs, looks every 100 ms for a new visible popup window of this process with
    /// the given title (by default "Information") and clicks its button or, if it has no clickable button window
    /// (as with AD's message box), closes it with WM_CLOSE, so a save that shows a message box does not
    /// wait for the user. This is Win32 window automation, not an SDK feature, and the title match depends on the AD
    /// user interface language.
    ///
    /// Bounded by design: Dispose stops the thread and waits for it (at most 2 s); popups that were already open when
    /// the dismisser started are left alone, and each window is clicked once only.
    /// </summary>
    internal sealed class DialogDismisser : IDisposable
    {
        private readonly string _title;
        private readonly uint _processId = (uint)Environment.ProcessId;
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private readonly HashSet<IntPtr> _ignore = new HashSet<IntPtr>();
        private readonly List<string> _dismissed = new List<string>();

        // Dialogs whose button was clicked, and when: one still open after ClickGrace is closed with WM_CLOSE.
        private readonly Dictionary<IntPtr, DateTime> _clicked = new Dictionary<IntPtr, DateTime>();
        private static readonly TimeSpan ClickGrace = TimeSpan.FromSeconds(1);
        private readonly Thread _thread;

        // Kept in a field so the garbage collector cannot free the delegate while EnumWindows calls it.
        private readonly NativeMethods.EnumWindowsProc _onWindow;

        public DialogDismisser(string title)
        {
            _title = title;
            _onWindow = OnWindow;

            // Popups with that title that are already open are not ours to click.
            foreach (IntPtr hWnd in FindPopups())
                _ignore.Add(hWnd);

            _thread = new Thread(Run) { IsBackground = true, Name = "SavePcbAsAscii dialog dismisser" };
            _thread.Start();
        }

        /// <summary>One line per dismissed window: its title, the texts of its child windows and what was done.</summary>
        public IReadOnlyList<string> Dismissed
        {
            get { lock (_dismissed) return _dismissed.ToList(); }
        }

        /// <summary>False if the thread did not stop within the timeout (it is a background thread and ends later).</summary>
        public bool Stopped { get; private set; }

        public void Dispose()
        {
            _stop.Set();
            Stopped = _thread.Join(TimeSpan.FromSeconds(2));
            if (Stopped)
                _stop.Dispose();
        }

        private void Run()
        {
            try
            {
                // Wait returns true when Dispose asks the thread to stop, false after each 100 ms poll interval.
                while (!_stop.Wait(100))
                {
                    foreach (IntPtr hWnd in FindPopups())
                    {
                        if (_stop.IsSet)
                            return;
                        if (_ignore.Add(hWnd))
                            Dismiss(hWnd);
                        else if (_clicked.TryGetValue(hWnd, out DateTime when) && DateTime.UtcNow - when > ClickGrace)
                        {
                            // Still there a second after the click: the click had no effect. Close it instead, once.
                            _clicked.Remove(hWnd);
                            NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            lock (_dismissed) _dismissed.Add($"'{_title}' dialog still open after the click: sent WM_CLOSE");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never let an exception escape a thread: in .NET that ends the whole process, here Altium Designer.
                lock (_dismissed) _dismissed.Add("dismisser stopped: " + ex.Message);
            }
        }

        private List<IntPtr> _found;

        private List<IntPtr> FindPopups()
        {
            _found = new List<IntPtr>();
            NativeMethods.EnumWindows(_onWindow, IntPtr.Zero);
            return _found;
        }

        private bool OnWindow(IntPtr hWnd, IntPtr lParam)
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true;
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != _processId)
                return true;
            // A message box: a popup window, or one of the message-box window classes (AD's is a VCL TMessageForm;
            // the WS_POPUP test alone may not match it).
            long style = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE).ToInt64();
            string cls = NativeMethods.ClassName(hWnd);
            if ((style & NativeMethods.WS_POPUP) == 0 && cls != "TMessageForm" && cls != "#32770")
                return true;
            if (string.Equals(NativeMethods.WindowText(hWnd), _title, StringComparison.OrdinalIgnoreCase))
                _found.Add(hWnd);
            return true;
        }

        private void Dismiss(IntPtr dialog)
        {
            var children = new List<IntPtr>();
            NativeMethods.EnumChildWindows(dialog, (h, _) => { children.Add(h); return true; }, IntPtr.Zero);

            string texts = string.Join(" / ", children.Select(NativeMethods.WindowText).Where(t => t.Length > 0));

            string classes = string.Join(", ", children.Select(NativeMethods.ClassName).Distinct());

            // Click a child only if its window class is a Win32 "Button" or VCL "TButton". AD's message box
            // (TMessageForm) has TXPExtPanel panes as its first children, so clicking the first child window
            // (GetWindow(GW_CHILD)) would hit a pane, and its OK button is a TXPBitBtn that ignores a posted
            // BM_CLICK (the dialog is still open a second later). So TXPBitBtn is deliberately not treated as
            // clickable; the dialog is closed with WM_CLOSE, which for a message box with a single OK button is the
            // same answer (the save then returns).
            IntPtr button = children.FirstOrDefault(h => NativeMethods.ClassName(h).IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0);

            // PostMessage, not SendMessage: the worker never waits for AD's UI thread, so stopping
            // the dismisser cannot block on a click that the UI thread has not processed yet.
            string action;
            if (button != IntPtr.Zero)
            {
                NativeMethods.PostMessage(button, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                _clicked[dialog] = DateTime.UtcNow; // closed with WM_CLOSE if it is still open after ClickGrace
                action = $"clicked '{NativeMethods.WindowText(button)}' ({NativeMethods.ClassName(button)})";
            }
            else
            {
                NativeMethods.PostMessage(dialog, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                action = "no clickable button window, sent WM_CLOSE";
            }

            lock (_dismissed)
                _dismissed.Add($"'{_title}' dialog ({NativeMethods.ClassName(dialog)}; children: {classes}) [{texts}]: {action}");
        }
    }
}
