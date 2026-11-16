# Read the PCB viewport title and a technology set licence

Checks whether a technology set (by default `Cam350`) is licensed, then reads the title of the current board's
viewport, and returns both as text. It shows how an extension reaches `DXP.IServerSecurity` through its own server
module, and how to read `PCB.IPCB_Board.Viewport`.

## What it shows
- `DXP.IServerSecurity.IsTechnologySetSupported`: asks whether a named technology set is licensed in this session.
- `DXP.IClient.GetServerModuleByName` (with `GetCount` / `GetServerModule` as a fallback): finds a server module
  to cast to `DXP.IServerSecurity`.
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document, or none.
- `PCB.IPCB_Board.Viewport`: read with the typed helper `GetState_Viewport` (needs `using PCB;`).
- `PCB.IPCB_Viewport.ViewportTitle`: the viewport's title string.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document. Without one, the command (run from the command line, as the menu
   item is in the PCB editor's menu only) reports the licence line in its error.
2. **PcbViewport › Get Viewport**, or `tools/ad.ps1 -Sample PcbViewport run GetViewport "TechnologySet=Cam350"`
3. Two lines: `Technology set 'Cam350' supported: true` (or `false`), then `<file>: ViewportTitle = '<title>'`, or
   `<file>: the board returned no viewport.`

| Command | Parameters | Result |
|---|---|---|
| `GetViewport` | `TechnologySet=<name>` (optional, default `Cam350`) | `Technology set '<name>' supported: true/false` and `<file>: ViewportTitle = '<title>'`; without a PCB document, an error that still shows the licence line |

## How it works
`PcbViewportCommands.cs` first gets the client from `DXP.GlobalVars.Client` and looks up this extension's module
with `GetServerModuleByName`. When that module is not running (for example while the commands are hot-loaded
rather than installed, `GetServerModuleByName` returns null), it walks `GetServerModule(0..GetCount()-1)` and
takes the first module that casts to `DXP.IServerSecurity`. It then calls `IsTechnologySetSupported`.
The result is streamed with `ctx.Log` before the PCB part runs. The command then gets the board from
`PCB.GlobalVars.PCBServer.GetCurrentPCBBoard()`, calls `GetState_Viewport()`, and returns `GetState_ViewportTitle()`.
A missing PCB editor, board or viewport is reported in the text, not dereferenced.

## Notes
- Read-only: nothing on the board or in the licence changes.
- Every running server module, native or managed, casts to `DXP.IServerSecurity`, and all of them give the same
  answer: the technology set licence is a product-wide fact, so any module can be used.
- What `PCB.IPCB_Board.Viewport` returns is undocumented. It returns **null** on an ordinary open, active board
  (the SpiritLevel-SL1 example), without throwing. Callers must check it for null. `PCB.IPCB_Viewport` derives
  from `PCB.IPCB_RectangularPrimitive`, which suggests a primitive belonging to the board rather than the editor
  window.
- Valid technology set names are not listed in the SDK reference. `Cam350` is the default; its meaning is not
  documented. `Schematic_Editor` is one name that a typical licence reports as supported. An unknown name
  returns false and does not throw, so false does not distinguish "not licensed" from "no such technology set".
  Use the `ListTechnologySets` command of the TechnologySetLicenseCheck sample to see the names the running
  licence reports.
