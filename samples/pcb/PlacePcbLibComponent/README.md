# Place a footprint from an IntLib onto the board

Finds a component in an integrated library, picks its PCB footprint model and places that footprint on the current
board as a new component. It shows how to search an `.IntLib` with the integrated library manager and how to create a
component with the PCB object factory and `LoadFromLibrary`.

## What it shows
- `EDP.Utils.LoadIntegratedLibraryManager`: the .NET entry point to the integrated library manager.
- `SCH.IIntegratedLibraryManager.AvailableLibraryPath` / `SCH.IIntegratedLibraryManager.InstalledLibraryPath`: find a
  library by file name instead of hard-coding its folder.
- `SCH.IIntegratedLibraryManager.GetComponentCount` / `SCH.IIntegratedLibraryManager.GetComponentName`: find a
  component by name (0-based index).
- `SCH.IIntegratedLibraryManager.GetModelCount`, `SCH.IIntegratedLibraryManager.GetModelName`,
  `SCH.IIntegratedLibraryManager.GetModelType` and `SCH.IModelType.Name`: find the component's footprint model.
  `GetModelType` is a typed helper (`SCH.IIntegratedLibraryManagerHelper`), called statically here.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` with `PCB.TObjectId.eComponentObject`: create an empty component.
- `PCB.IPCB_Component.LoadFromLibrary`: load the footprint's primitives from the library into the component.
- `PCB.IPCB_Primitive.Layer`, `PCB.IPCB_Group.XLocation`, `PCB.IPCB_Group.YLocation`, `PCB.IPCB_Board.AddPCBObject`:
  put it on the top layer at a location and add it to the board, between `PreProcess` and `PostProcess`.
- `PCB.IPCB_Board.GraphicalView_ZoomRedraw`: redraw the view.

## Run it
1. Open a `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium Designer)
   and make it the active document. The defaults use `Miscellaneous Devices.IntLib`, which must be in Altium
   Designer's available or installed libraries; or pass any `.IntLib` whose components have footprint models with
   `Lib=` (a full path, or the file name of an installed library), `Component=` and `Footprint=`.
2. **PlacePcbLibComponent › List Models**, or `tools/ad.ps1 -Sample PlacePcbLibComponent run ListModels` to see the
   component's models and their type names (read-only).
3. **PlacePcbLibComponent › Place Component**, or `tools/ad.ps1 -Sample PlacePcbLibComponent run PlaceComponent`
   (or, for another library, `run PlaceComponent "Lib=<file>.IntLib|Component=<name>|Footprint=<model>"`).
4. The result names the placed footprint (`SOIC16_M` with the defaults), its designator, pad count, layer and
   location, the parameter key `LoadFromLibrary` used, and the component count before and after (one more). The
   footprint appears at the center of the board outline. The document is modified, not saved.

| Command | Parameters | Result |
|---|---|---|
| `PlaceComponent` | `Lib=<.IntLib path or file name>` (default `Miscellaneous Devices.IntLib`), `Component=<name>` (default `Res Pack1`), `Footprint=<model name>` (default `SOIC16_M`), `X=<mil>`, `Y=<mil>` (both or neither; from the board origin; default: center of the board outline), `LoadKey=<key>` (optional, see Notes) | `Placed footprint '<pattern>' ... Components on the board: n -> n+1.` or an error saying what was not found |
| `ListModels` | `Lib=`, `Component=` as above | One line per model: `[j] <model name> : type '<IModelType.Name>' (server '<ServerName>')` |

## How it works
`PlacePcbLibComponentCommands.cs`: `PlaceComponent` gets the PCB server and the current board, then the integrated
library manager. `ResolveLibrary` takes `Lib` as a full path, or looks the file name up among the available and
installed libraries. `FindComponent` loops `GetComponentCount` / `GetComponentName` (case-insensitive). `FindPcbModel` loops the component's models and keeps the one whose `GetModelType(...).Name()`
is `PCBLIB` and whose `GetModelName` is the requested footprint. The location is `X`/`Y` from the board origin or the
center of `PCB.IPCB_Board.BoardOutline`'s bounding rectangle.

Between `PreProcess` and `PostProcess`, `PCBObjectFactory` creates a component, `SetState_Board` assigns the board, and
`LoadFromLibrary("<key>=<library path>|Footprint=<model name>")` loads the footprint (key `IntegratedLibraryName`
unless `LoadKey` forces another). If it returns false or throws a `COMException`, the component is destroyed and
nothing is placed. On success the layer is set to `eV6_TopLayer`, the location assigned and the component added with
`AddPCBObject`. A board iterator counts components before and after. `ListModels` prints the models of the component
with their type names.

## Notes
- `LoadFromLibrary` key for an `.IntLib`: `IntegratedLibraryName=<path>`. The reference's `FileName=<path>` (the
  key for a `.PcbLib`) throws a `COMException` (0x8000FFFF, "Catastrophic failure") with an `.IntLib` path instead
  of returning false, so the command catches it and reports it. `LoadKey=<key>` forces another key.
- The model type name: .NET has no constant for the footprint model type. `SCH.IModelType.Name` returns `PCBLIB`
  for a footprint model (`ServerName` `PCB`) and `SIM` for a simulation model; `ListModels` prints it.
- .NET exposes `PCB.IPCB_Primitive.Layer` as `PCB.TV6_Layer`, so the sample sets `eV6_TopLayer`; the placed
  component reads back `eV6_TopLayer`.
- The location is relative to the board origin (the coordinates AD shows), not absolute internal coordinates.
  Coordinates are internal units, 10,000 per mil.
- The placed component keeps the designator `LoadFromLibrary` gives it, the placeholder `Designator1`. Running the
  command twice places two components with the same designator. It changes the board in memory only. Close the
  document without saving to discard it.
- `SCH` and `PCB` both define `TObjectId`, `CoordRect` and others: the file aliases the two `SCH` types it needs instead
  of `using SCH;` (otherwise CS0104).
