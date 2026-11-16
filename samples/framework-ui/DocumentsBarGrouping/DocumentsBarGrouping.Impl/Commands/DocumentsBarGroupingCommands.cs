using System;
using DXP;

namespace AdSamples.DocumentsBarGrouping
{
    /// <summary>
    /// Reads and changes how the Documents Bar groups the open documents (a client-level UI preference,
    /// exposed by IClientAPI_Interface). No document is needed.
    /// </summary>
    internal static class DocumentsBarGroupingCommands
    {
        /// <summary>Read-only: returns the current grouping, so the change can be read back.</summary>
        [HotCommand("GetGroupingInDocumentsBar", "Show the Documents Bar grouping")]
        public static string GetGroupingInDocumentsBar(CommandContext ctx)
        {
            IClientAPI_Interface clientApi = ClientApi();
            return $"Documents Bar grouping: {Describe(clientApi.GetGroupingInDocumentsBar())}";
        }

        /// <summary>
        /// SetGroupingInDocumentsBar "Mode=Toggle|ByDocKind|ByProject|None" (default Toggle).
        /// Toggle switches by document kind and by project; any other current value
        /// (dbgNone) becomes by project.
        /// </summary>
        [HotCommand("SetGroupingInDocumentsBar", "Toggle the Documents Bar grouping")]
        public static string SetGroupingInDocumentsBar(CommandContext ctx)
        {
            string mode = ctx.Get("Mode", "Toggle");
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            IClientAPI_Interface clientApi = ClientApi();

            TDocumentsBarGrouping previous = clientApi.GetGroupingInDocumentsBar();
            TDocumentsBarGrouping next = mode.ToLowerInvariant() switch
            {
                "toggle" => previous switch
                {
                    TDocumentsBarGrouping.dbgByDocKind => TDocumentsBarGrouping.dbgByProject,
                    TDocumentsBarGrouping.dbgByProject => TDocumentsBarGrouping.dbgByDocKind,
                    _ => TDocumentsBarGrouping.dbgByProject,
                },
                "bydockind" => TDocumentsBarGrouping.dbgByDocKind,
                "byproject" => TDocumentsBarGrouping.dbgByProject,
                "none" => TDocumentsBarGrouping.dbgNone,
                _ => throw new UserException($"Unknown Mode '{mode}'. Use Mode=Toggle, ByDocKind, ByProject or None."),
            };

            clientApi.SetGroupingInDocumentsBar(next);

            // Broadcast a "system preferences changed" notification after the change, so that subscribers
            // (such as the Documents Bar) refresh. SystemNotification takes the int code from SystemNotificationCode.
            client.BroadcastNotification(new SystemNotification(DXP.NotificationCode.SystemNotificationCode.SystemPreferencesChanged));

            return $"Documents Bar grouping changed from {Describe(previous)} to {Describe(clientApi.GetGroupingInDocumentsBar())}.";
        }

        private static IClientAPI_Interface ClientApi()
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            // GetClientAPI is a typed helper (extension method) on IClient. DXP.GlobalVars.ClientApi is also typed
            // IClientAPI_Interface; this sample takes the route through the client.
            return client.GetClientAPI()
                ?? throw new UserException("IClient.GetClientAPI returned no client API.");
        }

        private static string Describe(TDocumentsBarGrouping grouping) => grouping switch
        {
            TDocumentsBarGrouping.dbgNone => "none (dbgNone)",
            TDocumentsBarGrouping.dbgByDocKind => "by document kind (dbgByDocKind)",
            TDocumentsBarGrouping.dbgByProject => "by project (dbgByProject)",
            _ => $"unknown value {(int)grouping}",
        };
    }
}
