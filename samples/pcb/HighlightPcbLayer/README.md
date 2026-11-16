# Show one PCB layer in single-layer mode

Turns on single-layer mode in the PCB editor and makes one layer (Top Layer by default) the current layer, so that
layer stands out and the others are dimmed or hidden. It shows how to switch a PCB editor option through the system
options, and how to set the current layer of a board's view from .NET.

## What it shows
- `PCB.IPCB_ServerInterface.SystemOptions` / `PCB.IPCB_SystemOptions.SingleLayerMode`: the setting as a property, the
  default and only working route, also used to read the state back.
- `DXP.Utils.RunCommand`: the alternative route, the process `PCB:SetupPreferences` with `SingleLayerMode=ON` /
  `SingleLayerMode=OFF` (`Via=Process`). **It has no effect** — see Notes.
- `PCB.IPCB_Board.CurrentLayerV7`: the writable current layer. `PCB.IPCB_Board.CurrentLayer` (a `PCB.TV6_Layer`) is
  read-only in .NET, so the layer is set with
  `SetState_CurrentLayerV7(new V7_Layer(TV6_Layer.eV6_TopLayer))`.
- `PCB.V7_Layer` built from a `PCB.TV6_Layer`; `PCB.IPCB_Board.LayerName` gives the layer's name on this board.
- `PCB.IPCB_Board.Update_PCBGraphicalView`: redraws the view after the change.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium Designer)
   in a PCB editor view and make it the active document.
2. **HighlightPcbLayer › Highlight Layer**, or `tools/ad.ps1 -Sample HighlightPcbLayer run HighlightLayer ""`
3. The Top Layer is the current layer in single-layer mode. The result reads
   `<board>.PcbDoc: single-layer mode ON (set ON via IPCB_SystemOptions.SingleLayerMode), current layer Top Layer (eV6_TopLayer).`
   Run `HighlightLayer "Off=true"` to return to the normal view.

| Command | Parameters | Result |
|---|---|---|
| `HighlightLayer` | `Layer=<name>` (optional, default Top Layer: a `TV6_Layer` name with or without `eV6_` and spaces, such as `BottomLayer` or `Mid Layer 1`, or the layer's name on the board), `Via=Property` (default) or `Via=Process`, `Off=true` (optional: turn single-layer mode off, leave the current layer) | `<board>: single-layer mode ON/OFF (set ON/OFF via <route>), current layer <name> (<TV6_Layer>).`, plus `WARNING: <route> did not change the mode ...` when the read-back disagrees with what was asked for. An error if the layer is unknown or did not become current |

## How it works
`HighlightPcbLayerCommands.cs`: `HighlightLayer` gets the current board from `PCB.GlobalVars.PCBServer` and resolves
the `Layer` parameter to a `TV6_Layer` before it changes anything: first as an enum name, then by comparing
`LayerName` of every `TV6_Layer` with the given text. It then switches single-layer mode, with
`SystemOptions.SetState_SingleLayerMode(true)` or, when `Via=Process`, with
`DXP.Utils.RunCommand("PCB:SetupPreferences", "SingleLayerMode=ON")`. Next it sets
`CurrentLayerV7` to a `V7_Layer` made from the `TV6_Layer`, and calls `Update_PCBGraphicalView(false, true)`. Last, it reads back `SingleLayerMode`, `CurrentLayerV7` and `CurrentLayer`. If the current layer is not
the requested one, it raises an error instead of reporting success.

## Notes
- **The process route does not work.**
  `DXP.Utils.RunCommand("PCB:SetupPreferences", "SingleLayerMode=ON")` returns without error, raises nothing and
  writes nothing to the Messages panel, but leaves `IPCB_SystemOptions.SingleLayerMode` unchanged — in both
  directions, and for the parameter values `ON`, `OFF`, `True`, `Toggle` and `Cycle`. The property is therefore the
  default route; `Via=Process` is kept so the two can be compared, and the
  result warns when the mode did not change. **Lesson: prefer the typed server options over `PCB:SetupPreferences`
  parameters, and always read the setting back.**
- The command changes view state, not design objects: the document is not marked modified.
  Single-layer mode is a PCB editor option (`PCB.IPCB_SystemOptions`), not a property of the board, so run
  `Off=true` to return to the normal view.
- Setting `CurrentLayerV7` works on a board open in a PCB editor view (for example Top Layer and Bottom Layer); it has no
  effect on a board without a view (docs: PCB basics, "Boards without a view"), and then the
  read-back check fails with an error.
- The `SingleLayerMode` read-back is for PCB documents. When the active view shows a PCB library, the property reports
  the library's state (docs: `PCB.IPCB_SystemOptions.SingleLayerMode`). The command is meant for `.PcbDoc` files.
- Layer names: the enum names cover the fixed `TV6_Layer` set (for example mechanical layers up to 16). A layer the
  user renamed in the layer stack is found by its board name through `LayerName`. `Layer=NoLayer` is rejected.
- In the result, "set ON/OFF via <route>" names the route that was requested. The ON/OFF value before it is read
  back from `SingleLayerMode`, so a process call that did nothing still shows up.
