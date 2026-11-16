# Add and strip component parameters on document load and save

Reacts to Altium Designer's document notifications: when a schematic is loaded, every component gets a hidden
parameter; when it is saved, the parameter is removed, the document is saved again and the parameter is put back,
so the file on disk stays clean. It shows how a .NET extension receives notifications through a registered
`DXP.INotificationHandler`, and how to add and remove schematic parameters.

## What it shows
- `DXP.IClient.RegisterNotificationHandler`: registers an object whose `DXP.INotificationHandler.HandleNotification`
  AD calls for every notification it broadcasts. Register the SDK's own `DXP.NotificationHandler` with a callback,
  not a handler class of your own: AD could not call a sample class declared `[ClassInterface(AutoDispatch)]`
  ("Member not found ... DXPSDK.dll" dialogs for every notification), while the SDK class works.
  In .NET this replaces an override of `ReceiveNotification`: `DXP.ServerModule.ReceiveNotification` is not virtual.
- `DXP.INotification.GetNotificationType` / `GetNotificationCode` / `GetNotificationData`: what a notification
  carries. Document notifications have the type `DXP.NotificationType.DocumentNotification` (`"altium.dxp.document"`,
  `cDocumentNotification`) and codes from `DXP.NotificationCode.DocumentNotificationCode`, such as
  `DocumentLoading` (0) and `DocumentSaved` (10).
- `DXP.IDataForDocumentNotification.GetServerDocument`: the document a document notification is about, with
  `DXP.IServerDocument.GetKind` (`"SCH"` for schematics) and `GetFileName`.
- `DXP.IServerDocument.DoFileSave`: saves the document again from inside the `DocumentSaved` handler. This save
  raises no `DocumentSaved` (nor `DocumentBeforeSave`), so it does not re-enter the handler; the sample keeps a
  re-entrancy guard anyway, as a safety net.
- `SCH.ISch_ServerInterface.GetSchDocumentByPath`: the schematic document of a server document.
- `SCH.ISch_ServerInterface.SchObjectFactory` (`SCH.TObjectId.eParameter`), `SCH.ISch_Parameter.Name`,
  `SCH.ISch_ComplexText.IsHidden`, `SCH.ISch_BasicContainer.AddSchObject` / `RemoveSchObject`: create a hidden
  component parameter, and remove it.
- `SCH.ISch_Iterator.AddFilter_ObjectSet`: iterate the components of a sheet, and the parameters of a component.
- `SCH.ISch_RobotManager.SendMessage` with `SCHMBeginModify` / `SCHMEndModify` / `SCHMPrimitiveRegistration`: tell the
  editor about the changes.

## Run it
1. The extension must be deployed with `tools/deploy.ps1 -Sample DocumentOpenSaveNotification` and Altium Designer
   restarted (the notification handler lives in the deployed part). Copy a schematic, for example `SL_Power.SchDoc`
   of the SpiritLevel-SL1 example project that ships with Altium Designer, to a scratch folder.
   Don't open it yet.
2. **DocumentOpenSaveNotification › Notifications On**, or
   `tools/ad.ps1 -Sample DocumentOpenSaveNotification run EnableNotifications "State=on|Under=<scratch folder>"`.
3. Open the copied sheet. `CountParameters` then reports that every component carries `SampleParameter`.
   **Save** it (or run `SaveDocument "Via=process"`): the file on disk has no `SampleParameter`, the open sheet
   still has it. Plain `SaveDocument` (`DoFileSave`) saves without raising `DocumentSaved`, so the handler doesn't see it.
   **Notifications Status** lists the notifications seen and what the handler did.

| Command | Parameters | Result |
|---|---|---|
| `EnableNotifications` | `State=on\|off\|status` (default `status`); with `on`: `Name=<text>` (default `SampleParameter`), `Value=<text>` (default `SampleValue`), `Under=<folder>` (optional); `Reset` | The switch, the parameter, the scope, counters per notification code and the last 20 handler events |
| `AddParameters` | `Name=<text>`, `Value=<text>` | `<file>: '<name>'='<value>' added to <n> of <m> components, <k> updated.` |
| `RemoveParameters` | `Name=<text>` | `<file>: '<name>' removed from <n> of <m> components.` |
| `CountParameters` | `Name=<text>`, `Value=<text>` | `<file>: <n> of <m> components carry '<name>' (<k> with value '<value>'). Document modified: yes\|no.` |
| `SaveDocument` | `Via=dofilesave\|process` (default `dofilesave`) | `<file>: DoFileSave returned True.` or, with `Via=process` (`WorkSpaceManager:SaveObject`, as File › Save), `<file>: WorkSpaceManager:SaveObject ran.`, plus what the save handler did |
| `Notification.Document` | set by the shim | Called by the shim's notification handler for each document notification; fails when a user calls it |

