# Broadcast a custom vault state-transition notification

Creates a custom notification and broadcasts it to Altium Designer. The notification is a `DXP.BaseNotification`
that also implements `DXP.IValidateStateTransitionNotification`, the notification used to validate a vault state
transition. The sample shows how to subclass the SDK's notification class, how to give a dispatch-only interface
its fixed dispatch IDs, and the difference between the synchronous and the asynchronous broadcast.

## What it shows
- `DXP.BaseNotification`: the SDK's notification class. Its constructor takes the notification type and data. Its
  protected virtual `SupportsServiceImpl` and `GetNotificationTypeImpl` / `GetNotificationCodeImpl` /
  `GetNotificationDataImpl` can be overridden. They are in the assembly but not in the API reference.
- `DXP.IValidateStateTransitionNotification`: a dispatch-only interface. Its members carry fixed dispatch IDs
  (`Internal_GetRevision` 4710 … `SetNote` 4716). The implementing class repeats them with `[DispId]` on an
  `AutoDispatch` class interface, the same form `DXP.BaseNotification` uses for `GetNotificationType` (723).
- `DXP.IValidateStateTransitionNotification.Valid` / `Note`: the validator's answer. The class implements the raw
  accessors behind these properties (`GetValid` / `SetValid` / `GetNote` / `SetNote`); the sample itself never uses
  the properties. That a subscriber refuses a transition by setting `Valid = false` and a `Note` is inferred from
  the names.
- `DXP.SupportService.SupportsService`: how a subscriber recognises the notification. The override returns true for
  `ValidateStateTransitionNotification`, the value of `ServiceNames.ValidateStateTransitionNotificationServiceName`
  (class `DXP.ServiceNames` in Altium.SDK.Interfaces, not in the API reference).
- `DXP.IClient.BroadcastNotificationAsynchronously`: queues the notification (the default mode). Only the
  delivery to subscribers is deferred: AD reads the notification's type, code and data before the call returns.
- `DXP.IClient.BroadcastNotification`: delivers it to every subscriber before it returns.

## Run it
1. No document is needed. The notification class is in the shim, so the extension must be deployed with
   `tools/deploy.ps1 -Sample TestNotification` and Altium Designer restarted.
2. **TestNotification › Broadcast Synchronously**, or `tools/ad.ps1 -Sample TestNotification run BroadCast "Mode=sync"`
3. The result shows the notification number, its type (empty by default), Valid `True`, Note `''`, the members
   that callers invoked on it and the services they asked `SupportsService` about. **Broadcast** (asynchronous)
   only queues it: run **Notification Status** afterwards to see what happened to it.

| Command | Parameters | Result |
|---|---|---|
| `BroadCast` | `Type=<string>` (default empty), `Mode=async\|sync` (default `async`), `Confirm` (needed for the two vault types) | `Queued notification #n ...` or `Broadcast notification #n synchronously ...`, then type, service, Valid, Note, calls, services asked |
| `NotificationStatus` | `Reset` (optional flag) | Helper: the same report for the last notification, or `No notification broadcast yet in this AD session.` |

## How it works
`TestNotification/ValidateStateTransitionNotification.cs` (shim) holds the notification class. It also holds the
`OnCreated` hook of `PluginServerModule`, which publishes two delegates in AppDomain data: broadcast, and read
the last notification back. The broadcast delegate creates a `ValidateStateTransitionNotification`
(`BaseNotification(type, null)`) and calls `IClient.BroadcastNotification` or
`IClient.BroadcastNotificationAsynchronously` on `GlobalVars.Client`. The class counts every call on its members
and records each service name passed to `SupportsServiceImpl`. `TestNotification.Impl/Commands/TestNotificationCommands.cs`
checks the parameters, calls the delegates and formats the snapshot as text.

The class sits in the shim, not in the hot-reloadable implementation. AD receives the object and may call it after
the command returned, so its code must stay loaded and unchanged.

## Notes
- The `[DispId]` form of the class is the same as the SDK's own `BaseNotification`. AD accepts the object without
  error dialogs and without the "Member not found" errors that a notification *handler* declared without dispatch
  IDs can produce. This sample registers no handler: it only sends.
- Nobody subscribes to an empty notification type, and the sample has no subscriber of its own. The call counters
  are the only visible effect. One broadcast makes AD call the overrides 24 times: `GetNotificationTypeImpl` x14,
  `GetNotificationCodeImpl` x8, `GetNotificationDataImpl` x2. The sealed public getters therefore delegate to the
  `*Impl` methods.
- `SupportsServiceImpl` is never called during a broadcast: subscribers are matched on the notification type, not
  on the advertised service name.
- `BroadcastNotificationAsynchronously` reads type, code and data on the calling thread, with the same call counts
  as the synchronous `BroadcastNotification`, and makes no later call; only the delivery to subscribers is
  deferred. The notification object must still stay loaded, which is why it lives in the shim.
- With an empty type, no member of `IValidateStateTransitionNotification` is called, and `Valid` / `Note` stay
  `True` / `''`: the broadcast reaches no validate-state-transition subscriber.
- `altium.dxp.validatestatetransition` and `altium.dxp.validatestatetransitionex` are the vault notification
  types. They come from `DXP.NotificationType` in Altium.Dxp.Classes.dll; the SDK's `DXP.NotificationType` doesn't
  have them. `BroadCast` refuses them without `Confirm`: AD's vault code might act on a notification whose
  `Internal_GetVault`, `Internal_GetRevision` and `Internal_GetStateTransition` return null.
- `SetValid` / `SetNote` store the value, so a subscriber's answer can be read back.
- `BroadcastNotification` has two forms: an `IClient` instance method taking `object`, and an extension method
  taking `INotification`. C# picks the instance method. What the extension adds is not documented.
- The top-level **TestNotification** menu is merged into AD's menu bar at startup (`Updates = 'AdvSch'`), before
  the server starts. The menu items show their result in a modal message box.
