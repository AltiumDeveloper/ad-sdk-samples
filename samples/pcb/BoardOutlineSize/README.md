# Report a board outline's bounding width and height

Opens a `.PcbDoc` by path, or takes the current board, and reports the width and height of the board outline's
bounding rectangle. It shows how to get a board by file path, and how to read and convert PCB coordinates.

## What it shows
- `DXP.IClient.OpenDocument` with kind `"PCB"`: loads a PCB document from disk into the workspace.
  `DXP.IClient.GetDocumentByPath` tells whether it is already open.
- `PCB.IPCB_ServerInterface.GetPCBBoardByPath`: the board of an open PCB document, by file name. It returns null
  for a document that is not open, so the document is opened first.
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document (used when `PCBFile` is not given).
- `PCB.IPCB_Board.BoardOutline`: the board outline, a `PCB.IPCB_BoardOutline` (a polygon).
- `PCB.IPCB_Primitive.BoundingRectangle`: returns a `PCB.CoordRect` (`Lx`, `Ly`, `Hx`, `Hy`).
- `EDP.Utils.CoordToMils` / `EDP.Utils.CoordToMMs`: internal units (10,000 per mil) to mils and millimetres.

## Run it
1. Have a `.PcbDoc` on disk, for example a copy of the board of the SpiritLevel-SL1 example project that ships
   with Altium Designer. Or open one and make it the active document.
2. **BoardOutlineSize › Get Pcb Size** (current board), or
   `tools/ad.ps1 -Sample BoardOutlineSize run GetPcbSize "PCBFile=<path to .PcbDoc>"`
3. The result reads `<board>.PcbDoc (opened, left open): Width = <w> mil (<mm> mm, <raw>), Height = <h> mil (...)`,
   followed by the outline bounds in internal units.

| Command | Parameters | Result |
|---|---|---|
| `GetPcbSize` | `PCBFile=<path>` (optional; default: the current board), `Out=<file or folder>` (optional) | `<board>: Width = … mil (… mm, <raw>), Height = …`, the bounds `Lx Ly Hx Hy`; with `Out` also `Written to <path>` |

## How it works
`BoardOutlineSizeCommands.cs`: `GetPcbSize` gets `PCB.GlobalVars.PCBServer`. With `PCBFile` it makes the path
absolute, checks that the file exists, and calls `OpenDocument("PCB", path)` unless `GetDocumentByPath` already finds
the document. Then it gets the board with `GetPCBBoardByPath`. Without `PCBFile` it uses `GetCurrentPCBBoard`. It reads
`GetState_BoardOutline().BoundingRectangle()`, computes width = `Hx - Lx` and height = `Hy - Ly`, and formats them
with `EDP.Utils.CoordToMils` and `EDP.Utils.CoordToMMs`, keeping the raw values. `Out` writes a single
line `Width=<n>|Height=<n>` (internal units) to the given file, or to `BoardOutlineSize_<board>.txt` in a given folder.

## Notes
- Read-only: the board is not changed. A document opened by the command stays open in the workspace. Close it
  yourself if you don't need it.
- The size is of the outline's bounding rectangle, not its area or shape.
- `OpenDocument` is called without `ShowDocument`. That is enough for `GetPCBBoardByPath` to return the board,
  when the full normalized path is passed. The command fails with a clear message when no board is found.
- Millimetres come from `EDP.Utils.CoordToMMs` (1 mil = 0.0254 mm) rather than a hand-written factor, which is
  easy to get wrong.
- The docs' example for `PCB.IPCB_BoardOutline` calls `Invalidate`/`Rebuild`/`Validate` on the outline before reading
  it. This sample reads the outline without those calls.
- For reference, the SpiritLevel-SL1 board measures 7087.038 x 1988.613 mil (180.0108 x 50.5108 mm,
  70870378 x 19886126 internal units).
