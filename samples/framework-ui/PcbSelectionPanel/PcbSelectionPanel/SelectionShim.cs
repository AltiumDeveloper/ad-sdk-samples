using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Forms;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). The sample does two things AD drives:
    /// 1. on DocumentActivating of a PCB document, it adds a panel view to that document with IServerDocument.AddView;
    /// 2. that view receives notifications and reacts to the view notification code 3 (selection changed).
    ///
    /// In .NET, DXP.ServerModule.ReceiveNotification is not virtual (an override fails with
    /// CS0506), so (1) needs a handler registered with IClient.RegisterNotificationHandler. Earlier samples' handlers
    /// made AD show "Member not found" error dialogs, so nothing here runs on its own: the handler is registered, and
    /// the per-document view is added, only through the implementation's Listen and AttachView commands. Framework
    /// Panel.cs already implements the RegisterViews partial method, so the switches are published from the static
    /// constructor instead, as delegates in AppDomain data (BCL types only: they cross the hot reload boundary).
    /// </summary>
    public partial class PluginServerModule
    {
        static PluginServerModule()
        {
            AppDomain.CurrentDomain.SetData(SelectionShim.CountsKey, new ConcurrentDictionary<string, int>());
            AppDomain.CurrentDomain.SetData(SelectionShim.ListenKey, (Func<bool, string>)SelectionShim.SetListening);
            AppDomain.CurrentDomain.SetData(SelectionShim.AttachViewKey, (Func<string>)SelectionShim.AttachView);
            AppDomain.CurrentDomain.SetData(SelectionShim.AttachedKey, (Func<string[]>)SelectionShim.AttachedDocuments);
        }
    }

    /// <summary>The shim's state and switches. All AppDomain keys carry the sample name (AppDomain data is shared by all extensions).</summary>
    internal static class SelectionShim
    {
        // The implementation repeats these strings: it cannot reference the shim assembly.
        public const string ListenKey = SampleInfo.Name + ".Listen";                  // Func<bool, string>
        public const string AttachViewKey = SampleInfo.Name + ".AttachView";          // Func<string>
        public const string AttachedKey = SampleInfo.Name + ".AttachedDocuments";     // Func<string[]>
        public const string ListeningSinceKey = SampleInfo.Name + ".ListeningSince";  // DateTime or null
        public const string CountsKey = SampleInfo.Name + ".NotificationCounts";      // ConcurrentDictionary<string, int>
        public const string LastErrorKey = SampleInfo.Name + ".NotificationError";    // string

        /// <summary>
        /// The view name of the per-document panel, declared by the second PanelInfo
        /// block of Installation\PcbSelectionPanel.ins. Prefixed with the sample name: panel names are global in AD.
        /// </summary>
        public const string HolderViewName = SampleInfo.Name + "Holder";

        private static NotificationHandler _handler;

        // AD gets the views through COM; the list also keeps the .NET objects (and their forms) reachable. The .NET
        // API has no IServerDocument.RemoveView, so a view stays until its document closes. Both collections live as
        // long as the shim module (they are not cleared on hot reload of the implementation, nor when a document
        // closes): a sample-level simplification. Known limitation: after a PCB is closed and reopened under the
        // same path, AttachedFiles still lists it, so AttachView answers "already attached" for the new document.
        private static readonly List<PcbHolderView> Views = new List<PcbHolderView>();
        private static readonly HashSet<string> AttachedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers (on = true) or unregisters the handler. The client is re-queried each time, not kept.</summary>
        public static string SetListening(bool on)
        {
            if (on)
            {
                if (_handler != null)
                    return "already registered";
                // DXP.NotificationHandler is the SDK's own handler class: it derives from DXP.SupportService, has an
                // AutoDispatch class interface and carries DispId 266 on HandleNotification (read from the metadata of
                // Altium.SDK.dll), and calls the delegate. Earlier samples declared a handler class of their own,
                // which made AD show "Member not found"; AD calls the SDK class without that dialog.
                _handler = new NotificationHandler(OnClientNotification);
                GlobalVars.Client.RegisterNotificationHandler(_handler);
                AppDomain.CurrentDomain.SetData(ListeningSinceKey, DateTime.Now);
                return "registered";
            }

            if (_handler == null)
                return "not registered";
            GlobalVars.Client.UnregisterNotificationHandler(_handler);
            _handler = null;
            AppDomain.CurrentDomain.SetData(ListeningSinceKey, null);
            return "unregistered";
        }

        /// <summary>
        /// The DocumentActivating reaction: add the holder view to the current view's document, if it is a
        /// PCB document that does not have it yet. Returns what it did, for the command's text.
        /// </summary>
        public static string AttachView()
        {
            IServerDocumentView current = GlobalVars.Client.GetCurrentView();
            IServerDocument document = current?.GetOwnerDocument();
            if (document == null)
                return "no document: focus a PCB document first";

            string kind = document.GetKind();
            string file = document.GetFileName();
            if (!string.Equals(kind, "PCB", StringComparison.OrdinalIgnoreCase))
                return $"not a PCB document: {file} is of kind '{kind}'";

            // IServerDocument.GetViewByName in .NET returns IServerDocumentView, which a panel
            // view may not be, so the shim's own list of files is checked first and the document second.
            if (AttachedFiles.Contains(file))
                return "already attached: " + file;
            try
            {
                if (document.GetViewByName(HolderViewName) != null)
                    return "already attached (GetViewByName): " + file;
            }
            catch (Exception ex)
            {
                AppDomain.CurrentDomain.SetData(LastErrorKey, $"{DateTime.Now:HH:mm:ss} GetViewByName: {ex.GetType().Name}: {ex.Message}");
            }

            var view = new PcbHolderView(new PcbHolderForm());
            document.AddView((object)view); // the object overload: a panel view is an IServerView, not an IServerDocumentView
            Views.Add(view);
            AttachedFiles.Add(file);
            return "attached: " + file;
        }

        public static string[] AttachedDocuments()
        {
            var files = new string[AttachedFiles.Count];
            AttachedFiles.CopyTo(files);
            return files;
        }

        /// <summary>
        /// Counts a notification ("source:type/code") and forwards the ones the sample handles to the implementation.
        /// Returns the implementation's text (lines for the holder's text box), or null.
        /// </summary>
        public static string Dispatch(string source, INotification notification)
        {
            string type = notification.GetNotificationType() ?? "";
            int code = notification.GetNotificationCode();
            if (AppDomain.CurrentDomain.GetData(CountsKey) is ConcurrentDictionary<string, int> counts)
                counts.AddOrUpdate($"{source}:{type}/{code}", 1, (_, n) => n + 1);

            bool isView = string.Equals(type, NotificationType.ViewNotification, StringComparison.OrdinalIgnoreCase);
            bool isDocument = string.Equals(type, NotificationType.DocumentNotification, StringComparison.OrdinalIgnoreCase);

            // The view codes: 3 (cViewNotification_SelectionChanged) and 42 (cViewNotification_UpdateViews).
            if (isView && (code == 3 || code == 42))
                return Commands.Invoke("Notification.View", $"Code={code}|Source={source}", notification);
            if (isDocument && code == NotificationCode.DocumentNotificationCode.DocumentActivating)
                return Commands.Invoke("Notification.Document", $"Code={code}|Source={source}", notification);
            return null;
        }

        public static void RecordError(string where, Exception ex) =>
            AppDomain.CurrentDomain.SetData(LastErrorKey, $"{DateTime.Now:HH:mm:ss} {where}: {ex.GetType().Name}: {ex.Message}");

        /// <summary>The registered handler's callback: every notification AD broadcasts while Listen State=on.</summary>
        private static void OnClientNotification(INotification notification)
        {
            try
            {
                Dispatch("handler", notification);
            }
            catch (Exception ex)
            {
                // Never let an exception (or a dialog) escape into AD's notification loop: keep it for Listen State=status.
                RecordError("handler", ex);
            }
        }
    }

    /// <summary>The per-document panel's form: one read-only text box filling the form, with a vertical scroll bar.</summary>
    internal sealed class PcbHolderForm : ServerPanelForm
    {
        public PcbHolderForm()
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
    /// The per-document panel view. Its notifications go to the implementation (through the shim, so it hot-reloads), and the returned lines
    /// are appended to the form's text box.
    /// </summary>
    internal sealed class PcbHolderView : ServerPanelView
    {
        private readonly PcbHolderForm _form;

        public PcbHolderView(PcbHolderForm form) : base(form, SelectionShim.HolderViewName, SampleInfo.Name)
        {
            _form = form;
        }

        // BaseServerView.ReceiveNotification (the member AD calls, DispId 64 in the metadata) is sealed: overriding it
        // fails with CS0506. Beside it the SDK declares the protected virtual ReceiveNotificationImpl(INotification),
        // the overridable form (most sealed DispId members of BaseServerView have such an ...Impl twin). The sealed method
        // forwards to it (Listen State=status counts "view:").
        protected override void ReceiveNotificationImpl(INotification argNotification)
        {
            try
            {
                if (argNotification != null)
                {
                    string lines = SelectionShim.Dispatch("view", argNotification);
                    if (!string.IsNullOrEmpty(lines) && !_form.IsDisposed)
                        _form.Log.AppendText(lines.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                SelectionShim.RecordError("view", ex);
            }
        }
    }
}
