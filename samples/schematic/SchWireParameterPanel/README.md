# Show wire parameters from a panel on every view update

Makes every parameter of every wire on the current schematic sheet visible and selected, on demand or after each
view update of a schematic document. It teaches nested schematic iteration (wires, then each wire's child
parameters), and, in the shim, a notification handler that adds a panel view to each activated schematic document,
plus a dockable panel that lists the sheet's wire parameters.

## What it shows
- `SCH.ISch_BasicContainer.SchIterator_Create` / `SchIterator_Destroy`: a sheet iterator filtered to `SCH.TObjectId.eWire`, and inside it each wire's own iterator filtered to `SCH.TObjectId.eParameter`. Destroy both, the inner one included.
- `SCH.ISch_ComplexText.IsHidden`, `SCH.ISch_GraphicalObject.EnableDraw`, `SCH.ISch_GraphicalObject.Selection`, `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: unhide, draw and select a child object, then redraw it.
- `SCH.ISch_RobotManager.SendMessage` (`SCHMBeginModify` / `SCHMEndModify`): bracket the `IsHidden` change, as the schematic guide shows for modifying an object.
- `DXP.NotificationHandler` (its constructor registers it with the client) and `DXP.IClient.UnregisterNotificationHandler`: receive AD's notifications in .NET, where `ServerModule.ReceiveNotification` cannot be overridden.
- `DXP.NotificationType.DocumentNotification` and `DXP.NotificationCode.DocumentNotificationCode.DocumentActivating`: the .NET forms of `cDocumentNotification` / `cDocumentActivating`.
- `DXP.IClient.CurrentView`, `DXP.IServerDocument.GetKind`, `GetViewByName`, `AddView`: attach a `DXP.ServerPanelView` to one document.
- `DXP.IClient.IsDocumentOpen`: forget the views of documents that were closed, so a reopened sheet gets a new view.
- `DXP.ServerPanelForm` / `DXP.ServerPanelView` and `DXP.IGUIManager.SetPanelVisibleInCurrentForm`: the sample's global panel.

## Run it
The extension has a panel and a notification handler: deploy it with `tools/deploy.ps1 -Sample SchWireParameterPanel`
and restart Altium Designer before the first run.

1. Open a schematic sheet whose wires carry parameters (for example one made with the WireAddParameter sample) and focus it.
2. **SchWireParameterPanel › Show Wire Parameters**, or `tools/ad.ps1 -Sample SchWireParameterPanel run ShowWireParameters`
3. The result has the format `SL_Power.SchDoc: 1 wire parameter(s) on 1 wire(s); unhidden 1, draw enabled 0, selected 1.`,
   and hidden parameters are drawn again on the sheet.
4. For the automatic behaviour: **SchWireParameterPanel › AutoShow On**, edit the sheet, then **AutoShow Status** to see which notifications arrived. Turn it off with **AutoShow Off**.

| Command | Parameters | Result |
|---|---|---|
| `ShowPanel` | none | Opens the panel: the sheet's wire parameters, the AutoShow state, buttons for the commands |
| `ListWireParameters` | `Max=<n>` (default 50) | One line per wire parameter: wire index (among all the sheet's wires), name=value, hidden, draw, selected |
| `ShowWireParameters` | `Select=true\|false` (default true) | Unhides, enables drawing and (unless `Select=false`) selects every wire parameter; returns the counts of changes |
| `AutoShow` | `State=on\|off\|status` (default status) | Switches the automatic mode, or reports it: attached views, notifications received by source/type/code, the last handler error |

## How it works
`Commands/SchWireParameterPanelCommands.cs` holds the commands. `Collect` walks the sheet with
`ISch_Document.SchIterator_Create` (filter `eWire`) and, for each wire, the wire's own `SchIterator_Create` (filter
`eParameter`), destroying each iterator in a `finally`. `ShowWireParameters` changes only the properties that
differ: `IsHidden` inside `SCHMBeginModify` / `SCHMEndModify` robot-manager messages, then `EnableDraw`,
`Selection` and `GraphicallyInvalidate`, and marks the sheet's server document modified when something was unhidden.

The automatic mode lives in the shim, `SchWireParameterPanel/WireViewShim.cs`, because AD calls those objects back.
`AutoShow State=on` reaches it through a delegate in AppDomain data. It creates a `DXP.NotificationHandler`,
whose constructor registers it with the client, and attaches the holder view to the focused schematic at once. On each
`DocumentActivating` document notification, the handler takes `IClient.CurrentView`, its owner document, checks
`GetKind() == "SCH"`, asks the document with `GetViewByName` and, if that finds nothing or throws, the shim's own
file-to-view map, then adds a new `SchHolderView` with `IServerDocument.AddView`. On every document notification,
before each attach and before `AutoShow State=status` lists the views, the map drops the files that
`IClient.IsDocumentOpen` no longer reports, so a sheet that is closed and reopened is attached again. The view
overrides `ReceiveNotificationImpl`. It counts every notification, and on a view notification with the EndUpdate
code (7; the older code 6 is still accepted) it runs `ShowWireParameters`, guarded against re-entry by an
in-process flag and ignoring notifications that arrive within 750 ms after a run (the run's own changes
raise view codes 2 and 7 again while it runs; the in-process flag drops those), then appends the result to its text box. `AutoShow State=off` unregisters the handler and
makes the attached views do nothing.

The global panel is the framework's (`framework/shim/Panel.cs`). Its content, `Ui/PanelContent.cs`, hot-reloads and
calls the commands through `SamplePanel.Invoke`.

## Notes
- **AutoShow is off by default.** Notification handlers declared as classes of their own made Altium Designer show
  "Member not found" error dialogs, so this sample registers nothing when it starts; only `AutoShow State=on` does.
  The SDK's `DXP.NotificationHandler` is called without any such dialog, and `DocumentActivating` arrives as type
  `altium.dxp.document`, code 3.
- No hook unregisters the handler when AD shuts down: turn AutoShow off before closing AD.
- `IServerDocument.AddView` accepts the .NET `ServerPanelView`, and AD's view notifications (type `altium.dxp.view`,
  codes 0, 2, 3 and 7) reach `ReceiveNotificationImpl`.
- The EndUpdate code is 7, not 6. Code 6 (`cViewNotification_EndUpdate = 6`) is not a .NET constant (in
  `DXP.NotificationCode.ViewNotificationCode`, 6 is `ApplicationStartupComplete`), and AD does not send it to a
  per-document view. An edit made through the API raises code 2 once, from `SendMessage` with `SCHMEndModify`, and code 7
  once per `GraphicallyInvalidate` call (the redraw). Both arrive synchronously, on the stack of the code that edits.
  A save raises code 20 twice. The in-process flag drops the notifications the automatic run raises itself, so one
  edit gives exactly one automatic run and no loop. `AutoShow State=status` lists every code received.
- **`Selection` on a wire parameter is not retained**: `SetState_Selection(true)` reads back `false`, while
  `IsHidden` on the same object sticks and the owning wire's selection sticks. So `ShowWireParameters` reports the
  same `selected` count on every run and the parameters never appear selected.
- `IServerDocument.GetViewByName` does not find the holder panel view (it returns null), so the shim's own map is
  what prevents a duplicate view: `AutoShow State=on` run twice on the same sheet answers
  "already attached (shim list)".
- The command marks the server document modified itself when it unhid something.
- `ShowWireParameters` changes only what differs, so a run raised by its own changes changes nothing, besides the
  shim's re-entry guard.
- Views cannot be removed in .NET (no `RemoveView`): an attached view stays until its document closes.
- Limitation: if a sheet is closed and reopened while AutoShow is off, with no `AutoShow` command run while it was
  closed, the shim's map still lists it, and `AutoShow State=on` answers "already attached (shim list)" instead of
  attaching a new view. `AutoShow State=status` or `State=on` run while the sheet is closed prunes it.
