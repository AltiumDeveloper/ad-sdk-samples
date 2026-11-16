# Read and toggle the Documents Bar grouping mode

Reads how the Documents Bar groups the open documents (by document kind or by project), changes it, and
broadcasts a "system preferences changed" notification. It shows the client-level UI API
`DXP.IClientAPI_Interface` and how to send a system notification from an extension.

## What it shows
- `DXP.GlobalVars.Client` and `DXP.IClient.GetClientAPI`: the route from the client to the client UI API
  (`GetClientAPI` is a typed helper, an extension method on `IClient`).
- `DXP.IClientAPI_Interface.GroupingInDocumentsBar`: read with `GetGroupingInDocumentsBar`, written with
  `SetGroupingInDocumentsBar`.
- `DXP.TDocumentsBarGrouping`: `dbgNone` (0), `dbgByDocKind` (1), `dbgByProject` (2).
- `DXP.IClient.BroadcastNotification` with a `DXP.SystemNotification` built from the int constant
  `DXP.NotificationCode.SystemNotificationCode.SystemPreferencesChanged`.

## Run it
1. No document is needed. To see the effect, open two or more documents from different projects.
2. **DocumentsBarGrouping › Set Grouping In Documents Bar**, or
   `tools/ad.ps1 -Sample DocumentsBarGrouping run SetGroupingInDocumentsBar "Mode=Toggle"`
3. The command returns the old and new mode, for example
   `Documents Bar grouping changed from by document kind (dbgByDocKind) to by project (dbgByProject).`
   Run it again to switch back. The grouping is a user preference: restore the value you started with.

| Command | Parameters | Result |
|---|---|---|
| `GetGroupingInDocumentsBar` | none | `Documents Bar grouping: <mode>` |
| `SetGroupingInDocumentsBar` | `Mode=Toggle` (default), `ByDocKind`, `ByProject` or `None` | `Documents Bar grouping changed from <old> to <new>.` |

An unknown `Mode` fails with `Unknown Mode '<value>'` and leaves the grouping unchanged.

## How it works
`DocumentsBarGroupingCommands.cs` gets `IClientAPI_Interface` from `DXP.GlobalVars.Client` with
`GetClientAPI()`. `GetGroupingInDocumentsBar` returns the current `TDocumentsBarGrouping`. `SetGroupingInDocumentsBar`
works out the new value from `Mode`. `Toggle`: by document kind becomes by project, by project
becomes by document kind, anything else (`dbgNone`) becomes by project. It calls `SetGroupingInDocumentsBar`, then
broadcasts `new SystemNotification(SystemNotificationCode.SystemPreferencesChanged)` through
`IClient.BroadcastNotification`, and returns the old value and the value read back.

## Notes
- `IClient.BroadcastNotification` has an `object` overload on the interface and an `INotification` extension
  overload. C# picks the interface method; both accept a `SystemNotification`.
- `SystemNotification` takes an `int` code. The codes are constants of `DXP.NotificationCode.SystemNotificationCode`.
- The broadcast is meant to make subscribers such as the Documents Bar refresh. With all document tabs fitting in
  one row, the tabs look the same under both values right after the set and the broadcast.
