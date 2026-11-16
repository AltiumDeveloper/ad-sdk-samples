# Count the pads of the current board

Counts every pad on the current PCB board and returns the number with the board's file name. It shows how to set
up a board iterator with an object filter, a layer filter and an iteration method, walk it, and destroy it.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document, or none.
- `PCB.IPCB_Board.FileName`: the full path of the board's document, read with `GetState_FileName`.
- `PCB.IPCB_Board.BoardIterator_Create` / `PCB.IPCB_Board.BoardIterator_Destroy`: the board owns the iterator;
  destroy takes it by `ref` and is called in a `finally` block.
- `PCB.IPCB_AbstractIterator.AddFilter_ObjectSet`: only pads, with `new PCB.TObjectSet { PCB.TObjectId.ePadObject }`.
- `PCB.IPCB_AbstractIterator.AddFilter_AllLayers`: search all layers, so that both multi-layer (through-hole) and
  single-layer (SMD) pads are found.
- `PCB.IPCB_AbstractIterator.AddFilter_LayerSet_2` with `PCB.PCBConstant.AllLayersSet`: another way of
  asking for all layers, available as `LayerFilter=Partition` for comparison.
- `PCB.IPCB_BoardIterator.AddFilter_Method` with `PCB.TIterationMethod.eProcessAll`: free pads and component pads.
- `PCB.IPCB_AbstractIterator.FirstPCBObject` / `NextPCBObject`: step through the matches until `null`.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document.
2. **BoardPadCount › Count Pads**, or `tools/ad.ps1 -Sample BoardPadCount run CountPads`
3. The result is `<file>.PcbDoc: <N> pad(s) (LayerFilter=All)`. The board is not changed.

| Command | Parameters | Result |
|---|---|---|
| `CountPads` | `LayerFilter=All` (default), `Partition` or `None` (optional) | `<file>: <N> pad(s) (LayerFilter=<filter>)` |

## How it works
`BoardPadCountCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`; a `UserException` covers the
cases where no PCB editor or no PCB document is active. It creates a board iterator and sets three filters: the
object set (pads), the layers (by default `AddFilter_AllLayers`) and the method (`eProcessAll`). It counts the
objects returned by `FirstPCBObject` / `NextPCBObject`, logging progress every 1000 pads, and destroys the iterator
in a `finally` block. `LayerFilter=Partition` replaces the layer filter with
`AddFilter_LayerSet_2(PCBConstant.AllLayersSet)`; `LayerFilter=None` sets no layer filter at all.

## Notes
- Read-only: nothing on the board is modified, so no `PreProcess`/`PostProcess` bracket is needed.
- There is no `Release()` call per primitive: in .NET the runtime manages the COM references. The iterator
  itself must still be destroyed.
- A new board iterator already searches the layers pads live on: on the SpiritLevel board `LayerFilter=None`
  counts the same 642 pads as `All`.
- `AddFilter_LayerSet_2` with a `PCB.TLayerPartitionSet` is a working layer filter: with
  `PCB.PCBConstant.AllLayersSet` it returns the same 642 pads as `AddFilter_AllLayers`; on the same board
  `V7SignalLayersSet` returned 523 pads, `V7MultiLayersSet` 119, `V7SilkscreenLayersSet` 0 and an empty set nothing.
