# Report routed lengths of the xSignals in each class

Lists every xSignal class of the current PCB board with the xSignals (pin pairs) it contains, their node count and
their total, routed and unrouted lengths. It shows how to reach the pin pair manager, find xSignal classes with a
board iterator, and test class membership by name.

## What it shows
- `PCB.IPCB_Board.PinPairsManager`: the board's manager of pin pairs (xSignals).
- `PCB.IPCB_PinPairsManager.InvalidateAll`: recalculates out-of-date pin pairs. `NodeCount` doesn't recalculate on
  its own, so call it before reading.
- `PCB.IPCB_PinPairsManager.PinPairsCount` / `PCB.IPCB_PinPairsManager.PinPairs`: index-based access to the pin pairs.
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eClassObject`: iterate the board's object classes.
- `PCB.IPCB_ObjectClass.MemberKind` = `PCB.TClassMemberKind.eClassMemberKind_Signal`: the class is an xSignal class.
- `PCB.IPCB_ObjectClass.IsMember`: membership is tested with the pin pair's `PCB.IPCB_PinPair.Name`.
- `PCB.IPCB_ObjectClass2.DisplayName`: the class name as shown in AD (`<All xSignals>` for the super class).
- `PCB.IPCB_PinPair.NodeCount`, `PCB.IPCB_PinPair.Length`, `PCB.IPCB_PinPair.RoutedLength`,
  `PCB.IPCB_PinPair.UnroutedLength`: lengths in internal units (`EDP.EDPConstant.InternalUnits` per mil).
- Setup helper: `PCB.IPCB_PinPairsManager.CreateFromPinsDescriptors` and `PCB.IPCB_PinPairsManager.AssignPinPairClass`
  create an xSignal and put it into a class.

## Run it
1. Open a `.PcbDoc` with xSignal classes and make it the active document. The board of the SpiritLevel-SL1 example
   project that ships with Altium Designer has no xSignals of its own; it works after
   **XSignalLengths › Add Test XSignal**, which creates one xSignal in the class `XSignalLengths_Test`.
2. **XSignalLengths › Get XSignal Info**, or `tools/ad.ps1 -Sample XSignalLengths run GetXSignalInfo`
3. The result starts with `<board>: <n> xSignal classes, <m> class members listed, <k> xSignals on the board.`, then
   one `xSignal Class : <name>` line per class and one indented line per xSignal with its node count and lengths, for
   example `Signal Length : 1234.5 mil (12345000)`.

| Command | Parameters | Result |
|---|---|---|
| `GetXSignalInfo` | `Out=<file or folder>` (optional), `Open=true` (optional: write the report, to a temporary file when `Out` is not given, and open it in AD) | The report as text; with `Out`/`Open` also `Report written to <path>` |
| `AddTestXSignal` | `Pins=<U1-1,U2-3>` (optional; default: two pads of the first net that joins two components), `Class=<name>` (default `XSignalLengths_Test`) | `Created xSignal '<name>' between <pins> in class '<class>'.` |

## How it works
`XSignalLengthsCommands.cs`: `GetXSignalInfo` gets the current board, its `PCB.IPCB_PinPairsManager`, and calls
`InvalidateAll`. A board iterator filtered to `eClassObject` (all layers, `eProcessAll`) walks the object classes and
keeps those whose `MemberKind` is `eClassMemberKind_Signal`. For each class it loops over all pin pairs of the
manager and lists those for which `IsMember(pinPair.GetState_Name())` is true. The iterator is destroyed in a
`finally`. Lengths are printed in mils with the raw internal value next to them; -1 is shown as "no signal path".
`Out` writes the same lines to a file. `Open` (also without `Out`, to a temporary file) opens it as a text document.

`AddTestXSignal` builds a pin descriptor string (`U1-1|U2-3`), either from `Pins` or from the first net with pads on
two components (`PCB.IPCB_Pad.PinDescriptorString`). It calls `CreateFromPinsDescriptors` and passes the new pin pair
to `AssignPinPairClass` in a `DXP.IInterfaceList` from `PCB.IPCB_ContourUtilities.CreateInterfaceList`, all between
`PreProcess` and `PostProcess`.

## Notes
- Lengths are `long` values in internal units (10,000 per mil), and `Length` = `RoutedLength` + `UnroutedLength`
  (on a routed pin pair: 6100452 = 610.045 mil, close to the net's own routed length). A pin pair
  with no signal path, such as one on a supply net connected through a plane, returns -1 for `Length` and
  `UnroutedLength` and 0 for `RoutedLength`.
- A super class (`<All xSignals>`) answers `IsMember` with true for every name, so it lists every xSignal.
- Pins are passed with `,` because `|` separates the command parameters. The command converts them back to the
  `|`-separated descriptor string the API expects.
- `AddTestXSignal` changes the board in memory. Close the document without saving to discard it. A
  `DXP.IInterfaceList` from `PCB.IPCB_ContourUtilities.CreateInterfaceList` is accepted by `AssignPinPairClass`.
  Open point: whether the change can be undone. On the SpiritLevel board the default pins land on the VCCO supply
  net, so the test xSignal shows -1 lengths. Pass `Pins=` for a routed signal net to see real lengths, for example
  `Pins=U5-3,U1-155` (CCLK).
- The class iterator uses all layers, like the docs' example for `PCB.IPCB_ObjectClass.SuperClass`. A filter on
  `eV6_MultiLayer` finds the same classes.
