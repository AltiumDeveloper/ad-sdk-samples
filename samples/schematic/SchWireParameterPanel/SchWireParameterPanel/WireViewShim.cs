using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). The automatic mode does two things that AD drives:
    /// 1. on DocumentActivating of a schematic document, it adds a panel view (the holder view) to that document
    ///    with IServerDocument.AddView;
    /// 2. that view receives the view notifications and, on the EndUpdate code, unhides and selects every wire
    ///    parameter of the sheet.
    ///
    /// In .NET, DXP.ServerModule.ReceiveNotification is not virtual (overriding it gives
    /// CS0506), so (1) needs a DXP.NotificationHandler. Nothing here runs on its own: the handler is created only by
    /// the implementation's "AutoShow State=on" command, and "AutoShow State=off" unregisters it and stops the
    /// per-document views from acting. The template's OnCreated hook publishes those switches as delegates in
    /// AppDomain data (BCL types only: they cross the hot reload boundary).
    /// </summary>
    public partial class PluginServerModule
    {
        // Template hook OnCreated: called once, when Altium Designer creates the server module.
        // Publishes the switches the implementation and the panel content call.
        partial void OnCreated(IClient client)
        {
            AppDomain.CurrentDomain.SetData(WireViewShim.CountsKey, new ConcurrentDictionary<string, int>());
            AppDomain.CurrentDomain.SetData(WireViewShim.LogKey, new ConcurrentQueue<string>());
            AppDomain.CurrentDomain.SetData(WireViewShim.AutoShowKey, (Func<bool, string>)WireViewShim.SetAutoShow);
            AppDomain.CurrentDomain.SetData(WireViewShim.AttachedKey, (Func<string[]>)WireViewShim.AttachedDocuments);
        }
    }

    /// <summary>The shim's state and switches. All AppDomain keys carry the sample name (AppDomain data is shared by all extensions).</summary>
    internal static class WireViewShim
    {
        // The implementation repeats these strings: it cannot reference the shim assembly.
        public const string AutoShowKey = SampleInfo.Name + ".AutoShow";              // Func<bool, string>
        public const string AttachedKey = SampleInfo.Name + ".AttachedDocuments";     // Func<string[]>
        public const string EnabledSinceKey = SampleInfo.Name + ".EnabledSince";      // DateTime or null
        public const string CountsKey = SampleInfo.Name + ".NotificationCounts";      // ConcurrentDictionary<string, int>
        public const string LogKey = SampleInfo.Name + ".NotificationLog";            // ConcurrentQueue<string>, newest last
        public const string LastErrorKey = SampleInfo.Name + ".NotificationError";    // string

        /// <summary>
        /// The view name of the per-document view, declared by the second PanelInfo
        /// block of Installation\SchWireParameterPanel.ins. Prefixed with the sample name: panel names are global in AD.
        /// </summary>
        public const string HolderViewName = SampleInfo.Name + "Holder";

        /// <summary>
        /// An older EndUpdate view code (cViewNotification_EndUpdate = 6). It is not a DXP.NotificationCode field (in
        /// DXP.NotificationCode.ViewNotificationCode, 6 is ApplicationStartupComplete), and AD does not send it to
        /// the view. Kept as a trigger for older builds.
        /// </summary>
        public const int OriginalEndUpdateCode = 6;

        /// <summary>
        /// The view code AD sends to a per-document view when an object on the sheet is redrawn
        /// (once per ISch_GraphicalObject.GraphicallyInvalidate call). Not a DXP.NotificationCode field either: in
        /// ViewNotificationCode, 7 is ApplicationShutdownStarted, which is not what these mean.
        /// </summary>
        public const int EndUpdateCode = 7;

        /// <summary>
        /// The run's own changes raise code 7 again, synchronously, and the in-process flag drops those. As a second
        /// guard, notifications that arrive within this time after a run ends are ignored.
        /// </summary>
        private static readonly TimeSpan QuietAfterRun = TimeSpan.FromMilliseconds(750);
        private static DateTime _lastRunEnded = DateTime.MinValue;

        private const int LogCapacity = 40;

        private static NotificationHandler _handler;
        private static bool _enabled;

        // Re-entry guard: the EndUpdate work changes the sheet, which may raise further view
        // notifications while it runs.
        private static bool _inProcess;

        // AD gets the views through COM; this map (file path -> view) also keeps the .NET objects (and their forms)
        // reachable. The .NET API has no IServerDocument.RemoveView, so a view stays until its document closes (it is
        // inert while off). Entries of documents that are no longer open are pruned (PruneClosed), so a sheet that is
        // closed and reopened gets a new view and AutoShow State=status lists only open documents.
        private static readonly Dictionary<string, SchHolderView> Attached =
            new Dictionary<string, SchHolderView>(StringComparer.OrdinalIgnoreCase);

        public static bool Enabled => _enabled;

        /// <summary>
        /// on = true: registers the handler (if needed) and attaches the view to the focused schematic, which was
        /// activated before the handler existed. on = false: unregisters the handler and makes the views inert.
        /// Returns what it did, for the command's text. The client is re-queried each time, not kept.
        /// </summary>
        public static string SetAutoShow(bool on)
        {
            if (on)
            {
                string registered = "handler already registered";
                if (_handler == null)
                {
                    // DXP.NotificationHandler calls the delegate for every notification. Its constructor registers it with
                    // the client, so it is not registered again; UnregisterNotificationHandler removes it.
                    _handler = new NotificationHandler(OnClientNotification);
                    registered = "handler registered";
                }
                _enabled = true;
                AppDomain.CurrentDomain.SetData(EnabledSinceKey, DateTime.Now);
                return registered + "; " + AttachView();
            }

            _enabled = false;
            AppDomain.CurrentDomain.SetData(EnabledSinceKey, null);
            if (_handler == null)
                return "handler not registered";
            GlobalVars.Client.UnregisterNotificationHandler(_handler);
            _handler = null;
            return "handler unregistered; attached views stay until their documents close, but do nothing";
        }

        /// <summary>
        /// The DocumentActivating branch: add the holder view to the current view's document, if it is a
        /// schematic document ('SCH') that does not have it yet.
        /// </summary>
        public static string AttachView()
        {
            PruneClosed();

            IServerDocumentView current = GlobalVars.Client.GetCurrentView();
            IServerDocument document = current?.GetOwnerDocument();
            if (document == null)
                return "no document view focused, nothing attached";

            string kind = document.GetKind();
            string file = document.GetFileName();
            if (!string.Equals(kind, "SCH", StringComparison.OrdinalIgnoreCase))
                return $"not attached: {file} is of kind '{kind}', not 'SCH'";

            // The shim asks the document (GetViewByName) first. The .NET form returns
            // IServerDocumentView, which a panel view may not be; it does not find the holder view
            // (returns null). The shim's map is the fallback (if the call throws or finds nothing), which is safe because closed
            // documents are pruned from it above and on every document notification the handler receives. Limitation:
            // a sheet closed and reopened while AutoShow is off, with no AutoShow status/on run in between, is still in
            // the map and is not attached again.
            try
            {
                if (document.GetViewByName(HolderViewName) != null)
                    return "view already attached (GetViewByName) to " + file;
            }
            catch (Exception ex)
            {
                RecordError("GetViewByName", ex);
            }
            if (Attached.ContainsKey(file))
                return "view already attached (shim list) to " + file;

            var view = new SchHolderView(new SchHolderForm());
            document.AddView((object)view); // the object overload: a panel view is not an IServerDocumentView
            Attached[file] = view; // replaces the entry of an earlier, closed instance of the same file
            return "view attached to " + file;
        }

        /// <summary>The files that have a holder view, after dropping the documents that are no longer open.</summary>
        public static string[] AttachedDocuments()
        {
            PruneClosed();
            var files = new string[Attached.Count];
            Attached.Keys.CopyTo(files, 0);
            return files;
        }

        /// <summary>
        /// Drops the entries whose document is no longer open (IClient.IsDocumentOpen): their view closed with the
        /// document. If the check throws, the entry is kept and the error recorded for AutoShow State=status.
        /// </summary>
        private static void PruneClosed()
        {
            if (Attached.Count == 0)
                return;
            IClient client = GlobalVars.Client;
            var closed = new List<string>();
            foreach (string file in Attached.Keys)
            {
                try
                {
                    if (!client.IsDocumentOpen(file))
                        closed.Add(file);
                }
                catch (Exception ex)
                {
                    RecordError("IsDocumentOpen", ex);
                }
            }
            foreach (string file in closed)
                Attached.Remove(file);
        }

        /// <summary>Counts a notification under "source:type/code" and appends it to the short log.</summary>
        public static void Record(string source, string type, int code)
        {
            string key = $"{source}:{type}/{code}";
            if (AppDomain.CurrentDomain.GetData(CountsKey) is ConcurrentDictionary<string, int> counts)
                counts.AddOrUpdate(key, 1, (_, n) => n + 1);
            if (AppDomain.CurrentDomain.GetData(LogKey) is ConcurrentQueue<string> log)
            {
                log.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {key}");
                while (log.Count > LogCapacity && log.TryDequeue(out _)) { }
            }
        }

        public static void RecordError(string where, Exception ex) =>
            AppDomain.CurrentDomain.SetData(LastErrorKey, $"{DateTime.Now:HH:mm:ss} {where}: {ex.GetType().Name}: {ex.Message}");

        /// <summary>
        /// The view's EndUpdate work: the implementation's ShowWireParameters (Select=true), guarded
        /// against re-entry. Returns its text, or null when it did not run.
        /// </summary>
        public static string RunEndUpdate()
        {
            if (!_enabled || _inProcess || DateTime.Now - _lastRunEnded < QuietAfterRun)
                return null;
            _inProcess = true;
            try
            {
                return Commands.Invoke("ShowWireParameters", "Select=true|Source=EndUpdate", null);
            }
            finally
            {
                _inProcess = false;
                _lastRunEnded = DateTime.Now;
            }
        }

        /// <summary>The registered handler's callback: every notification AD broadcasts while AutoShow is on.</summary>
        private static void OnClientNotification(INotification notification)
        {
            try
            {
                if (notification == null)
                    return;
                string type = notification.GetNotificationType() ?? "";
                int code = notification.GetNotificationCode();
                Record("handler", type, code);

                // Forget the views of documents closed since the last notification (their views closed with them),
                // so that a sheet reopened later is attached again.
                if (string.Equals(type, NotificationType.DocumentNotification, StringComparison.OrdinalIgnoreCase))
                    PruneClosed();

                // A document notification with the DocumentActivating code.
                if (_enabled
                    && string.Equals(type, NotificationType.DocumentNotification, StringComparison.OrdinalIgnoreCase)
                    && code == NotificationCode.DocumentNotificationCode.DocumentActivating)
                    AttachView();
            }
            catch (Exception ex)
            {
                // Never let an exception (or a dialog) escape into AD's notification loop: keep it for AutoShow State=status.
                RecordError("handler", ex);
            }
        }
    }

    /// <summary>The per-document view's form: one read-only text box that fills the form, with a vertical scroll bar.</summary>
    internal sealed class SchHolderForm : ServerPanelForm
    {
        public SchHolderForm()
        {
            Text = SampleInfo.Name;
            BackColor = DxpThemeManager.ColorPanelBody;
            Log = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };
            Controls.Add(Log);
        }

        public TextBox Log { get; }
    }

    /// <summary>
    /// The per-document view. It counts every notification, and on the EndUpdate code (7) runs the
    /// implementation's ShowWireParameters (through the shim, so it hot-reloads) and appends its text to the form.
    /// </summary>
    internal sealed class SchHolderView : ServerPanelView
    {
        private readonly SchHolderForm _form;

        public SchHolderView(SchHolderForm form) : base(form, WireViewShim.HolderViewName, SampleInfo.Name)
        {
            _form = form;
        }

        // BaseServerView.ReceiveNotification (the member AD calls) is not overridable in .NET; the SDK declares the
        // protected virtual ReceiveNotificationImpl(INotification) beside it. AD's call reaches it
        // (AutoShow State=status counts the "view:" notifications).
        protected override void ReceiveNotificationImpl(INotification argNotification)
        {
            try
            {
                if (argNotification == null || !WireViewShim.Enabled)
                    return;
                string type = argNotification.GetNotificationType() ?? "";
                int code = argNotification.GetNotificationCode();
                WireViewShim.Record("view", type, code);

                // A view notification with the EndUpdate code, which is 7 (see EndUpdateCode).
                if ((code != WireViewShim.EndUpdateCode && code != WireViewShim.OriginalEndUpdateCode)
                    || !string.Equals(type, NotificationType.ViewNotification, StringComparison.OrdinalIgnoreCase))
                    return;

                string result = WireViewShim.RunEndUpdate();
                if (!string.IsNullOrEmpty(result) && !_form.IsDisposed)
                    _form.Log.AppendText($"{DateTime.Now:HH:mm:ss} {result}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                WireViewShim.RecordError("view", ex);
            }
        }
    }
}
