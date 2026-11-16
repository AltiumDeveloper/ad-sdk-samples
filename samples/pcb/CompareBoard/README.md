# Compare two PCB documents layer by layer

Opens two `.PcbDoc` files, gets their boards and compares them layer by layer with the PCB server's primitive
comparator, then reports the comparator's raw result. It shows how to get boards of files that are not the
current document, and how to call `PCB.IPCB_PrimitiveComparator.Compare`.

## What it shows
- `DXP.IClient.OpenDocument`: opens a file as a server document of kind `"PCB"` (or returns it when it is already
  open). `PCB.IPCB_ServerInterface.GetPCBBoardByPath` only finds boards of open documents.
- `DXP.IClient.ShowDocument`: shows the opened documents, so the differences can be seen (optional, `Show=false`).
- `PCB.IPCB_ServerInterface.GetPCBBoardByPath`: the board of an open PCB document by its file name, or null.
- `PCB.IPCB_ServerInterface.PrimitiveComparator`: the comparator object.
- `PCB.IPCB_PrimitiveComparator.Compare`: compares the boards layer by layer. Per the reference it returns true when
  no differences are found, and switches on the display of difference objects in the PCB editor. Its tolerance is an
  area in square internal units (1 square mil = 100,000,000). It returns `false` for two byte-identical copies of the
  same file, so do not read a `false` result as proof that the boards differ (see Notes).

## Run it
1. Have two `.PcbDoc` files on disk. No document needs to be open.
2. **CompareBoard › Compare Board** (pick the two files in the file dialogs), or
   `tools/ad.ps1 -Sample CompareBoard run CompareBoard "BoardA=<path>|BoardB=<path>"`
3. Both documents open and BoardA is focused. The result is `Compare(<A>, <B>, 0) returned true: no differences
   found.` or `... returned false: the comparator reports differences.` The text names the raw result, because
   `false` also comes back for two identical copies (see Notes).

| Command | Parameters | Result |
|---|---|---|
| `CompareBoard` | `BoardA=<path to .PcbDoc>`, `BoardB=<path to .PcbDoc>` (required with `tools/ad.ps1`; from the menu without parameters: file dialogs), `Tolerance=<square internal units>` (default `0`), `Show=false` (optional: open the documents without showing them) | `Compare(<A>, <B>, <tolerance>) returned true: no differences found.` or `... returned false: the comparator reports differences. ...` |

The command fails with a clear message when a path is missing, a file does not exist, both paths name the same
file, or `Tolerance` is not a number >= 0.

## How it works
`CompareBoardCommands.cs` resolves both paths (parameter or file dialog), checks that the files exist and are not the
same file, and parses `Tolerance` (invariant culture, a number >= 0). It opens both files with
`DXP.IClient.OpenDocument("PCB", path)` and, unless `Show=false`, shows B and then A with `DXP.IClient.ShowDocument`.
It then reads `PCB.GlobalVars.PCBServer` (the PCB server is loaded with the first PCB document), gets both boards with
`GetPCBBoardByPath`, gets the comparator with `PrimitiveComparator()` and calls `Compare(boardA, boardB, tolerance)`.
An exception from `Compare` (the reference says Esc cancels it by raising one) is reported as a `UserException`. The
returned text names the raw `bool` result.

## Notes
- The files are not changed, but the editor state is: the documents stay open and `Compare` switches on difference
  objects. They are drawn in the view of `BoardA`, over the areas that differ, neither document is marked as modified
  by the comparison, and nothing is written to the Messages panel. Close the documents without saving to discard
  everything.
- `true` does not follow from equal content: two byte-identical copies of the same `.PcbDoc` compare as `false` (also
  with a very large tolerance), while `Compare(board, board, tolerance)` on one board returns `true`. The command's
  text therefore names the raw value.
- `Compare` shows progress and blocks Altium Designer while it runs; on the SpiritLevel-SL1 board it takes about 12 s.
- `GetPCBBoardByPath` is called with the same full path that was passed to `OpenDocument` (normalised with
  `Path.GetFullPath`; full path, spaces and mixed case work).
- `Show=false` only opens the documents, without showing them.