`AddParameters`, `RemoveParameters`, `CountParameters` and `SaveDocument` act on the focused schematic sheet. Name
and value default to what `EnableNotifications State=on` set last.

## How it works
`DocumentOpenSaveNotification/DocumentNotifications.cs` is the shim part. When AD creates the server module, it
registers a `DocumentNotificationHandler` with `IClient.RegisterNotificationHandler`. AD then calls its
`HandleNotification` for every notification. While the switch is off (the default) it returns at once. While it
is on, it keeps only notifications of type `NotificationType.DocumentNotification` and passes each one to the
implementation command `Notification.Document`, with the notification as the command's context. Exceptions are
caught and kept for the status, so none reaches AD's notification loop.

`Commands/DocumentNotificationCommand.cs` handles the notification. It reads the code, takes the
`IServerDocument` from the `IDataForDocumentNotification` data and ignores everything that isn't a schematic
(`GetKind() == "SCH"`) inside the `Under` folder. On `DocumentLoading` it gets the sheet with
`GetSchDocumentByPath` and adds the parameter to every component. If the schematic server doesn't know the sheet
yet, it retries on the document's next notification. On `DocumentSaved` it removes the parameter, calls
`DoFileSave("")`, and adds the parameter back. The file is in the `Resaving` set meanwhile, so a `DocumentSaved`
raised by its own save would be ignored (`DoFileSave` raises none). `NotificationState` holds the switch, name,
value and scope (AppDomain data, shared with the shim), the counters and an event log.

`ComponentParameters.cs` holds the parameter helpers. `Find` iterates a component's parameters
(`AddFilter_ObjectSet` with `eParameter`) and compares names regardless of case. `AddToAll` creates a missing
parameter with `SchObjectFactory`: name, text, hidden, the component's location, then `AddSchObject` and
`SCHMPrimitiveRegistration`. If the parameter exists, it updates the text. `RemoveFromAll` calls
`RemoveSchObject`. Each component change is bracketed with `SCHMBeginModify`/`SCHMEndModify`. The commands in
`Commands/DocumentOpenSaveNotificationCommands.cs` call the same helpers on the focused sheet, inside
`PreProcess`/`PostProcess`, so a command is one undo step.

## Notes
- While it is on, the handler changes **every** schematic AD loads or saves, unless `Under` limits it to one
  folder. It is off after every AD start.
- `DXP.ServerModule.ReceiveNotification` is public but not virtual: an `override` fails with CS0506. The reference
  says the method "needs to be overridden", which a .NET extension can't do. Hence the registered handler.
- The handler is registered in the framework's `partial void RegisterViews(IClient)`, the only hook the shared
  `PluginServerModule` constructor offers. A sample with a panel already uses that hook and could not do the same.
- The save handler leaves the sheet in memory with the parameters again, so the sheet is marked modified right after
  a save (`CountParameters` reports `Document modified: yes`).
- `DocumentNotificationCode` also has `DocumentBeforeSave` (29). AD raises it for a schematic saved through
  `WorkSpaceManager:SaveObject`, before `DocumentSaved`. Removing the parameter there would avoid the second
  save; this sample removes it after the save and saves again.
- The `DXP.NotificationHandler` callback is called; **every notification arrives twice**, so the load handler runs
  twice (the second run updates the parameters it added) and the save handler runs twice; `GetSchDocumentByPath`
  already finds the sheet at `DocumentLoading`; `DoFileSave` raises no `DocumentSaved`.
  `DocumentNotificationCode.ToString` gives no name for some codes AD sends (29, 36, 50, 56); the status shows the number.
- The handler is registered when the extension's server starts, which AD does on the extension's first command
  (or `DXP.IClient.StartServer`). Documents opened before that are not seen. Don't add `SystemExtension = True` to the
  `.ins` to start it with AD: that makes AD fail to start ("Member not found ... Failed to load
  DocumentOpenSaveNotification. Failed to initialize application").
- The counters and the event log start over after a hot reload of the implementation (they are statics). The
  switch survives it.
- Changes to the shim (`DocumentNotifications.cs`) or the `.ins`/`.rcs` need a redeploy and an AD restart; the
  implementation hot-reloads in Debug builds.
