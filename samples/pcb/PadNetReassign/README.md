# Move all pads to one net with the group warehouse calls

Moves every pad of the current PCB board to one net and reports how many pads ended on it. It shows the sequence
the PCB editor needs to move a primitive between nets: `CancelGroupWarehouseRegistration` on the old net, a new
`Net`, `RegisterWithGroupWarehouse` on the new net, inside `BeginModify`/`EndModify` and `PreProcess`/`PostProcess`.
Optionally it also adds a parameter to the focused project.

## What it shows
- `PCB.IPCB_Net.CancelGroupWarehouseRegistration`: the old net releases the pad (its `InNet` becomes false).
- `PCB.IPCB_Primitive.Net`: `SetState_Net` assigns the new net.
- `PCB.IPCB_Net.RegisterWithGroupWarehouse`: the new net takes the pad. It adds nothing while the pad's `InNet` is
  still true, which is why the cancel call comes first.
- `PCB.IPCB_Primitive.BeginModify` / `PCB.IPCB_Primitive.EndModify`: bracket each pad's change.
- `PCB.IPCB_ServerInterface.PreProcess` / `PCB.IPCB_ServerInterface.PostProcess`: bracket the whole bulk change; always
  paired (here in a `finally`).
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eNetObject` and `PCB.TObjectId.ePadObject`: find the
  target net and the pads.
- `EDP.IWorkspace.DM_FocusedProject`, `EDP.IProject.DM_AddParameter`, `EDP.IProject.DM_ProjectFullPath`,
  `DXP.IClient.GetDocumentByPath`, `DXP.IServerDocument.Modified`: add a project parameter and mark the project
  document modified (opt-in).
- `EDP.IProject.DM_ParameterCount` / `EDP.IProject.DM_Parameters` (both inherited from `EDP.IDMObject`) with
  `EDP.IParameter.DM_Name` and `EDP.IParameter.DM_Value`: read the parameter back to confirm it was added.

## Run it
1. Open a copy of a `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document. For `AddProjectParam`, its project must be the focused project. The
   command changes every pad: never run it on a design you want to keep, and close the document without saving
   afterwards.
2. **PadNetReassign › Update Pad**, or `tools/ad.ps1 -Sample PadNetReassign run UpdatePad "Net=GND"`
3. The result reads `<board>.PcbDoc: <p> pads moved to net 'GND' (<g> were already on it); <q> of <p> pads are now
   on 'GND'.` **PadNetReassign › List Pad Nets** (`tools/ad.ps1 -Sample PadNetReassign run ListPadNets`) then
   shows `GND : <q> pads`.

| Command | Parameters | Result |
|---|---|---|
| `UpdatePad` | `Net=<name>` (optional; default: the first net the iterator returns), `AddProjectParam` (flag), `ParamName=<name>` (default `testParam1`), `ParamValue=<value>` (default `1234`), `Redraw` (flag) | Pads moved and pads now on the net; with `AddProjectParam` a line `Project parameter <name>=<value> added to <project> (read back: '<value>'; ...)` |
| `ListPadNets` | `Top=<n>` (optional, default 10) | `<p> pads on <n> nets, <k> pads without a net.`, then the nets with the most pads |

## How it works
`PadNetReassignCommands.cs`: `UpdatePad` gets the current board. With `AddProjectParam` it resolves the focused
project first (`DXP.IClient.GetDXPWorkspace` as `EDP.IWorkspace`, then `DM_FocusedProject`), so a missing project
fails before the board is touched. `FindNet` walks an `eNetObject` iterator and returns the net named by `Net`, or
the first net. `CollectPads` walks an `ePadObject` iterator into a list; both iterators are destroyed in a `finally`.

Between `PreProcess` and `PostProcess`, each pad gets `BeginModify`, `CancelGroupWarehouseRegistration` on its
current net (if any), `SetState_Net(target)`, `RegisterWithGroupWarehouse` on the target net, and `EndModify`. Then
the command reads each pad's `Net` back and counts those on the target net. `Redraw` calls
`PCB.IPCB_Board.ViewManager_FullUpdate`. With `AddProjectParam`, `DM_AddParameter` adds the parameter, the project
document from `GetDocumentByPath(DM_ProjectFullPath())` gets `SetModified(true)`, and the value is read back with
`DM_ParameterCount` / `DM_Parameters`.

`ListPadNets` groups the pads by `Net.Name` and lists the largest nets.

## Notes
- Destructive: every pad is moved, so the board's connectivity is gone. The change stays in memory until saved.
- Per the reference, `RegisterWithGroupWarehouse` clears `Net` instead of adding a primitive that is not on a signal
  layer, the multi-layer or the connection layer. The read-back count shows if any pad was left without the net. On
  the SpiritLevel-SL1 board this does not happen for any of its 642 pads, including the 124 that had no net before.
- Pads already on the target net go through the same cancel/register pair and stay on it: running the command a
  second time on the same net leaves all pads there.
- `PostProcess` updates the board's views, per the pcb-basics guide. `Redraw` additionally calls
  `ViewManager_FullUpdate`.
- Undo: `BeginModify` notifies the board's undo history, per the pcb-basics guide.
- The default target net is simply the first net the iterator returns, which can be any net (on the SpiritLevel-SL1
  board it is `NetR12_2`). Pass `Net=` for a predictable result.
- `DM_AddParameter` has no reference description. It appends: called with a name the
  project already has, it creates a second parameter with that name, and the new entry comes first in
  `DM_Parameters`. The read-back therefore takes the first match. Not known: whether it marks the project document
  modified on its own.
- The getter of `IServerDocument.Modified` is `GetModified()`.
- The extension methods `GetDXPWorkspace` and `GetDocumentByPath` need `using DXP;`.
