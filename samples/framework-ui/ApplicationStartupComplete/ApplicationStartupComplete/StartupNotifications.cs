using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). In .NET,
    /// DXP.ServerModule.ReceiveNotification is not virtual (overriding it fails with CS0506),
    /// so this sample registers an INotificationHandler with IClient.RegisterNotificationHandler instead.
    ///
    /// The handler is NOT registered when AD creates the server module: a handler declared without the DispId made
    /// AD show "Member not found" error dialogs for every notification, so registering is an explicit step (the
    /// Listen command) that a user takes on purpose, and can undo. The shim publishes the switch for the
    /// implementation as a delegate in AppDomain data (BCL types only, so it crosses the hot reload boundary).
    /// </summary>
    public partial class PluginServerModule
    {
        private static StartupNotificationHandler _handler;

        // Template hook OnCreated: called once, when Altium Designer creates the server module.
        // Publishes the switch the implementation uses to register the handler.
        partial void OnCreated(IClient client)
        {
            AppDomain.CurrentDomain.SetData(StartupNotificationHandler.ServerCreatedKey, DateTime.Now);
            AppDomain.CurrentDomain.SetData(StartupNotificationHandler.CountsKey, new ConcurrentDictionary<string, int>());
            AppDomain.CurrentDomain.SetData(StartupNotificationHandler.ListenKey, (Func<bool, string>)SetListening);
        }

        /// <summary>
        /// Registers (on = true) or unregisters the handler. Returns what it did, for the command's text. The client is
        /// re-queried each time (GlobalVars.Client), not kept in a static.
        /// </summary>
        private static string SetListening(bool on)
        {
            if (on)
            {
                if (_handler != null)
                    return "already registered";
                _handler = new StartupNotificationHandler();
                GlobalVars.Client.RegisterNotificationHandler(_handler);
                AppDomain.CurrentDomain.SetData(StartupNotificationHandler.ListeningSinceKey, DateTime.Now);
                return "registered";
            }

            if (_handler == null)
                return "not registered";
            GlobalVars.Client.UnregisterNotificationHandler(_handler);
            _handler = null;
            AppDomain.CurrentDomain.SetData(StartupNotificationHandler.ListeningSinceKey, null);
            return "unregistered";
        }
    }

    /// <summary>
    /// Receives every notification AD broadcasts while registered. It counts them by type (and code, for view
    /// notifications) and forwards ApplicationStartupComplete to the implementation's "Notification.View" command,
    /// the place for an extension's one-time "AD is ready" work.
    ///
    /// Declaration: DXP.INotificationHandler is a dispatch-only interface, and its HandleNotification carries
    /// DispId 266 in Altium.SDK.Interfaces. AD presumably calls the handler
    /// through IDispatch with that ID, so the method repeats [DispId(266)] on the AutoDispatch class interface, the
    /// same form DXP.SupportService uses for its callback ([DispId(1023)]). This declaration is
    /// called without error; a handler without the DispId made AD report "Member not found".
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class StartupNotificationHandler : INotificationHandler
    {
        // AppDomain data is shared by every extension in AD: the keys carry the sample name. The implementation
        // repeats these strings (it cannot reference the shim assembly).

        /// <summary>DateTime: when AD created this server module.</summary>
        public const string ServerCreatedKey = SampleInfo.Name + ".ServerCreated";

        /// <summary>DateTime or null: when the handler was registered.</summary>
        public const string ListeningSinceKey = SampleInfo.Name + ".ListeningSince";

        /// <summary>Func&lt;bool, string&gt;: registers or unregisters the handler.</summary>
        public const string ListenKey = SampleInfo.Name + ".Listen";

        /// <summary>ConcurrentDictionary&lt;string, int&gt;: notifications seen, keyed "type" or "type/code" (view).</summary>
        public const string CountsKey = SampleInfo.Name + ".NotificationCounts";

        /// <summary>string: the last exception the handler caught.</summary>
        public const string LastErrorKey = SampleInfo.Name + ".NotificationError";

        [DispId(266)]
        public void HandleNotification(object argNotification)
        {
            try
            {
                var notification = (INotification)argNotification;
                string type = notification.GetNotificationType() ?? "";
                bool isView = string.Equals(type, NotificationType.ViewNotification, StringComparison.OrdinalIgnoreCase);
                int code = notification.GetNotificationCode();

                if (AppDomain.CurrentDomain.GetData(CountsKey) is ConcurrentDictionary<string, int> counts)
                    counts.AddOrUpdate(isView ? type + "/" + code : type, 1, (_, n) => n + 1);

                // The startup notification: type cViewNotification and code cApplicationStartupComplete.
                if (isView && code == NotificationCode.ViewNotificationCode.ApplicationStartupComplete)
                    Commands.Invoke("Notification.View", "Code=" + code, notification);
            }
            catch (Exception ex)
            {
                // Never let an exception (or a dialog) escape into AD's notification loop: keep it for StartupStatus.
                AppDomain.CurrentDomain.SetData(LastErrorKey, DateTime.Now.ToString("HH:mm:ss") + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
