# Panel view on PCB documents listing selected primitives

Adds a panel view of its own to a PCB document with `DXP.IServerDocument.AddView` and reacts to notifications: on a
view notification it lists the `ObjectIDString` of every selected primitive; on a document activation it can attach
the view to the activated PCB. It teaches per-document panel views, view-level `ReceiveNotification`, and a
client notification handler built on the SDK's `DXP.NotificationHandler`.

## What it shows
- `DXP.ServerPanelForm` / `DXP.ServerPanelView`: a per-document panel (a form with a read-only text box, wrapped in a view).
- `DXP.BaseServerView.ReceiveNotification`: sealed in .NET; a derived view overrides the protected
  `ReceiveNotificationImpl(INotification)` instead (compiler: CS0506 on an override of `ReceiveNotification`).
- `DXP.IServerDocument.AddView`, `DXP.IServerDocument.GetViewByName`, `DXP.IServerDocument.GetKind`: attach the
  view once to a document of kind `PCB`.
- `DXP.IClient.CurrentView`, `DXP.IServerDocumentView.GetOwnerDocument`: find the document to attach to.
- `DXP.NotificationHandler` with `DXP.IClient.RegisterNotificationHandler` / `UnregisterNotificationHandler`: the
  SDK's handler class, which calls a `DXP.NotificationHandlerCallback`; it stands in for an override of
  `DXP.ServerModule.ReceiveNotification`, which is not virtual in .NET.
- `DXP.NotificationType.DocumentNotification` with `DXP.NotificationCode.DocumentNotificationCode.DocumentActivating`,
  and `DXP.NotificationType.ViewNotification` with the view codes 3 and 42.
- `PCB.IPCB_Board.BoardIterator_Create` with `SetState_FilterAll`, `AddFilter_AllLayers` and `eProcessAll`, then
  `PCB.IPCB_Primitive.Selected` and `PCB.IPCB_Primitive.ObjectIDString`: a walk over every primitive.

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample PcbSelectionPanel` and Altium Designer restarted (it
has shim code and two `PanelInfo` blocks).

1. Open a `.PcbDoc`, for example the board of an example project that ships with Altium Designer, such as
   SpiritLevel-SL1, and focus it. Work on a copy: the selection may be saved with the document.
2. **PcbSelectionPanel › Select 3 Pads (Test)**, then **Show Selection**, or
   `tools/ad.ps1 -Sample PcbSelectionPanel run ShowSelection`.
3. The result reads `<board>.PcbDoc: 3 selected primitive(s) (board selection list: <k>)` followed by the IDs and
   their counts. **Show Panel** opens the panel with the log; **Notifications On** logs every selection change there.

| Command | Parameters | Result |
|---|---|---|
| `ShowSelection` | none | `<board>: <n> selected primitive(s) (board selection list: <k>)` and one line per ObjectIDString with its count |
| `AttachView` | none | `AttachView: attached: <file>.`, `already attached: <file>`, `not a PCB document: ...` or `no document: ...` |
| `Listen` | `State=on\|off\|status` (default `status`), `AutoAttach=true` (with `on`), `Reset=true` | what it did, then the status: handler state, attached views, notifications received by source/type/code, log size, last error |
| `SelectTest` | `Kind=<TObjectId name, default Pad>`, `Count=<n, default 3>`, or `Clear=true` | `Selected <n> <kind> primitive(s) on <board>.` / `Deselected <n> primitive(s) on <board>.` |
| `Panel.Show` | none | opens the sample's panel (log and a **Refresh selection** button) |
| `Notification.View`, `Notification.Document` | set by the shim | called by the shim only; fail when run by a user |

`SelectTest` is a helper: it selects primitives from code so the rest can be tried
without clicking in the editor.

## How it works
- `PcbSelectionPanel/SelectionShim.cs` (shim, needs a redeploy and restart): the static constructor of
  `PluginServerModule` publishes the switches as delegates in AppDomain data, because the framework's `Panel.cs`
  already uses the `RegisterViews` partial method. `SelectionShim.SetListening` creates a `DXP.NotificationHandler`
  and registers it with the client; `SelectionShim.AttachView` reads `IClient.CurrentView`, its owner document, checks
  `GetKind() == "PCB"` and the shim's list of attached files (then `GetViewByName`), and calls `AddView` with a new
  `PcbHolderView`. `SelectionShim.Dispatch` counts every notification and forwards view codes 3 and 42 to
  `Notification.View` and `DocumentActivating` to `Notification.Document`. `PcbHolderView.ReceiveNotificationImpl`
  dispatches the same way and appends the returned lines to its own text box.
- `PcbSelectionPanel.Impl/Commands/PcbSelectionPanelCommands.cs`: `SelectedObjectIds` is the board
  loop; `Notification.View` logs `<time> <type> code <n> (<source>): <k> selected` plus the IDs;
  `Notification.Document` attaches the view when AutoAttach is on; `Listen` reports the counters, naming codes with
  `NotificationCode.ViewNotificationCode.ToString` / `DocumentNotificationCode.ToString`.
- `PcbSelectionPanel.Impl/Ui/PanelContent.cs`: the global panel's content (hot-reloads), showing the log kept in
  AppDomain data.

## Notes
- Nothing listens until **Notifications On** (`Listen State=on`), and no view is attached until **Attach View** (or
  AutoAttach). An extension's own `INotificationHandler` class can make Altium Designer show "Member not found"
  error dialogs. The SDK's `DXP.NotificationHandler` is called by AD and raises no such
  dialog.
- Code 3 is `cViewNotification_SelectionChanged`. A selection change raises it, also
  when the selection is made from code through `IPCB_Primitive.Selected` (one notification per primitive), and that
  also updates `IPCB_Board.SelectecObjectCount`. The .NET constants `NotificationCode.ViewNotificationCode` name 3
  `DocumentDataRefresh` and have no 42 (`cViewNotification_UpdateViews`); `ViewNotificationCode.ToString(3)` prints
  "Document Opening". Codes 4 and 59 also arrive on the view and have no constant. The status shows what arrives.
- The sealed `BaseServerView.ReceiveNotification` calls `ReceiveNotificationImpl`, and `AddView`
  accepts a .NET `ServerPanelView`. At `DocumentActivating` the current view is already the activated document's,
  so AutoAttach logs `not a PCB document: ... is of kind 'SCH'` for a schematic and `already attached: ...PcbDoc`
  for the board.
- The two notification paths are complementary: the attached view gets the document's view notifications
  (`altium.dxp.view`), while the client handler gets only `altium.dxp.document` and `altium.dxp.documentform` ones.
  With the handler on and a view attached, the log tags each line with the path AD called (`handler` or `view`); only
  document notifications reach both and appear twice. A module-level handler alone would not see the selection
  changes a view sees.
- The .NET API has no `RemoveView`: an attached view stays until its document is closed, and keeps receiving
  notifications after the client handler is unregistered.
- Known limitation: the shim remembers attached files for its whole lifetime. After a PCB is closed and reopened
  under the same path, `AttachView` answers `already attached` although the reopened document has no view; restart
  Altium Designer to attach again.
- With the handler on, every code-3 notification walks the whole board: slow on large boards.
- `SelectTest` changes the selection only (no undo step). Selection may be saved with the document: close the
  copy without saving.
