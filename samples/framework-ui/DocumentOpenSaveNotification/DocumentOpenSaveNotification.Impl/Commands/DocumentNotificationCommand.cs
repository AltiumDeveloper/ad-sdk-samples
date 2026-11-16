using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXP;
using SCH;
using Code = DXP.NotificationCode.DocumentNotificationCode;

namespace AdSamples.DocumentOpenSaveNotification
{
    /// <summary>
    /// The ReceiveNotification handling, as a command the shim's notification handler calls
    /// (DocumentOpenSaveNotification\DocumentNotifications.cs) for every document notification while the switch is
    /// on. On DocumentLoading it adds the parameter to every component; on DocumentSaved it removes it, saves the
    /// document again (so the file on disk is clean) and adds it back. It never shows a dialog: it runs inside AD's
    /// notification dispatch, where a modal dialog would block AD.
    /// </summary>
    internal static class DocumentNotificationCommand
    {
        [HotCommand("Notification.Document", "Handle one document notification (the shim's notification handler calls this)")]
        public static string Handle(CommandContext ctx)
        {
            // The shim passes the DXP.INotification as the context. A type test would not help here (it succeeds for
            // every SDK object), so only null is checked.
            var notification = ctx.View as INotification
                ?? throw new UserException("Notification.Document is called by the shim's notification handler, not by users. Use EnableNotifications.");

            int code = notification.GetNotificationCode();
            NotificationState.Count(code);

            // The data of a document notification is an IDataForDocumentNotification: the document and its file.
            var data = notification.GetNotificationData() as IDataForDocumentNotification;
            IServerDocument document = data?.GetServerDocument();
            string file = document?.GetFileName() ?? data?.GetFileName();
            if (document == null || string.IsNullOrEmpty(file))
                return null;
            if (!string.Equals(document.GetKind(), "SCH", StringComparison.OrdinalIgnoreCase) || !NotificationState.InScope(file))
                return null;

            if (code == Code.DocumentLoading || NotificationState.PendingLoads.Contains(file))
                OnLoaded(code, file);
            else if (code == Code.DocumentSaved)
                OnSaved(document, file);
            return null;
        }

        /// <summary>cDocumentLoading: AddComponentParameters(SchDoc).</summary>
        private static void OnLoaded(int code, string file)
        {
            // While the document is still loading, the schematic server may not know it yet. Then remember the file
            // and retry on its next document notification (opening, activating...). The event log shows which one.
            ISch_Document sheet = SCH.GlobalVars.SchServer?.GetSchDocumentByPath(file);
            if (sheet == null)
            {
                if (NotificationState.PendingLoads.Add(file))
                    NotificationState.Record(code, file, "not yet known to the schematic server; retrying on its next notification");
                return;
            }
            NotificationState.PendingLoads.Remove(file);

            var (components, created, updated) = ComponentParameters.AddToAll(sheet, NotificationState.Name, NotificationState.Value);
            NotificationState.Loads++;
            NotificationState.Record(code, file, $"added '{NotificationState.Name}' to {created} of {components} components ({updated} updated)");
        }

        /// <summary>cDocumentSaved: RemoveComponentParameters, DoFileSave, AddComponentParameters.</summary>
        private static void OnSaved(IServerDocument document, string file)
        {
            // DoFileSave below raises DocumentSaved again for the same file: without this guard the handler would
            // save forever.
            if (NotificationState.Resaving.Contains(file))
            {
                NotificationState.NestedSaves++;
                NotificationState.Record(Code.DocumentSaved, file, "own re-save, ignored");
                return;
            }

            ISch_Document sheet = SCH.GlobalVars.SchServer?.GetSchDocumentByPath(file);
            if (sheet == null)
            {
                NotificationState.Record(Code.DocumentSaved, file, "not found by the schematic server, nothing done");
                return;
            }

            NotificationState.Resaving.Add(file);
            try
            {
                var (components, removed) = ComponentParameters.RemoveFromAll(sheet, NotificationState.Name);
                // "" = save in the document's own kind. The result is false when the save failed.
                bool saved = document.DoFileSave("");
                var (_, created, _) = ComponentParameters.AddToAll(sheet, NotificationState.Name, NotificationState.Value);
                NotificationState.Saves++;
                NotificationState.Record(Code.DocumentSaved, file,
                    $"removed from {removed} of {components} components, DoFileSave={saved}, added back to {created}");
            }
            finally
            {
                NotificationState.Resaving.Remove(file);
            }
        }
    }

