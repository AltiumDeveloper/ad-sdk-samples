# Invalidate a pad's solder mask cache

Marks the solder mask expansion stored in the first pad's cache (or every pad's) as invalid and reports the cache
before and after. The invalid state is short-lived: Altium Designer revalidates that field in `PostProcess`, so
the `after:` line usually shows it valid again (see Notes). It shows the read-modify-assign pattern for
`PCB.V7_PadCache` and, as an alternative, the pad's own `InvalidateCache` method.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.ePadObject`, `PCB.PCBConstant.AllLayersSet` and
  `PCB.TIterationMethod.eProcessAll`: iterate the pads of the board on all layers.
- `PCB.IPCB_Pad.Cache`: `GetState_Cache` returns a `PCB.V7_PadCache` holding a copy of the pad's cache. A change
  takes effect only when it is assigned back with `SetState_Cache`.
- `PCB.V7_PadCache.SolderMaskExpansionValid` = `PCB.TCacheState.eCacheInvalid`: the field this sample changes.
  The enum's other values are `eCacheValid` and `eCacheManual`.
- `PCB.IPCB_Pad.InvalidateCache`: per the docs, marks the pad's cached design rule values out of date, without
  assigning a cache record (`Method=Invalidate`). Its exact effect on the cache fields is not documented.
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify`: the
  bracket for changing PCB objects.
- `PCB.IPCB_Primitive.SolderMaskExpansion`: the pad's solder mask expansion as the pad reports it, listed next to
  the cache value.

## Run it
1. Open a copy of a `.PcbDoc` with pads (the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer works) and make it the active document.
2. **PadCache › Read Pad Cache**, or `tools/ad.ps1 -Sample PadCache run ReadPadCache`, to see the first pad's cache.
3. **PadCache › Invalidate Pad Solder Mask Cache**, or
   `tools/ad.ps1 -Sample PadCache run InvalidatePadSolderMaskCache`.
4. The result names the board and lists the first pad's cache `before:` and `after:`. On the SpiritLevel-SL1 board
   the first pad is `Pad U5-11` with `SolderMaskExpansion=3.937 mil (39370)`. With the default `Method=Assign` the
   `after:` line shows the solder mask expansion state as `eCacheValid` again, with the value unchanged, because
   `PostProcess` revalidates it before the command reads the cache back. The fields the command does not touch are
   identical in both lines.

| Command | Parameters | Result |
|---|---|---|
| `InvalidatePadSolderMaskCache` | `All=True` (every pad, default: the first pad only), `Method=Assign` (default) or `Method=Invalidate` | Board name, number of pads changed, first pad's cache before and after |
| `ReadPadCache` | none | Board name and the first pad's cache fields (read-only) |

## How it works
`PadCacheCommands.cs`: both commands get the board from `PCBServer.GetCurrentPCBBoard` and collect pads with a
board iterator (`ePadObject`, all layers, `eProcessAll`), destroying the iterator in `finally`. `InvalidatePadSolderMaskCache`
records the first pad's cache, opens `PreProcess`, and for each pad calls `BeginModify`, then either reads
`GetState_Cache()`, sets `SolderMaskExpansionValid` to `eCacheInvalid` and assigns it back with `SetState_Cache`
(`Method=Assign`), or calls `InvalidateCache()` (`Method=Invalidate`). `EndModify` and `GraphicallyInvalidate`
follow, and `PostProcess` closes the bracket. The first pad's cache is then read again for the `after:` line.
`ReadPadCache` lists the cache fields of the same first pad.

## Notes
- The first pad is the first one the iterator returns. The command does not choose a particular pad.
- The change is made in memory only. The document is marked modified; close it without saving to discard the change.
- The native cache record has fields that `PCB.V7_PadCache` lacks. Assigning the cache from .NET with
  `SetState_Cache` is safe: the fields the command does not change (paste mask expansion, relief air gap, power plane
  clearance, with their states) are kept, for one pad and for every pad of a board, with no error. The
  `before:`/`after:` lines list those fields so you can check this.
- The assigned `eCacheInvalid` is **not** durable, and `PCBServer.PostProcess` is what ends
  it: the field reads `eCacheInvalid` after `SetState_Cache` and after `EndModify`, and `eCacheValid` right after
  `PostProcess`. Reading `IPCB_Pad.SolderMaskExpansion` or calling `GraphicallyInvalidate` does not change it. The
  cached value stays the same throughout.
- `Method=Invalidate` calls `IPCB_Pad.InvalidateCache` instead of assigning a record. It is a different operation,
  not a drop-in fallback: it sets only `ReliefAirGapValid` and `PowerPlaneClearanceValid`
  to `eCacheInvalid`, leaves the solder mask and paste mask states `eCacheValid`, and changes no cached value. The
  states it sets survive `PostProcess` and later commands.
- Not known: whether the revalidation recomputes the expansion from the design rules or restores the stored one,
  and whether the change can be undone. The meaning of `eCacheValid` versus `eCacheManual` is not documented, and
  this sample makes no claim about it.
