using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using DXP;

namespace AdSamples.ApplicationStartupComplete
{
    /// <summary>
    /// The startup-complete reaction as the command
    /// "Notification.View", plus a switch for the shim's notification handler and a status read-back.
    /// The shim (StartupNotifications.cs) owns the handler and the data; this side only reads and formats it.
    /// </summary>
    internal static class ApplicationStartupCompleteCommands
    {
        // Same strings as CSharpPlugin.StartupNotificationHandler in the shim (AppDomain data is shared by all
        // extensions, so every key carries the sample name).
        private const string Prefix = "ApplicationStartupComplete.";
        private const string ServerCreatedKey = Prefix + "ServerCreated";
        private const string ListeningSinceKey = Prefix + "ListeningSince";
        private const string ListenKey = Prefix + "Listen";
        private const string CountsKey = Prefix + "NotificationCounts";
        private const string LastErrorKey = Prefix + "NotificationError";

        // Written by Notification.View. AppDomain data, not a static, so a hot reload of this module keeps it.
        private const string StartupCompleteKey = Prefix + "StartupCompleteAt";

        /// <summary>StartupStatus "[Reset]": was ApplicationStartupComplete received, and what else arrived.</summary>
        [HotCommand("StartupStatus", "Show whether the startup-complete notification was received")]
        public static string StartupStatus(CommandContext ctx)
        {
            var data = AppDomain.CurrentDomain;
            var counts = data.GetData(CountsKey) as ConcurrentDictionary<string, int>;
            if (ctx.Flag("Reset"))
            {
                counts?.Clear();
                data.SetData(StartupCompleteKey, null);
                data.SetData(LastErrorKey, null);
            }

            var text = new StringBuilder();
            var created = data.GetData(ServerCreatedKey) as DateTime?;
            var since = data.GetData(ListeningSinceKey) as DateTime?;
            var startup = data.GetData(StartupCompleteKey) as DateTime?;

            text.AppendLine($"Server module created: {Time(created)}.");
            text.AppendLine(since == null
                ? "Notification handler: not registered (run Listen State=on)."
                : $"Notification handler: registered since {Time(since)}.");
            text.AppendLine(startup != null
                ? $"ApplicationStartupComplete: received at {Time(startup)}. Altium startup complete."
                : "ApplicationStartupComplete: not received. It is raised once, when AD finishes starting; " +
                  "an extension that starts on first use registers its handler later and cannot see it.");

            int total = counts?.Values.Sum() ?? 0;
            text.AppendLine($"Notifications received: {total}.");
            if (counts != null)
                foreach (var entry in counts.OrderBy(e => e.Key, StringComparer.Ordinal))
                    text.AppendLine($"  {Describe(entry.Key)}: {entry.Value}");
            text.Append($"Last error: {data.GetData(LastErrorKey) as string ?? "none"}.");
            return text.ToString();
        }

        /// <summary>Listen "State=on|off": registers or unregisters the shim's notification handler.</summary>
        [HotCommand("Listen", "Register (State=on) or unregister (State=off) the notification handler")]
        public static string Listen(CommandContext ctx)
        {
            string state = ctx.Get("State", "on").ToLowerInvariant();
            if (state != "on" && state != "off")
                throw new UserException($"State='{state}': use State=on or State=off.");

            var setListening = AppDomain.CurrentDomain.GetData(ListenKey) as Func<bool, string>
                ?? throw new UserException("The shim has not published its notification switch (server module not created?).");
            string result = setListening(state == "on");
            return $"Notification handler {result}.";
        }

        /// <summary>
        /// Called by the shim's handler when ApplicationStartupComplete arrives (the notification is the context).
        /// This is where an extension does its one-time work once AD is ready. A modal dialog
        /// inside AD's notification dispatch (during startup) would block AD, so the sample records the
        /// time and StartupStatus reports it.
        /// </summary>
        [HotCommand("Notification.View", "Records ApplicationStartupComplete (called by the shim's notification handler, not by users)")]
        public static string NotificationView(CommandContext ctx)
        {
            if (!(ctx.View is INotification notification))
                throw new UserException("Notification.View is called by the shim's notification handler, not by users. Run StartupStatus.");

            if (string.Equals(notification.GetNotificationType(), NotificationType.ViewNotification, StringComparison.OrdinalIgnoreCase) &&
                notification.GetNotificationCode() == NotificationCode.ViewNotificationCode.ApplicationStartupComplete)
                AppDomain.CurrentDomain.SetData(StartupCompleteKey, DateTime.Now);
            return "";
        }

        private static string Time(DateTime? time) => time?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown";

        /// <summary>"altium.dxp.view/6" becomes "altium.dxp.view ApplicationStartupComplete(6)".</summary>
        private static string Describe(string key)
        {
            int slash = key.LastIndexOf('/');
            if (slash < 0 || !int.TryParse(key.Substring(slash + 1), out int code))
                return key;
            return $"{key.Substring(0, slash)} {NotificationCode.ViewNotificationCode.ToString(code)}({code})";
        }
    }
}
