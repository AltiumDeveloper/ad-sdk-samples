# List harness ports of the focused sheet via the UDM

Lists the harness ports of the focused schematic sheet with their harness types. It shows how to go from the
project's compiled data model (EDP) to the live schematic objects: read-lock the unified data model, resolve each
port's schematic handle to an `SCH.ISch_Port`, and read its harness type through `SCH.IHarnessTypeHolder`.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` / `EDP.IWorkspace.DM_FocusedDocument`: the project and sheet the user works on.
- `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: compile only when the data model is out of date.
- `EDP.IProject.DM_DataModel` with `EDP.IUnifiedDataModel.BeginRead` / `EDP.IUnifiedDataModel.EndRead`: every
  access to the compiled model, handle resolution included, sits inside one read bracket closed in a `finally`.
- `EDP.IDocument.DM_PortCount` / `EDP.IDocument.DM_Ports`: the ports of one document, as `EDP.INetItem`.
- `EDP.IUnifiedDataModel.SupportsInterface` with `EDP.IDMObject.DM_SchHandle`: resolves a data-model item to the
  live schematic object (an `out object`, cast by the caller).
- `SCH.ISch_BasicContainer.ObjectId`: checks that the resolved object is a port (`SCH.TObjectId.ePort`) instead of
  a type test, which succeeds for every SDK object.
- `SCH.IHarnessTypeHolder.IsHarnessObject` / `SCH.IHarnessTypeHolder.HarnessType`: whether a port belongs to a
  harness, and the harness type name.

## Run it
1. Open a project with harnesses (for example the Bluetooth Sentinel example project that ships with Altium
   Designer) and focus one of its schematic sheets that has harness ports, such as `Host_Controller.SchDoc`.
2. **HarnessPortTypes › Get Harness**, or `tools/ad.ps1 -Sample HarnessPortTypes run GetHarness`
3. The result names the sheet, how many ports it found (and from how many data-model entries), how many are
   harness ports and how many did not resolve, then one line per harness port:
   `<port name>: harness type '<type>'`.

| Command | Parameters | Result |
|---|---|---|
| `GetHarness` | none | `<sheet>: <n> port(s) from <e> data-model entries, <h> harness port(s), <u> not resolved.` plus one line per harness port |

## How it works
`HarnessPortTypesCommands.cs` gets the workspace from `DXP.GlobalVars.Client`, then the focused project, and
compiles it if `DM_NeedsCompile` says so. Inside `BeginRead`/`EndRead` it takes the focused document, rejects
anything that is not a `.SchDoc`, and walks `DM_Ports(i)` for `i < DM_PortCount()`. `ResolvePort` passes each
port's `DM_SchHandle` to `SupportsInterface` and keeps the object only if its `ObjectId` is `ePort`. The port is
then cast to `IHarnessTypeHolder`; ports whose `IsHarnessObject` is true are listed with their `HarnessType`.
`DM_Ports` yields one `INetItem` per net of a port, so a harness port comes back once per signal of its harness;
entries whose `DM_SchHandle` was already seen are skipped, which is why the summary reports both counts.

## Notes
- The command may compile the project (only when it needs compiling). That changes compiled state, not files.
- `SCH.ISch_Port` does not derive from `SCH.IHarnessTypeHolder` in the .NET interfaces. The cast goes through COM
  at run time; ports answer `IsHarnessObject` and `HarnessType` through it. A `COMException` from it would count
  the port as "not resolved".
- `SupportsInterface` resolves handles for sheets open in an editor; on the focused sheet every port handle
  resolves.
- `DM_Ports` lists the compiled model's net items, not the drawn ports: a harness port appears once per signal of
  its harness, every entry carrying the same `DM_SchHandle`. For example, a sheet with 5 drawn ports gave 21
  entries, 7 of them for one 7-signal harness port.
- No harness ports on the sheet gives an explicit message. A focused PCB document is rejected with an error.
