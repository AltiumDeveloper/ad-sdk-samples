# List internal plane and dielectric layers

Lists the internal plane layers and the dielectric layers of the current board, with their thickness and material
properties in millimetres. It shows how to walk a board's master layer stack with an iterator filtered by layer
class and how to cast each layer object to the typed interface of that class.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document, or none.
- `PCB.IPCB_Board.MasterStack`: the board's `PCB.IPCB_MasterLayerStack`, which holds every layer of the stack,
  including the dielectrics.
- `PCB.IPCB_LayerStackBase.Iterator_1` with `PCB.TLayerClassID.eLayerClass_InternalPlane` /
  `PCB.TLayerClassID.eLayerClass_Dielectric`: an iterator that visits only one class of layer. Walk it with
  `PCB.IPCB_LayerIterator.SetBeforeFirst` and `PCB.IPCB_LayerIterator.Next`.
- `PCB.IPCB_LayerObjectIterator.LayerObject`: the `PCB.IPCB_LayerObject` at the current position, cast to
  `PCB.IPCB_InternalPlane` or `PCB.IPCB_DielectricLayer`. The class filter is what makes the cast safe: a C# type test
  (`is` / `as`) succeeds for any SDK object.
- `PCB.IPCB_ElectricalLayer.CopperThickness`, `PCB.IPCB_InternalPlane.PullBackDistance`,
  `PCB.IPCB_InternalPlane.NetName`: the plane's properties. Lengths are in internal units.
- `PCB.IPCB_DielectricLayer.DielectricType`, `PCB.IPCB_DielectricLayer.DielectricHeight`,
  `PCB.IPCB_DielectricLayer.DielectricMaterial`, `PCB.IPCB_DielectricLayer.DielectricConstant`: the dielectric's
  properties. `PCB.TDielectricType` is mapped to a label in the sample.
- `EDP.Utils.CoordToMMs`: internal units (10,000 per mil) to millimetres.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument`: open the optional report file in Altium Designer's text
  editor.

## Run it
1. Open a `.PcbDoc` and make it the active document, for example the board of the SpiritLevel-SL1 example project
   that ships with Altium Designer. For `InternalPlaneInfo` the board needs at least one internal plane layer, or the
   command reports 0 layers.
2. **LayerStackInfo › Internal Plane Info** / **Dielectric Info**, or
   `tools/ad.ps1 -Sample LayerStackInfo run DielectricInfo`
3. The result gives a header with the board file and the layer count, then one line per layer (format in the
   table below). Thicknesses are in mm with 4 decimals. With `Out=<file>` the layer lines (without the header) are
   also written to that file and the result ends with `Written to <file>`. The menu items pass no `Out`, so from
   the menu the commands only return text.

| Command | Parameters | Result |
|---|---|---|
| `InternalPlaneInfo` | `Out=<file>` (optional) | `<file>: <n> internal plane layer(s):` then `LayerName : …; Type : Internal Plane; Material : Copper; Thickness : <mm> mm; Pullback : <mm> mm; Net : <net>` per plane. The `Out` file holds the layer lines only, without the header |
| `DielectricInfo` | `Out=<file>` (optional) | `<file>: <n> dielectric layer(s):` then `LayerName : …; Type : Dielectric; Material : <type>; Thickness : <mm> mm; Dielectric Material : <name>; Dielectric Constant : <Dk>` per layer. The `Out` file holds the layer lines only, without the header |

## How it works
`LayerStackInfoCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`. A `UserException` covers the
cases where no PCB editor or no PCB document is active. Each command takes the board's master stack
(`GetState_MasterStack`) and asks it for an iterator over one layer class (`Iterator_1`). After `SetBeforeFirst`, each
`Next` that returns `true` positions the iterator on a layer. `LayerObject()` returns that layer, which the command
casts to the typed interface and reads with `GetState_*`. Lengths go through `EDP.Utils.CoordToMMs`. `Report`
builds the returned text. When `Out` is given, it also writes the lines to that file. If, in addition, the run is
interactive (`ctx.IsInteractive`, so not from `tools/ad.ps1`), it opens the file with `IClient.OpenDocument("Text", …)`
and `ShowDocument`. The generated menu items pass no `Out`, so this branch needs an
interactive run with explicit parameters.

## Notes
- Read-only: nothing on the board changes.
- Layer object iterators have no destroy method (unlike board iterators), so none is called.
- The `V7_LayerID.IsInternalPlaneLayer` / `IsDielectricLayer` helpers don't exist in .NET. The class filter of
  `Iterator_1` makes such a check before the cast redundant. A `PCB.V7_LayerBase` has
  `Family`/`Genus`/`Species` fields, but their meaning is undocumented, so the sample doesn't use them.
- The dielectric type labels (`None`, `Core`, `Prepreg`, `Surface Material`, `Film`) are this sample's mapping of
  `PCB.TDielectricType`: the .NET SDK has no table of dielectric type names.
- `IClient.OpenDocument` and `IClient.ShowDocument` are typed extension methods. Without `using DXP;` the compiler
  reports CS1061.
- The dielectric class of the master stack includes the solder mask layers (type Surface
  Material, "Solder Resist"), listed top to bottom with the cores and prepregs; overlays are not listed.
- In the layer lines, `Material` is the dielectric type label (for example `Surface Material`) and
  `Dielectric Material` is the material name (for example `Solder Resist`).
- On a 2-layer board the internal plane list is empty.
