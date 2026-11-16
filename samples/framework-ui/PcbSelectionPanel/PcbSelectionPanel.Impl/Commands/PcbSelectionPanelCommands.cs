using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using PCB;

namespace AdSamples.PcbSelectionPanel
{
    /// <summary>
    /// The sample's behaviour as commands. The shim (SelectionShim.cs) owns what AD calls back: the notification
    /// handler and the per-document panel view. It forwards the notifications the sample handles to
    /// Notification.View and Notification.Document below, which hot-reload like any command.
    /// Nothing listens until Listen State=on: earlier samples' notification handlers made AD show error dialogs.
    /// </summary>
    internal static class PcbSelectionPanelCommands
    {
        // Same strings as CSharpPlugin.SelectionShim in the shim (the implementation cannot reference the shim).
        private const string Prefix = "PcbSelectionPanel.";
        private const string ListenKey = Prefix + "Listen";
        private const string AttachViewKey = Prefix + "AttachView";
        private const string AttachedKey = Prefix + "AttachedDocuments";
        private const string ListeningSinceKey = Prefix + "ListeningSince";
        private const string CountsKey = Prefix + "NotificationCounts";
        private const string LastErrorKey = Prefix + "NotificationError";

        // Owned by the implementation. AppDomain data, not statics, so a hot reload keeps them.
        private const string AutoAttachKey = Prefix + "AutoAttach";   // bool
        internal const string LogKey = Prefix + "Log";                 // List<string>, newest last

        /// <summary>The view codes: cViewNotification_SelectionChanged = 3, cViewNotification_UpdateViews = 42.</summary>
        private const int SelectionChangedCode = 3;
        private const int UpdateViewsCode = 42;
        private const int MaxLogLines = 500;

        /// <summary>ShowSelection: lists the selected objects, returned as text.</summary>
        [HotCommand("ShowSelection", "List the selected primitives of the focused PCB")]
        public static string ShowSelection(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            List<string> ids = SelectedObjectIds(board);
            return FormatSelection(board, ids);
        }

        /// <summary>
        /// AttachView: the DocumentActivating reaction, run on demand. Adds the per-document panel view to the focused PCB document.
        /// </summary>
        [HotCommand("AttachView", "Add the per-document panel view to the focused PCB")]
        public static string AttachView(CommandContext ctx)
        {
            var attach = AppDomain.CurrentDomain.GetData(AttachViewKey) as Func<string>
                ?? throw new UserException("The shim has not published AttachView: redeploy the extension and restart Altium Designer.");
            string result = attach();
            AppendLog($"{Now()} AttachView: {result}");
            return "AttachView: " + result + ".";
        }

        /// <summary>
        /// Listen "State=on|off|status|AutoAttach=true|Reset=true": registers or unregisters the shim's notification
        /// handler and reports what arrived. AutoAttach=true (with State=on) also attaches the panel view to each PCB
        /// document that is activated, on every DocumentActivating.
        /// </summary>
        [HotCommand("Listen", "Register (State=on) or unregister (State=off) the notification handler, or show its status")]
        public static string Listen(CommandContext ctx)
        {
            string state = ctx.Get("State", "status").ToLowerInvariant();
            if (state != "on" && state != "off" && state != "status")
                throw new UserException($"State='{state}': use State=on, State=off or State=status.");

            var data = AppDomain.CurrentDomain;
            if (ctx.Flag("Reset"))
            {
                (data.GetData(CountsKey) as ConcurrentDictionary<string, int>)?.Clear();
                data.SetData(LastErrorKey, null);
                LogLines().Clear();
                Ui.PanelContent.RefreshAll();
            }

            var text = new StringBuilder();
            if (state != "status")
            {
                var setListening = data.GetData(ListenKey) as Func<bool, string>
                    ?? throw new UserException("The shim has not published its notification switch: redeploy the extension and restart Altium Designer.");
                bool autoAttach = state == "on" && ctx.Flag("AutoAttach");
                data.SetData(AutoAttachKey, autoAttach);
                text.AppendLine($"Notification handler {setListening(state == "on")}" +
                                (state == "on" ? $", AutoAttach {(autoAttach ? "on" : "off")}." : "."));
            }
            text.Append(Status());
            return text.ToString();
        }

