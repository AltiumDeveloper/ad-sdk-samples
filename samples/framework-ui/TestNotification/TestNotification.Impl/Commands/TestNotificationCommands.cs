using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AdSamples.TestNotification
{
    /// <summary>
    /// The BroadCast command, plus a read-back. The shim (ValidateStateTransitionNotification.cs) owns the
    /// notification class and does the broadcast; this side checks the parameters and formats what the shim reports.
    /// </summary>
    internal static class TestNotificationCommands
    {
        // Same strings as CSharpPlugin.PluginServerModule.BroadcastKey / LastKey in the shim (the implementation cannot
        // reference the shim assembly). AppDomain data is shared by every extension, so the keys carry the sample name.
        private const string BroadcastKey = "TestNotification.Broadcast";
        private const string LastKey = "TestNotification.LastNotification";

        // The notification type strings that AD's own vault code listens for. They are not in the SDK's
        // DXP.NotificationType; the values were read from DXP.NotificationType in Altium.Dxp.Classes.dll.
        // A subscriber that acts on them would get null from Internal_GetVault / GetRevision: guarded by Confirm.
        private static readonly string[] VaultTypes =
        {
            "altium.dxp.validatestatetransition",
            "altium.dxp.validatestatetransitionex",
        };

        /// <summary>BroadCast "[Type=&lt;string&gt;]|[Mode=async|sync]|[Confirm]".</summary>
        [HotCommand("BroadCast", "Broadcast a validate-state-transition notification (Mode=async, the default, or sync)")]
        public static string BroadCast(CommandContext ctx)
        {
            // The type defaults to empty. Trimmed so " altium.dxp.validatestatetransition" cannot slip past
            // the Confirm guard below.
            string type = ctx.Get("Type", string.Empty).Trim();
            string mode = ctx.Get("Mode", "async").ToLowerInvariant();
            if (mode != "async" && mode != "sync")
                throw new UserException($"Mode='{mode}': use Mode=async or Mode=sync.");

            if (VaultTypes.Contains(type, StringComparer.OrdinalIgnoreCase) && !ctx.Flag("Confirm"))
                throw new UserException(
                    $"Type='{type}' is a notification type AD's vault code may act on, and this notification has no vault, " +
                    "revision or state transition (null). Add Confirm to broadcast it anyway.");

            var broadcast = AppDomain.CurrentDomain.GetData(BroadcastKey) as Func<string, bool, IDictionary<string, object>>
                ?? throw new UserException("The shim has not published its broadcast function (server module not created?).");

            bool synchronous = mode == "sync";
            var snapshot = broadcast(type, synchronous);

            var text = new StringBuilder();
            text.AppendLine(synchronous
                ? $"Broadcast notification #{snapshot["Sequence"]} synchronously (IClient.BroadcastNotification): every subscriber has seen it."
                : $"Queued notification #{snapshot["Sequence"]} (IClient.BroadcastNotificationAsynchronously): AD delivers it later; run NotificationStatus.");
            Describe(text, snapshot);
            return text.ToString().TrimEnd();
        }

        /// <summary>NotificationStatus "[Reset]": the last notification broadcast, and what callers did with it.</summary>
        [HotCommand("NotificationStatus", "Show the last broadcast notification and what subscribers asked of it")]
        public static string NotificationStatus(CommandContext ctx)
        {
            var last = AppDomain.CurrentDomain.GetData(LastKey) as Func<bool, IDictionary<string, object>>
                ?? throw new UserException("The shim has not published its read-back function (server module not created?).");

            bool reset = ctx.Flag("Reset");
            var snapshot = last(reset);
            if (snapshot == null)
                return "No notification broadcast yet in this AD session." + (reset ? " (reset)" : "");

            var text = new StringBuilder();
            text.AppendLine($"Last notification: #{snapshot["Sequence"]}, {snapshot["Mode"]}, created {(DateTime)snapshot["Created"]:yyyy-MM-dd HH:mm:ss}.");
            Describe(text, snapshot);
            if (reset)
                text.AppendLine("Reset: the next broadcast is #1.");
            return text.ToString().TrimEnd();
        }

        private static void Describe(StringBuilder text, IDictionary<string, object> s)
        {
            text.AppendLine($"Type: '{s["Type"]}'. Service advertised: '{s["ServiceName"]}'.");
            text.AppendLine($"Valid: {s["Valid"]}. Note: '{s["Note"]}'.");
            text.AppendLine("Calls on the notification: " + Counts(s["Calls"]) + ".");
            text.AppendLine("SupportsService asked for: " + Counts(s["ServicesAsked"]) + ".");
        }

        private static string Counts(object value) =>
            value is IDictionary<string, int> counts && counts.Count > 0
                ? string.Join(", ", counts.Select(e => $"{(e.Key.Length == 0 ? "''" : e.Key)} x{e.Value}"))
                : "none";
    }
}
