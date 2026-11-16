# Create an xSignal between two component pins

Creates an xSignal (pin pair) between two component pins on the current PCB board from a string of pin descriptors
such as `U1-16|U4-23`. It shows the pin pair manager's `CreateFromPinsDescriptors`, and how to list and delete pin
pairs to check the result.

## What it shows
- `PCB.IPCB_Board.PinPairsManager`: the board's manager of pin pairs (xSignals).
- `PCB.IPCB_PinPairsManager.CreateFromPinsDescriptors`: creates a pin pair from `<designator>-<pad>` descriptors
  separated by `|` and returns it (null for an empty descriptor).
- `PCB.IPCB_PinPairsManager.InvalidateAll`: recalculates out-of-date pin pairs. A new pin pair is out of date, and
  `NodeCount` does not recalculate it on its own.
- `PCB.IPCB_PinPairsManager.PinPairsCount` / `PCB.IPCB_PinPairsManager.PinPairs`: index-based access to the pin pairs.
- `PCB.IPCB_PinPairsManager.Delete`: removes a pin pair from the manager and the board.
- `PCB.IPCB_PinPair.Name`, `PCB.IPCB_PinPair.NodeCount`, `PCB.IPCB_PinPair.Length`: the pin pair's generated name
  (net name, `_PP`, number), node count and length in internal units.
- `PCB.IPCB_ServerInterface.PreProcess` / `PCB.IPCB_ServerInterface.PostProcess` around the board changes.

## Run it
1. Open a `.PcbDoc` and make it the active document, for example the board of the SpiritLevel-SL1 example project
   that ships with Altium Designer.
2. **CreateXSignal › Create XSignal** (uses the default pins `U1-16,U4-23`), or
   `tools/ad.ps1 -Sample CreateXSignal run CreateXSignal "Pins=U5-3,U1-155"`
3. The result is `Created xSignal 'CCLK_PP1' between U5-3,U1-155, Node Count : 2, xSignals on the board: 0 -> 1.
   Close the document without saving to discard it.` (name and counts depend on the board). `ListXSignals` then
   lists it, `DeleteXSignal "Name=CCLK_PP1"` removes it.

| Command | Parameters | Result |
|---|---|---|
| `CreateXSignal` | `Pins=<Des-Pad>,<Des-Pad>` (default `U1-16,U4-23`) | `Created xSignal '<name>' between <pins>, Node Count : <n>, xSignals on the board: <k> -> <k+1>. Close the document without saving to discard it.` |
| `ListXSignals` | none | `<board>: <k> xSignals.` and one line per xSignal: name, node count, length |
| `DeleteXSignal` | `Name=<xSignal name>` (required, exact match) | `Deleted xSignal '<name>'. xSignals on the board: <k> -> <k-1>.` |

`ListXSignals` and `DeleteXSignal` are helpers. `DeleteXSignal` with a name that is not on the board fails with
`No xSignal named '<name>' on the board`.

## How it works
`CreateXSignalCommands.cs`: `CreateXSignal` gets the current board and its `PCB.IPCB_PinPairsManager`, turns the
`Pins` parameter into a descriptor string (`,` to `|`), and calls `CreateFromPinsDescriptors` and then
`InvalidateAll` between `PreProcess` and `PostProcess`. A null result raises an error; otherwise the command reports
the new pin pair's `Name` and `NodeCount` and the `PinPairsCount` before and after.

`ListXSignals` calls `InvalidateAll` and walks `PinPairs(0 .. PinPairsCount - 1)`. `DeleteXSignal` finds the pin pair
by `Name` in the same way and passes it to `Delete`.

## Notes
- Pins are passed with `,` because `|` separates the command parameters. A descriptor is a component designator,
  `-` and a pad name.
- The reference documents a null result only for an empty descriptor. For designators that are not on the board
  (`U99-1,U98-2`) it does **not** return null; it creates a pin pair named `_PP1` (empty net name) with Node Count 2
  and no signal path (length -1). Calling it again for the same pins creates a second pin pair (`CCLK_PP1`, then
  `CCLK_PP2`). So check the result's name and length rather than relying on null.
- Creating or deleting a pin pair changes the board in memory. `Delete` removes the pin pair at once. Close the
  document without saving to discard the changes.
- The default pins `U1-16|U4-23` exist on the SpiritLevel-SL1 board but give a one-node pin pair
  (`LCD_DB3_PP1`, length 0); on your board pass `Pins=`.
- Lengths are internal units (10,000 per mil); -1 means the pin pair has no signal path.
