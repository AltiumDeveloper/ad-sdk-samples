# List the nets selected in the CAMtastic viewer

Checks that the active document is a CAMtastic document and reports it. Listing the nets selected in the CAMtastic
viewer needs native `CAMDXP.dll` exports that have no .NET SDK counterpart. The sample teaches how to identify the
active document and its editor with the managed API, and records the CAMtastic API gap.

## What it shows
- `DXP.IClient.CurrentView`: the active document view (`GetCurrentView()`), or none when no document is open.
- `DXP.IServerDocumentView.GetOwnerDocument`: the document behind the view.
- `DXP.IServerDocument.GetKind` and `DXP.IServerDocument.GetServerModule` / `DXP.IServerModule.GetModuleName`:
  the document kind and the editor server that owns it, used to tell a CAMtastic document from any other.
- The API gap: the .NET SDK has no CAMtastic types (no CAM document, net, net selection or net info).

## Run it
1. Open a CAMtastic (`.Cam`) document and make it the active document. Selecting nets in it makes no difference
   to this sample (see Notes).
2. `tools/ad.ps1 -Sample CamSelectedNetInfo run GetSelectedNets`. The **CamSelectedNetInfo › Get Selected Nets**
   menu is on the menu bar shown with no document open, not on the CAMtastic editor's menu bar (see Notes).
3. The command returns the file name, the document kind and the server name, followed by a line saying that the
   selected nets are not available. With no document, or a document of another kind, it fails with a message
   asking for a CAMtastic document.

| Command | Parameters | Result |
|---|---|---|
| `GetSelectedNets` | none | `<file>: CAMtastic document (kind '<kind>', server '<server>').` plus `Selected nets: not available. …` |

## How it works
`CamSelectedNetInfoCommands.cs` takes the active view from `DXP.GlobalVars.Client` (`GetCurrentView`), then its
document (`GetOwnerDocument`). It reads the document kind (`GetKind`) and the module name of the document's
server (`GetServerModule().GetModuleName()`). If neither equals `CAMTASTIC` (case-insensitive), it throws a
`UserException`. Otherwise it returns the document's identity and the statement that the net selection is out
of reach of the .NET SDK.

## Notes
- **API gap.** The selected nets are available only through four exports of the native `System\CAMDXP.dll`:
  `CAMAPI_GetCurrentDocHandle`, `CAMAPI_CountSelectedNets`, `CAMAPI_GetSelectedNetIDs` and
  `CAMAPI_GetNetInfoFromNetNo` (net number, name, length and nodes of each selected net). The .NET SDK has no managed
  equivalent. These samples do not P/Invoke into Altium Designer's native modules, so this sample does not list nets.
  The export signatures are not documented. A wrong signature, or a call with no CAM document open, could crash the
  Altium Designer process.
- The CAM document check compares the document kind and the server module name with `CAMTASTIC`, the name of the
  CAMtastic server (`Updates = 'CAMTASTIC'` in an `.ins`). The error message prints both values, so they can be read
  off when the check fails.
- `GetKind()` and `GetServerModule().GetModuleName()` both return `PCB` for a PcbDoc; for a text document they differ
  (`Text` / `TextEdit`).
- The menu item is on the shared no-document menu only, not on the CAMtastic editor's menu (`TargetID='MICAM2'`,
  `RefID0='MICAM70'`). Run the command with `tools/ad.ps1` while the CAM document is active.
- The command is read-only.