        /// <summary>
        /// SelectTest "Kind=Pad|Count=3" or "Clear=true": a helper. Selects the first Count
        /// primitives of a kind on the focused board through IPCB_Primitive.Selected (or deselects all), so that
        /// ShowSelection and the panel have something to list without a click in the editor.
        /// </summary>
        [HotCommand("SelectTest", "Select the first Count primitives of a kind (Kind=Pad|Count=3), or Clear=true")]
        public static string SelectTest(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            bool clear = ctx.Flag("Clear");
            string kindName = ctx.Get("Kind", "Pad");
            if (!int.TryParse(ctx.Get("Count", "3"), out int count) || count < 1)
                throw new UserException("Count must be a positive number.");
            TObjectId kind = ParseKind(kindName);

            // Deliberate exception to the PreProcess/PostProcess + BeginModify/EndModify rule: only the selection
            // state changes, which is not a design edit, so no undo step is wanted here.
            int changed = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                if (clear)
                    iterator.SetState_FilterAll();
                else
                    iterator.AddFilter_ObjectSet(new PCB.TObjectSet { kind });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive primitive = iterator.FirstPCBObject(); primitive != null; primitive = iterator.NextPCBObject())
                {
                    if (clear)
                    {
                        if (!primitive.GetState_Selected())
                            continue;
                        primitive.SetState_Selected(false);
                        changed++;
                    }
                    else
                    {
                        primitive.SetState_Selected(true);
                        if (++changed == count)
                            break;
                    }
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            board.ViewManager_FullUpdate(); // repaint, so the selection shows in the editor

            return clear
                ? $"Deselected {changed} primitive(s) on {Path.GetFileName(board.GetState_FileName())}."
                : $"Selected {changed} {kind} primitive(s) on {Path.GetFileName(board.GetState_FileName())}" +
                  (changed < count ? $" (the board has only {changed})." : ".");
        }

        /// <summary>
        /// Called by the shim for the view codes the sample handles (3 and 42), from the registered handler
        /// (Source=handler) or from the per-document view (Source=view). Returns the lines for the view's text box.
        /// A modal dialog on every selection change would block AD, so the IDs are logged.
        /// </summary>
        [HotCommand("Notification.View", "Selection changed / update views (called by the shim, not by users)")]
        public static string NotificationView(CommandContext ctx)
        {
            if (!(ctx.View is INotification notification))
                throw new UserException("Notification.View is called by the shim, not by users. Run ShowSelection.");

            int code = notification.GetNotificationCode();
            string source = ctx.Get("Source", "?");
            string head = $"{Now()} {notification.GetNotificationType()} code {code} ({source})";
            var lines = new List<string>();
            if (code == SelectionChangedCode)
            {
                // This iterates the board on every selection change, which is slow on large boards.
                IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard();
                if (board == null)
                    lines.Add(head + ": no current PCB board");
                else
                {
                    List<string> ids = SelectedObjectIds(board);
                    lines.Add($"{head}: {ids.Count} selected");
                    lines.AddRange(ids.Select(id => "  " + id));
                }
            }
            else if (code == UpdateViewsCode)
                lines.Add(head + ": update views");
            else
                return "";

            foreach (string line in lines)
                AppendLog(line);
            Ui.PanelContent.RefreshAll();
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Called by the shim on DocumentActivating. With
        /// AutoAttach on, it attaches the panel view; otherwise it only logs the activation.
        /// </summary>
        [HotCommand("Notification.Document", "Document activating (called by the shim, not by users)")]
        public static string NotificationDocument(CommandContext ctx)
        {
            if (!(ctx.View is INotification))
                throw new UserException("Notification.Document is called by the shim, not by users. Run AttachView.");

            string line;
            if (AppDomain.CurrentDomain.GetData(AutoAttachKey) is bool autoAttach && autoAttach
                && AppDomain.CurrentDomain.GetData(AttachViewKey) is Func<string> attach)
                line = $"{Now()} DocumentActivating: {attach()}";
            else
                line = $"{Now()} DocumentActivating (AutoAttach off)";
            AppendLog(line);
            Ui.PanelContent.RefreshAll();
            return "";
        }

        // ----- helpers -----

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not loaded: open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard() ?? throw new UserException("Open a .PcbDoc and focus it first.");
        }

