# Read the dielectric loss tangent (Df) of each layer

Lists the dielectric layers of the current board's master layer stack with their material, dielectric constant (Dk)
and loss tangent (Df). It shows how to walk the layer stack by layer class and read Df through the typed
`PCB.IPCB_DielectricLayer.DielectricLossTangent` property.

## What it shows
- `PCB.IPCB_Board.MasterStack`: the board's master layer stack.
- `PCB.IPCB_LayerStackBase.Iterator_1` with `PCB.TLayerClassID.eLayerClass_Dielectric`: an iterator that visits only
  the dielectric layers, so each `PCB.IPCB_LayerObjectIterator.LayerObject` can be cast to `PCB.IPCB_DielectricLayer`.
- `PCB.IPCB_LayerObjectIterator.SetBeforeFirst` / `PCB.IPCB_LayerObjectIterator.Next`: the iteration pattern.
- `PCB.IPCB_DielectricLayer.DielectricLossTangent`: the loss tangent (Df), a `double`, read with
  `GetState_DielectricLossTangent`. No raw COM dispatch is needed in .NET.
- `PCB.IPCB_DielectricLayer.DielectricConstant`, `PCB.IPCB_DielectricLayer.DielectricMaterial`,
  `PCB.IPCB_DielectricLayer.DielectricType`, `PCB.IPCB_LayerObject.LayerName`: the context printed next to Df.

## Run it
1. Open a `.PcbDoc` with a layer stack (for example the board of the SpiritLevel-SL1 example project that ships with
   Altium Designer) and make it the active document.
2. **DielectricLossTangent › Get Df Value**, or `tools/ad.ps1 -Sample DielectricLossTangent run GetDfValue`
3. The result starts with `<board>.PcbDoc: <n> dielectric layer(s):`, followed by one line per layer, for example
   `LayerName : Dielectric1; Type : Core; Dielectric Material : FR-4; Dk : 4.8; Df : <value>`.

| Command | Parameters | Result |
|---|---|---|
| `GetDfValue` | `Out=<file>` (optional: also write the layer lines to this file) | The layer list as text; with `Out` also `Written to <path>` |

## How it works
`DielectricLossTangentCommands.cs`: `GetDfValue` gets the current board from `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`
and its master layer stack. `Iterator_1(eLayerClass_Dielectric)` creates a layer iterator; after `SetBeforeFirst`, each
`Next` moves to the next dielectric layer. The layer object is cast to `PCB.IPCB_DielectricLayer`, and the command
reads `DielectricLossTangent`, `DielectricConstant`, `DielectricMaterial`, `DielectricType` and `LayerName`. Numbers
are formatted with the invariant culture in round-trip form. `Out` writes the same lines (without the header) to a file.

## Notes
- The .NET API has `DielectricLossTangent` as a typed property (read/write), so there is no need to call the getter
  through a raw COM dispatch ID.
- The cast relies on the iterator's layer class filter. A C# `as` test would not prove the type: SDK interfaces are
  dispatch interfaces and the cast succeeds for any layer object. The .NET API has no `IsDielectricLayer()` helper.
- This iterator also visits the solder mask layers (type Surface Material), so they appear in the list.
- A dielectric layer whose material carries no loss tangent reports `0`. On the SpiritLevel-SL1 board all three
  dielectric layers report Df 0, while `DielectricConstant` returns real values on the same objects.
- The command is read-only and does not change the board (the document stays unmodified).