    /// <summary>
    /// The switch and what the handler did. The switch, parameter name/value and scope live in AppDomain data (BCL
    /// types), because the shim reads the switch and they must survive a hot reload. The counters and the event log
    /// are statics of this generation: a hot reload starts them over.
    /// </summary>
    internal static class NotificationState
    {
        // = DocumentNotificationHandler.EnabledKey/ReceivedKey/LastErrorKey in the shim.
        public const string EnabledKey = SampleInfo.Name + ".NotificationsEnabled";
        public const string ReceivedKey = SampleInfo.Name + ".NotificationsReceived";
        public const string LastErrorKey = SampleInfo.Name + ".NotificationError";
        private const string NameKey = SampleInfo.Name + ".ParameterName";
        private const string ValueKey = SampleInfo.Name + ".ParameterValue";
        private const string UnderKey = SampleInfo.Name + ".Under";
        private const int MaxEvents = 20;

        public static readonly HashSet<string> PendingLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> Resaving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, int> Codes = new Dictionary<int, int>();
        private static readonly List<string> Events = new List<string>();

        public static int Loads, Saves, NestedSaves;

        public static bool Enabled
        {
            get => AppDomain.CurrentDomain.GetData(EnabledKey) is bool b && b;
            set => AppDomain.CurrentDomain.SetData(EnabledKey, value);
        }

        public static string Name
        {
            get => AppDomain.CurrentDomain.GetData(NameKey) as string ?? ComponentParameters.DefaultName;
            set => AppDomain.CurrentDomain.SetData(NameKey, value);
        }

        public static string Value
        {
            get => AppDomain.CurrentDomain.GetData(ValueKey) as string ?? ComponentParameters.DefaultValue;
            set => AppDomain.CurrentDomain.SetData(ValueKey, value);
        }

        /// <summary>Only documents under this folder are handled; empty = every schematic AD loads or saves.</summary>
        public static string Under
        {
            get => AppDomain.CurrentDomain.GetData(UnderKey) as string ?? string.Empty;
            set => AppDomain.CurrentDomain.SetData(UnderKey, value);
        }

        public static int Received => AppDomain.CurrentDomain.GetData(ReceivedKey) is int n ? n : 0;

        public static string LastError => AppDomain.CurrentDomain.GetData(LastErrorKey) as string;

        public static bool InScope(string file)
        {
            string under = Under;
            if (under.Length == 0)
                return true;
            string folder = Path.GetFullPath(under).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        public static void Count(int code) => Codes[code] = Codes.TryGetValue(code, out int n) ? n + 1 : 1;

        public static void Record(int code, string file, string what)
        {
            Events.Add($"{DateTime.Now:HH:mm:ss} {CodeName(code)} {Path.GetFileName(file)}: {what}");
            if (Events.Count > MaxEvents)
                Events.RemoveAt(0);
        }

        /// <summary>"DocumentLoading(0)": the name DXP.NotificationCode.DocumentNotificationCode gives the code, and the number.</summary>
        public static string CodeName(int code)
        {
            string name = Code.ToString(code);
            return string.IsNullOrEmpty(name) ? code.ToString() : $"{name}({code})";
        }

        public static string CodeSummary() =>
            Codes.Count == 0 ? "none" : string.Join(", ", Codes.OrderBy(c => c.Key).Select(c => $"{CodeName(c.Key)} x{c.Value}"));

        public static IReadOnlyList<string> RecentEvents => Events;

        public static void ResetCounters()
        {
            Codes.Clear();
            Events.Clear();
            PendingLoads.Clear();
            Loads = Saves = NestedSaves = 0;
            AppDomain.CurrentDomain.SetData(ReceivedKey, 0);
            AppDomain.CurrentDomain.SetData(LastErrorKey, null);
        }
    }
}