        /// <summary>
        /// A board iterator over all objects (cAllObjects), all layers (cAllLayers) and
        /// eProcessAll, keeping the primitives whose Selected is true.
        /// </summary>
        internal static List<string> SelectedObjectIds(IPCB_Board board)
        {
            var ids = new List<string>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.SetState_FilterAll(); // every object kind
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive primitive = iterator.FirstPCBObject(); primitive != null; primitive = iterator.NextPCBObject())
                    if (primitive.GetState_Selected())
                        ids.Add(primitive.GetState_ObjectIDString());
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return ids;
        }

        internal static string FormatSelection(IPCB_Board board, List<string> ids)
        {
            // SelectecObjectCount (sic, the SDK's spelling) is the board's own selection list: a cross-check of the loop.
            var text = new StringBuilder();
            text.Append($"{Path.GetFileName(board.GetState_FileName())}: {ids.Count} selected primitive(s) " +
                        $"(board selection list: {board.GetState_SelectecObjectCount()})");
            foreach (var group in ids.GroupBy(id => id).OrderBy(g => g.Key, StringComparer.Ordinal))
                text.Append($"\n  {group.Key}: {group.Count()}");
            return text.ToString();
        }

        private static TObjectId ParseKind(string name)
        {
            string wanted = name.StartsWith("e", StringComparison.Ordinal) ? name : "e" + name + "Object";
            if (Enum.TryParse(wanted, true, out TObjectId kind) && kind != TObjectId.eNoObject)
                return kind;
            throw new UserException($"Kind='{name}': use a PCB.TObjectId name such as Pad, Via, Track or Component.");
        }

        private static string Status()
        {
            var data = AppDomain.CurrentDomain;
            var text = new StringBuilder();
            var since = data.GetData(ListeningSinceKey) as DateTime?;
            text.AppendLine(since == null
                ? "Notification handler: not registered (Listen State=on)."
                : $"Notification handler: registered since {since:HH:mm:ss}, AutoAttach {(data.GetData(AutoAttachKey) is true ? "on" : "off")}.");

            string[] attached = (data.GetData(AttachedKey) as Func<string[]>)?.Invoke() ?? Array.Empty<string>();
            text.AppendLine($"Panel views attached: {attached.Length}" +
                            (attached.Length > 0 ? " (" + string.Join(", ", attached.Select(Path.GetFileName)) + ")" : "") + ".");

            // Keys are "source:type/code"; view codes are named with the SDK's ViewNotificationCode for reference.
            var counts = data.GetData(CountsKey) as ConcurrentDictionary<string, int>;
            text.AppendLine($"Notifications received: {counts?.Values.Sum() ?? 0}.");
            if (counts != null)
                foreach (var entry in counts.OrderBy(e => e.Key, StringComparer.Ordinal))
                    text.AppendLine($"  {Describe(entry.Key)}: {entry.Value}");

            List<string> log = LogLines();
            text.AppendLine($"Log: {log.Count} line(s){(log.Count > 0 ? ", last: " + log[^1] : "")}.");
            text.Append($"Last error: {data.GetData(LastErrorKey) as string ?? "none"}.");
            return text.ToString();
        }

        /// <summary>"view:altium.dxp.view/3" becomes "view:altium.dxp.view/3 (SDK: DocumentDataRefresh)".</summary>
        private static string Describe(string key)
        {
            int slash = key.LastIndexOf('/');
            if (slash < 0 || !int.TryParse(key.Substring(slash + 1), out int code))
                return key;
            string type = key.Substring(key.IndexOf(':') + 1, slash - key.IndexOf(':') - 1);
            string name = string.Equals(type, NotificationType.ViewNotification, StringComparison.OrdinalIgnoreCase)
                ? NotificationCode.ViewNotificationCode.ToString(code)
                : string.Equals(type, NotificationType.DocumentNotification, StringComparison.OrdinalIgnoreCase)
                    ? NotificationCode.DocumentNotificationCode.ToString(code)
                    : null;
            return string.IsNullOrEmpty(name) ? key : $"{key} (SDK: {name})";
        }

        internal static List<string> LogLines()
        {
            if (AppDomain.CurrentDomain.GetData(LogKey) is List<string> log)
                return log;
            log = new List<string>();
            AppDomain.CurrentDomain.SetData(LogKey, log);
            return log;
        }

        private static void AppendLog(string line)
        {
            List<string> log = LogLines();
            log.Add(line);
            if (log.Count > MaxLogLines)
                log.RemoveRange(0, log.Count - MaxLogLines);
        }

        private static string Now() => DateTime.Now.ToString("HH:mm:ss");
    }
}
