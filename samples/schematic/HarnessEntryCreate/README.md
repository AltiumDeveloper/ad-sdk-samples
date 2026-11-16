# Add a harness entry to a harness connector

Adds a new harness entry to the first harness connector on the current schematic sheet and reads it back. It shows
how to create a child object with the schematic object factory and add it to its owner object (the connector)
rather than to the sheet.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the focused sheet; a library is rejected by its `SCH.ISch_BasicContainer.ObjectId`.
- `SCH.ISch_BasicContainer.SchIterator_Create` with `SCH.ISch_Iterator.AddFilter_ObjectSet` (`SCH.TObjectId.eHarnessConnector`):
  find the harness connectors on the sheet; the iterator is destroyed with `SCH.ISch_BasicContainer.SchIterator_Destroy` in a `finally`.
- `SCH.ISch_ServerInterface.SchObjectFactory` (`SCH.TObjectId.eHarnessEntry`, `SCH.TObjectCreationMode.eCreate_Default`): create the entry.
- `SCH.ISch_BasicContainer.AddSchObject`: a harness connector is a container; the entry is added to it, not to the sheet.
- `SCH.ISch_HarnessEntry.DistanceFromTop`, `SCH.ISch_HarnessEntry.Name`, `SCH.ISch_HarnessEntry.Side`: position and name the entry.
- `SCH.ISch_GraphicalObject.SetState_xSizeySize`: recalculate the entry's size after its properties are set.
- `SCH.ISch_RobotManager.SendMessage` (`SCHMBeginModify` / `SCHMEndModify` on the connector) inside
  `DXP.IProcessControl.PreProcess` / `DXP.IProcessControl.PostProcess`: tell the editor about the change.
- `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: redraw the connector.

## Run it
1. Open a project with harness connectors, for example the Bluetooth Sentinel example project that ships with
   Altium Designer, and focus a sheet that has a harness connector. In that project `Visual_LEDx6.SchDoc` has
   exactly one connector (`LEDx6`, 6 entries) and `Microcontroller_STM32F101.SchDoc` has several;
   `ListHarnessEntries` tells you for any sheet.
2. **HarnessEntryCreate › Create Harness Entry**, or `tools/ad.ps1 -Sample HarnessEntryCreate run CreateHarnessEntry "DistanceFromTop=20|Name=PIPE"`
3. The result names the sheet and the connector, the entry as read back (`Name`, `Side`, `DistanceFromTop`) and the
   connector's entry count before and after (one more). The sheet is modified in memory; close it without saving.

| Command | Parameters | Result |
|---|---|---|
| `CreateHarnessEntry` | `DistanceFromTop=<n>` (default 20), `Name=<text>` (default: none), `Side=Left\|Right\|Top\|Bottom` (default: factory default) | `<sheet>: added a harness entry to connector '<type>' at (x, y) mil; read back: Name=... Side=... DistanceFromTop=...; Entries on the connector n -> n+1.` |
| `ListHarnessEntries` | none | `<sheet>: <n> harness connector(s).` then one line per connector and one per entry (read-only) |

## How it works
`HarnessEntryCreateCommands.cs`: `CurrentSheet` gets the schematic server and the current document and rejects
libraries. `FindConnectors` iterates the sheet with an `eHarnessConnector` filter; `Entries` iterates one connector
with an `eHarnessEntry` filter (a connector is iterated like a sheet). `CreateHarnessEntry` takes the first
connector, opens `PreProcess` on the sheet, creates the entry with `SchObjectFactory`, and between
`SCHMBeginModify` and `SCHMEndModify` on the connector adds it with `AddSchObject`, sets `DistanceFromTop` (then
`Name` and `Side` if given) and calls `SetState_xSizeySize`. After `PostProcess` it redraws the connector with
`GraphicallyInvalidate` and returns the entry's properties read back and the entry count before and after.
`ListHarnessEntries` prints the connectors (harness type text and location) and their entries.

## Notes
- `DistanceFromTop` is in internal schematic coordinates (10000 per mil, the same unit as
  `SCH.ISch_GraphicalObject.Location`), measured down from the top of the connector; one entry row is 100 mil, that
  is `1000000`. The default of 20 is only 0.002 mil, so every entry created that way
  sits flush with the connector's top edge and they overlap. Pass `DistanceFromTop=1000000` times the row you want.
- The change is wrapped in `SCHMBeginModify`/`SCHMEndModify` on the connector, plus `PreProcess`/`PostProcess`
  on the sheet; `SCHMPrimitiveRegistration` is not sent for the entry. This bracket adds the entry, redraws it
  and marks the sheet modified. Which robot messages a new child object needs is not documented.
- Without `Name` the entry keeps the factory default, which is the string `0` (not empty), with `Side=eRightSide`.
- Each run adds another entry; entries with the same `DistanceFromTop` overlap exactly. New entries come last in
  iterator order (insertion order, not sorted by position).
- Only the focused sheet is searched (`GetCurrentSchDocument`), and only its first connector in iterator order.
- No connector on the sheet, or no schematic document, gives an error message.
