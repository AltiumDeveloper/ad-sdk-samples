# Detect when Altium Designer finishes starting

Registers a notification handler and records the view notification `ApplicationStartupComplete`, which Altium
Designer raises once when it has finished starting. It teaches how a .NET extension receives AD's broadcast
notifications (in place of a `ReceiveNotification` override), and why an extension that starts on first use is too
late for this particular one.

## What it shows
- `DXP.IClient.RegisterNotificationHandler` / `DXP.IClient.UnregisterNotificationHandler`: the .NET replacement for
  overriding `ReceiveNotification` (not virtual in .NET). The handler is a shim object, registered on request.
- `DXP.INotificationHandler.HandleNotification`: called by AD for every notification while registered. The class
  is `[ComVisible(true)]`, `AutoDispatch`, and repeats the interface's dispatch ID `[DispId(266)]`.
- `DXP.INotification.GetNotificationType` / `DXP.INotification.GetNotificationCode`: filter the notification.
- `DXP.NotificationType.ViewNotification` and `DXP.NotificationCode.ViewNotificationCode.ApplicationStartupComplete`:
  the .NET names of `cViewNotification` and `cApplicationStartupComplete` (6).
- `DXP.NotificationCode.ViewNotificationCode.ToString`: names a view notification code for the status text.

## Run it
1. The extension must be deployed with `tools/deploy.ps1 -Sample ApplicationStartupComplete` and Altium Designer
   restarted (the handler lives in the deployed part). No document needed. Opening any document afterwards makes AD
   raise notifications to count.
2. **ApplicationStartupComplete › Listen (register handler)**, or
   `tools/ad.ps1 -Sample ApplicationStartupComplete run Listen "State=on"`
3. Open or switch documents, then **ApplicationStartupComplete › Startup Status**: the handler's registration time,
   the notifications received by type (view notifications by code), and `ApplicationStartupComplete: not received`
   (it was raised before the handler existed). **Stop Listening** unregisters the handler.

| Command | Parameters | Result |
|---|---|---|
| `StartupStatus` | `Reset` (flag, optional) | Server module creation time, handler state, whether `ApplicationStartupComplete` arrived and when, notification counts, last error. `Reset` clears counts, startup time and error. |
| `Listen` | `State=on\|off` (default `on`) | `Notification handler registered.` / `already registered.` / `unregistered.` / `not registered.` |
| `Notification.View` | `Code=<n>` (set by the shim) | Called by the shim's handler for `ApplicationStartupComplete`; records the time. Fails when a user runs it. |

## How it works
- `ApplicationStartupComplete/StartupNotifications.cs` (shim): when AD creates the server module, the framework's
  `RegisterViews` hook records the creation time and publishes a `Func<bool, string>` switch in AppDomain data.
  The switch registers or unregisters a `StartupNotificationHandler` with
  `DXP.IClient.RegisterNotificationHandler` / `DXP.IClient.UnregisterNotificationHandler` on `DXP.GlobalVars.Client`
  (re-queried each time, not cached).
- `StartupNotificationHandler.HandleNotification` casts the argument to `DXP.INotification`, counts it by
  `GetNotificationType` (view notifications also by `GetNotificationCode`), and for `ViewNotification` with code
  `ApplicationStartupComplete` calls the implementation command `Notification.View` with the notification as
  context. Exceptions are kept for the status and never reach AD.
- `ApplicationStartupComplete.Impl/Commands/ApplicationStartupCompleteCommands.cs`: `Listen` calls the shim's switch,
  `Notification.View` records the time (the place for an extension's one-time "AD is ready" work), and
  `StartupStatus` formats the AppDomain data, naming codes with `DXP.NotificationCode.ViewNotificationCode.ToString`.

## Notes
- **Timing, the main limitation.** `ApplicationStartupComplete` is raised once, while AD starts. Only an extension
  loaded during startup (a system extension, set in its `.ins`) can see it. This sample has no `SystemExtension` (if
  the shim fails during startup, AD does not start), so it loads on first use, after startup, and reports
  `not received`.
- **Handler declaration.** A .NET handler declared as an `AutoDispatch` class without a dispatch ID made AD show
  "Member not found ... DXPSDK.dll" error dialogs for every notification. This one adds `[DispId(266)]`, the ID
  `HandleNotification` carries in `Altium.SDK.Interfaces`. AD calls it without error, and the argument casts to
  `DXP.INotification`. Registration is still opt-in (`Listen`) and reversible (`Listen State=off`).
- `IClient.UnregisterNotificationHandler` stops the callbacks: after `Listen State=off`, closing a document adds
  nothing to the counts.
- Opening one schematic raises 6 `altium.dxp.document` notifications and 1 `altium.dxp.documentform`, and no
  `altium.dxp.view` notification. Document activity does not produce view notifications.
- The handler receives every notification AD broadcasts, so it only counts and filters; nothing heavy runs on that
  path. The reaction is a recorded time, not a dialog: a modal dialog inside the notification dispatch (during
  startup) would block AD.
- Counts and times live in AppDomain data owned by the shim (keys prefixed `ApplicationStartupComplete.`), so a
  hot reload of the implementation keeps them, and the handler stays registered.
