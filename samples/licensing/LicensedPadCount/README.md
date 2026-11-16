# Gate a command behind a technology-set license

Counts the pads of the current PCB board, but only when the licensing layer reports a technology set as
supported. It shows how an extension asks its own server module (`DXP.IServerSecurity`) for a license before doing
the work, and uses a filtered board iterator as the gated payload.

## What it shows
- `DXP.IServerSecurity.IsTechnologySetSupported`: `true` when the active license supports the named technology set.
  It is the license gate in front of the command.
- `DXP.ServerModule` / `DXP.IClient.GetServerModuleByName`: the class `DXP.ServerModule` implements
  `DXP.IServerSecurity`, the interface `DXP.IServerModule` does not. Code in the Impl project looks up the
  extension's own module by its server name and casts it.
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document, or none.
- `PCB.IPCB_Board.BoardIterator_Create` / `PCB.IPCB_Board.BoardIterator_Destroy`: the board owns the iterator;
  destroy takes it by `ref` and is called in a `finally` block.
- `PCB.IPCB_AbstractIterator.AddFilter_ObjectSet`, `PCB.IPCB_AbstractIterator.AddFilter_LayerSet_2` with
  `PCB.PCBConstant.AllLayersSet`, and `PCB.IPCB_BoardIterator.AddFilter_Method` with
  `PCB.TIterationMethod.eProcessAll`: pads only, all layers, free and component pads.

## Run it
1. Open any `.PcbDoc` (for example the board of SpiritLevel-SL1, an example project that ships with Altium
   Designer) and make it the active document.
2. **LicensedPadCount › Check License**, or `tools/ad.ps1 -Sample LicensedPadCount run CheckLicense "TechnologySet=Schematic_Editor"`
3. **LicensedPadCount › Count Pads**, or `tools/ad.ps1 -Sample LicensedPadCount run CountPads "TechnologySet=Schematic_Editor"`.
   With a supported set it returns the pad count; with an unsupported one (the default, the extension's own name,
   is expected to be unsupported) it returns the "no licence" message and does not read the board.

| Command | Parameters | Result |
|---|---|---|
| `CountPads` | `TechnologySet=<name>` (optional, default `LicensedPadCount`), `Server=<name>` (optional, see Notes) | `Technology set '<name>' is licensed. <file>: <N> pad(s).` or `No licence for technology set '<name>': pads not counted.` |
| `CheckLicense` | `TechnologySet=<name>` (optional, default `LicensedPadCount`), `Server=<name>` (optional, see Notes) | `Technology set '<name>' is supported.` / `... is not supported.` |

## How it works
`LicensedPadCountCommands.cs` reads the `TechnologySet` parameter (default `SampleInfo.Name`, the extension's
server name). `ServerSecurity(ctx)` gets `DXP.GlobalVars.Client`, calls `GetServerModuleByName` with the server name
(the `Server` parameter, default the extension's own server name) and casts the module to `DXP.IServerSecurity`.
`CountPads` calls `IsTechnologySetSupported` first; if the set is not supported it returns at once. Otherwise it
gets the current board from `PCB.GlobalVars.PCBServer` (a `UserException` covers no PCB editor or no active board),
creates a board iterator with the object, layer and method filters, counts the pads (logging every 1000) and
destroys the iterator. `CheckLicense` only runs the gate.

## Notes
- Read-only: the board is not modified.
- The shared framework registers every command through one `Run` process with an always-enabled state handler, so
  the sample gates at run time only and cannot grey out the menu item when unlicensed. The menu item is always
  enabled.
- The SDK reference does not say which technology set names exist, and the `.ins` has no technology-set entry. The
  extension's own name `LicensedPadCount` and an unknown name are "not supported" (no exception);
  `Schematic_Editor` is supported with a license that includes schematic editing; the name is case-sensitive
  (`schematic_editor` is not supported). How an extension gets its own set into a license remains open.
- `Server=<name>` picks the server module that supplies `IServerSecurity`. It is a helper for running the
  implementation loaded inside another extension, where the `LicensedPadCount` module is not registered. There,
  `GetServerModuleByName("<that extension's server>") as IServerSecurity` is non-null at run time, because that
  module is a `DXP.ServerModule` too.
