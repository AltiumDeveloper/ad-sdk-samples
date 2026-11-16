# Place a server-managed component on SCH or PCB

Downloads a component's schematic and PCB libraries from an HTTP component management server, reads the component's
metadata from the response's `Content-Disposition` header, and places the symbol or footprint. On the schematic it
then stamps the placed component with the metadata as parameters and links the downloaded footprint as its current
PCB model. It shows how to place from a library file you got from outside AD, and how to tag what was placed.

## What it shows
- `DXP.IClient.LastActiveDocumentOfType` / `DXP.IClient.ShowDocument`: find the last schematic or PCB document and
  bring it to the front before placing.
- `EDP.Utils.LoadIntegratedLibraryManager` and `SCH.IIntegratedLibraryManager.PlaceLibraryComponent`: place a symbol
  from a `.SchLib` file. The third argument is an AD parameter string: `Location.X`/`Location.Y` place at a point,
  without them AD places interactively.
- `SCH.ISch_BasicContainer.Handle`: tell newly placed components from existing ones (before/after comparison).
- `SCH.ISch_Component.DesignItemId` / `SCH.ISch_Component.SourceLibraryName`: a fallback way to find the placed
  component.
- `SCH.ISch_ServerInterface.SchObjectFactory` (`eParameter`), `SCH.ISch_Parameter.ParamType`,
  `SCH.ISch_Component.AddSchObject`: add a typed parameter (`STRING`, `BOOLEAN`, `INTEGER`, `FLOAT`) or update an
  existing one.
- `SCH.ISch_RobotManager.SendMessage` with `SCHMBeginModify`/`SCHMEndModify`/`SCHMPrimitiveRegistration`: tell the
  editor about the changes.
- `SCH.ISch_Component.AddSchImplementation`, `SCH.ISch_Implementation.AddDataFileLink`,
  `SCH.ISch_Implementation.IsCurrent`: add a `PCBLIB` model that points at a footprint in a `.PcbLib` file and make it
  the current one.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` and `PCB.IPCB_Component.LoadFromLibrary`
  (`FileName=<path>|Footprint=<name>`): build a board component from a `.PcbLib` file.
- `PCB.IPCB_Board.AddPCBObject` / `PCB.IPCB_Board.RemovePCBObject` and `DXP.Utils.RunCommand`
  (`PCB:PlaceComponent`, `RepositionSelected=True`): interactive placement, and undoing it on cancel.

## Run it
1. Start the local stub server, which stands in for a component management server. Use an example project that ships
   with Altium Designer, such as SpiritLevel-SL1: open its `SL_Power.SchDoc` and run
   `tools/ad.ps1 -Sample ComponentManagerPlaceComponent run StartTestServer "SchLib=<path>\SpiritLevel_2E.SchLib|PcbLib=<path>\Spirit_Level_Project.PcbLib"`
   with the two libraries from the project's `Libraries` folder. It answers with the URL and the
   `Url=...|SessionId=test|ComponentId=CM-TEST-1` to use.
2. **ComponentManagerPlaceComponent › Place Sch Component** (interactive, uses the running stub), or
   `tools/ad.ps1 -Sample ComponentManagerPlaceComponent run PlaceSchComponent "Url=<url>|SessionId=test|ComponentId=CM-TEST-1|X=500|Y=500"`
3. The result names the downloaded files, what `PlaceLibraryComponent` returned, the component count before and after,
   and for each stamped component its designator, location, models (`PCBLIB:<footprint>* -> <cache>\PcbLib\...`) and
   parameters (`ComponentManager_ID=CM-TEST-1`, ..., `CM_Supplier=Test Supplier & Co (eParameterType_String)`, ...).
   For the PCB, open a `.PcbDoc` and run `PlacePcbComponent` the same way: it reports the footprint, location, pad
   count and the component count before and after.

| Command | Parameters | Result |
|---|---|---|
| `PlaceSchComponent` | `Url`, `SessionId`, `ComponentId` (required, unless the stub runs and `Url` is omitted); `Out=<cache folder>` (default `<temp>\ComponentManager`); `X`/`Y` in mils (default 1000, 1000); `Interactive` (menu default, refused from `tools/ad.ps1`); `Part=<n>`; `Timeout=<s>` (30) | Downloads, placement result, stamped components with their models and parameters |
| `PlacePcbComponent` | as above; `X`/`Y` in mils from the board origin (default: centre of the board outline) | Footprint, location, designator, pads, component count before → after |
| `StartTestServer` | `SchLib`, `PcbLib` (full paths, required); `LibRef`, `Footprint` (default: a component of the active sheet with a PCB model, or the first board component); `Id` (`CM-TEST-1`); `LibraryPath` (SchLib file name); `FootprintPath`; `ComponentType`; `AssembleTech`; `Params=Name:Type:Value;...`; `Port` (0 = any free) | `Test server listening at http://127.0.0.1:<port>/ ... Use: Url=...` |
| `StopTestServer` | none | Request count and last request |

