# Set manual per-side solder mask on named pads

Finds every pad with a given name on the current board and switches its solder mask expansion to manual, with
separate top and bottom values. It shows how to change a pad's rule-derived settings through its cache
(`PCB.V7_PadCache`): read the cache, change fields, assign it back.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.IPCB_BoardIterator.AddFilter_LayerSet_2` and
  `PCB.PCBConstant.AllLayersSet`: walk every pad of the board, free pads and component pads alike.
- `PCB.IPCB_Pad.Name`: the pad's name (designator). Names aren't unique on a board, so all pads with the name match.
- `PCB.IPCB_Pad.Cache`: `GetState_Cache` returns a `PCB.V7_PadCache`; changes take effect when it is assigned back
  with `SetState_Cache`.
- `PCB.V7_PadCache.SolderMaskExpansionValid` = `PCB.TCacheState.eCacheManual`: the pad's own expansion is used
  instead of the solder mask expansion rule.
- `PCB.V7_PadCache.UseSeparateExpansions`, `PCB.V7_PadCache.SolderMaskExpansion`,
  `PCB.V7_PadCache.SolderMaskBottomExpansion`: separate top and bottom expansions, in internal units (converted
  with `EDP.Utils.MilsToCoord` / `EDP.Utils.CoordToMils`).
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify`: the
  bracket for changing PCB objects.

## Run it
1. Open a `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium Designer,
   which has five pads named `M3`) and make it the active document.
2. **PadSolderMask › Show Pad Solder Mask**, or
   `tools/ad.ps1 -Sample PadSolderMask run ShowPadSolderMask "Pad=M3|Max=3"`, to see the current settings.
3. `tools/ad.ps1 -Sample PadSolderMask run ChangePadSolderMask "Pad=M3|Top=4|Bottom=6"` (`M3` is the default pad
   name; the menu item **Change Pad Solder Mask** passes `Pad=M3` without `Top`/`Bottom`,
   so it only switches the pads to a manual, per-side expansion). The result names the number of changed pads
   and the first pad's cache, read back. Run **Show Pad Solder Mask** again to check them.

| Command | Parameters | Result |
|---|---|---|
| `ChangePadSolderMask` | `Pad=<name>` (default `M3`), `Top=<mils>`, `Bottom=<mils>` (optional) | `<board>: changed <n> pads named '<name>' ... First pad now: <descriptor>: SolderMaskExpansionValid=eCacheManual, UseSeparateExpansions=True, Top=..., Bottom=...` |
| `ShowPadSolderMask` | `Pad=<name>` (default `M3`), `Max=<n>` (default 10) | `<board>: <n> pads named '<name>'.` and one line per pad (at most `Max`) with the cache's solder mask fields |

## How it works
`PadSolderMaskCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`. `FindPads` iterates the pad
objects on all layers and collects those whose `GetState_Name()` equals the `Pad` parameter. It destroys the
iterator before anything changes. `ChangePadSolderMask` then calls `PreProcess`, and for each pad calls
`BeginModify`, reads `GetState_Cache()`, sets `SolderMaskExpansionValid` to `eCacheManual` and
`UseSeparateExpansions` to true (plus the expansions when `Top`/`Bottom` are given), assigns the cache back with
`SetState_Cache`, calls `EndModify` and redraws the pad with `GraphicallyInvalidate`. `PostProcess` closes the
bracket. `ShowPadSolderMask` lists the same cache fields and `IPCB_Primitive.SolderMaskExpansion` for each
matching pad.

## Notes
- Without `Top`/`Bottom` the command changes only the two mode flags. The stored expansion values stay as they
  were, so the pad's visible mask may not change.
- The change is made in memory only, and the `PreProcess`/`BeginModify` bracket marks the document modified (an
  unbracketed change does not). Save the document to keep it.
- With `UseSeparateExpansions=True` and a manual cache, `IPCB_Primitive.SolderMaskExpansion`
  reports the manual **top** value (4 mil after `Top=4|Bottom=6`). Whether the bracketed change can be undone as
  one step is not known.
- The native cache record has fields that `PCB.V7_PadCache` lacks. On the `M3` pads of the SpiritLevel-SL1 board,
  assigning the cache is clean: no error, and the paste mask and relief air gap values
  and states were kept unchanged. That is one pad shape, not a general guarantee. Try it on a copy of a design, not
  on work you can't lose. `ShowPadSolderMask` lists paste mask and relief air gap, which this sample never changes,
  so you can check that they are kept.
- `ShowPadSolderMask` is a read-back helper.
