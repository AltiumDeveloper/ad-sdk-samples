using System;
using System.Runtime.InteropServices;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). In .NET,
    /// DXP.ServerModule.ReceiveNotification is not virtual (CS0506), so this sample registers a notification handler
    /// with the client instead: IClient.RegisterNotificationHandler with an INotificationHandler object, once, when
    /// AD creates the server module. The handler lives in the shim, so it survives hot reloads of the implementation.
    /// </summary>
    public partial class PluginServerModule
    {
        // Kept in a field: AD holds the handler through COM, and the .NET object must stay reachable.
        private NotificationHandler _notificationHandler;

        // The framework's constructor calls RegisterViews(client) once, after InitializeCommands. This sample has no
        // panel (no Panel.cs), so the partial method is free for the handler registration.
        partial void RegisterViews(IClient client)
        {
            // DXP.NotificationHandler is the SDK's own INotificationHandler: it calls the delegate. A handler class of
            // the sample's own ([ClassInterface(AutoDispatch)], implementing INotificationHandler) made AD show
            // "Member not found at ... DXPSDK.dll" for every broadcast notification; the SDK
            // class is called without error.
            _notificationHandler = new NotificationHandler(DocumentNotificationHandler.HandleNotification);
            client.RegisterNotificationHandler(_notificationHandler);
        }
    }

    /// <summary>
    /// Receives every notification AD broadcasts. It only filters and forwards: what to do with a document
    /// notification is decided by the implementation's "Notification.Document" command, which hot-reloads in Debug
    /// builds like any other command. AD may deliver a notification more than once (AD delivers every
    /// one twice), so the implementation must not assume one call per event.
    /// </summary>
    public static class DocumentNotificationHandler
    {
        /// <summary>AppDomain data key (bool): set by the EnableNotifications command. Off until a user turns it on.</summary>
        public const string EnabledKey = SampleInfo.Name + ".NotificationsEnabled";

        /// <summary>AppDomain data key (string): the last exception the forwarding caught, shown by EnableNotifications.</summary>
        public const string LastErrorKey = SampleInfo.Name + ".NotificationError";

        /// <summary>AppDomain data key (int): notifications received while on, of any type (shows the handler is called).</summary>
        public const string ReceivedKey = SampleInfo.Name + ".NotificationsReceived";

        public static void HandleNotification(INotification argNotification)
        {
            // Notifications arrive for everything in the whole of AD (documents, views, messages...), so the switch
            // is checked first and nothing else is done while it is off.
            if (!(AppDomain.CurrentDomain.GetData(EnabledKey) is bool enabled) || !enabled)
                return;

            try
            {
                var received = AppDomain.CurrentDomain.GetData(ReceivedKey) is int n ? n : 0;
                AppDomain.CurrentDomain.SetData(ReceivedKey, received + 1);

                var notification = argNotification;
                if (notification == null)
                    return;
                // DXP.NotificationType.DocumentNotification is the .NET form of cDocumentNotification.
                if (!string.Equals(notification.GetNotificationType(), NotificationType.DocumentNotification, StringComparison.OrdinalIgnoreCase))
                    return;

                // The notification goes to the implementation as the command's context (ctx.View); the code also
                // travels as a parameter, so the implementation can log codes it does not handle.
                Commands.Invoke("Notification.Document", "Code=" + notification.GetNotificationCode(), notification);
            }
            catch (Exception ex)
            {
                // Never let an exception (or a dialog) escape into AD's notification loop: keep it for the status.
                AppDomain.CurrentDomain.SetData(LastErrorKey, DateTime.Now.ToString("HH:mm:ss") + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