## How it works
`ComponentServer.cs` is the plain .NET client. It resolves `Home/DownloadPcbLibFile?componentId=..&sessionId=..` (or
`DownloadSchLibFile`) against `Url` and downloads the body with `HttpClient`, run on the thread pool while AD's thread
waits. It reads the raw `Content-Disposition` header (`NonValidated`), because the server sends custom keys, not an RFC
6266 value. The header is split into `;`-separated `key=value` pairs (case-insensitive) and the required keys are checked:
`filename`, `id`, `componenttype`, `assembletech`, `librarypath`, `libraryref`, `footprintpath`, `footprintref`,
`ParameterCount` and `ParameterName_i`/`ParameterType_i`/`ParameterValue_i`. Values are percent-decoded.
The file is saved under `<Out>\PcbLib` or `<Out>\SchLib` after the server's file name has been checked.

`PlaceSchComponent` (`Commands/ComponentManagerPlaceComponentCommands.cs`) downloads the PcbLib, then the SchLib (whose
metadata is used). It records the handles of the sheet's components and calls
`PlaceLibraryComponent(libraryref, <downloaded SchLib>, "Location.X=..|Location.Y=..[|PartID=n]")`. It then picks the
components that are new and carry the lib reference. If there are none, it falls back to every component with that `DesignItemId` and
`SourceLibraryName` that has no `ComponentManager_ID` yet. `SchPlacement.cs`
stamps each one between `SCHMBeginModify`/`SCHMEndModify`: seven `ComponentManager_*` string parameters plus the
server's list (`AddOrSetParameter`, which updates or creates). Then `AddFootprintModel` makes the existing `PCBLIB`
models non-current and adds a new current one with `AddDataFileLink(footprint, <downloaded PcbLib>, "PCBLIB")`.

`PlacePcbComponent` downloads the PcbLib, runs `PCB:DeSelect`, and between `PreProcess`/`PostProcess` creates a
component with `PCBObjectFactory`, sets its board, calls `LoadFromLibrary` (checked), sets the top layer and location,
and adds it. In interactive mode it parks the component at (-1,-1) and selects it. Then it runs `PCB:PlaceComponent`
with `RepositionSelected=True`, and removes the component again if it is still at (-1,-1) (cancelled).

`TestServer.cs` is the stub: a `TcpListener` on 127.0.0.1 on a background thread that answers the two paths with the
configured files and a header in the format above (404 for any other component id). `SampleHooks.cs` stops it before
the implementation is unloaded.

## Notes
- **Interactive placement** waits for a mouse click, so it is the default from the menu only. From `tools/ad.ps1` it
  is refused; pass `X`/`Y` or nothing. If
  `RunCommand("PCB:PlaceComponent", "RepositionSelected=True")` returns before the click, the component is still at
  (-1,-1) and is removed. `PCB:PlaceComponent` and
  `RepositionSelected` are not described in the SDK reference.
- `PlaceLibraryComponent` accepts the full path of a `.SchLib` that is not installed, and
  `LoadFromLibrary` accepts the full path of a `.PcbLib` that is not installed.
- `Location.X`/`Location.Y` are passed as `EDP.Utils.MilsToCoord(mils)`, following the reference's example values.
  `X=500|Y=500` places the symbol at (500, 500) mil without interaction.
- A component placed from a file gets the file name, without folder, as `SourceLibraryName` (for example
  `SpiritLevel_2E.SchLib`). The stamping fallback and the stub's default `LibraryPath` rely on this.
- Iterating a component with the filter `{eParameter, eImplementation}`, dispatch on `GetState_ObjectId()`, not on a
  C# type test: `item is ISch_Parameter` can be true for an object whose `GetState_Name()` then
  fails with "Interface not supported".
- Only `PCBLIB` models are switched to non-current. A current 3D model of the symbol (`PCB3DLib`) stays current.
- New parameters are hidden and placed at the component's location. Existing parameters of the same name
  (case-insensitive) are updated.
- On the PCB, the server's parameter list is downloaded but not applied.
- Pass an absolute `Out` folder. A relative one resolves against AD's install folder, which is usually not writable.
- `Url` handling: `http://host/app/Index` and `http://host:port` both give `http://host[/app]/Home/Download...`. Values
  are decoded with `Uri.UnescapeDataString`: `%20` becomes a space, `+` stays `+`.
- Server file names with a path, invalid characters or the wrong extension are rejected, so a file name from the
  server cannot point outside the cache folder.
- The menu items cannot pass parameters: without `Url` they use the running test server, or report the missing `Url`.
- Close the documents without saving to discard the changes.
