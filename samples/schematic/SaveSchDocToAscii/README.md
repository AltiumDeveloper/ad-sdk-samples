# Save a schematic document in ASCII format

Saves the active schematic sheet to another file in the "Advanced Schematic ascii" format. It shows the generic
document container, `DXP.IServerDocument`, and how `DoFileSave` picks an output format by the name of a save filter.

## What it shows
- `DXP.IClient.CurrentView` and `DXP.IServerDocumentView.GetOwnerDocument`: from the focused view to its document
  (both are extension members; the view is null when no document is focused).
- `DXP.IServerDocument.GetKind`: the document kind string, used to refuse anything that is not a schematic sheet.
- `DXP.IServerDocument.FileName`: `DoFileSave` takes no file name, so the document is pointed at the target with
  `SetFileName` first, and back at its own file afterwards.
- `DXP.IServerDocument.DoFileSave`: saves the document with a named save filter (`Advanced Schematic ascii`).
- `DXP.IServerDocument.Modified`: read before the save and restored after it, so a copy on disk does not mark
  unsaved edits as saved.

## Run it
1. Open a schematic sheet (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with
   Altium Designer, copied to a work folder) and make it the active document.
2. **SaveSchDocToAscii › Save To Ascii**, or
   `tools/ad.ps1 -Sample SaveSchDocToAscii run SaveToAscii "Out=<folder or .SchDoc path>"`
3. The result names the file written, its size and whether it is text (with its first characters) or a binary OLE
   file. The open sheet keeps its own file name.

| Command | Parameters | Result |
|---|---|---|
| `SaveToAscii` | `Out=<file or existing folder>` (default: temp folder, `<sheet>_Ascii.SchDoc`), `Format=<save filter>` (default `Advanced Schematic ascii`), `Keep=true` (leave the open document pointing at the new file) | `DoFileSave('<format>') returned <bool>. Wrote <path> (<n> bytes, text, starts with '...'). The open document still points at <sheet>.` |

## How it works
`SaveSchDocToAsciiCommands.cs` gets the current view from `DXP.GlobalVars.Client`, its owner document, and checks
that `GetKind()` is `SCH` (case-insensitive). It works out the target path from `Out` (a folder gets
`<sheet>_Ascii.SchDoc`; the document's own file is refused), remembers the document's file name and `Modified`
flag, calls `SetFileName(target)` and `DoFileSave(format)`. In a `finally` block it restores the file name and
the flag unless `Keep` is set. It then reads the first 64 bytes of the target: the OLE signature `D0 CF 11 E0`
means a binary SchDoc, printable bytes are reported as text.

## Notes
- The command writes a file. With `Keep=true` the open sheet is re-pointed at the new file, so later saves go to
  the new name. Close it without saving to undo that.
- This sample restores the file name and the `Modified` flag after the save. The document stays open, is listed
  by the client under its own path afterwards and its own file is not rewritten.
- The format names come from the schematic editor's save filters. "Advanced Schematic ascii" and "Advanced
  Schematic binary" are accepted and really select the format (text vs. OLE compound file); the reference also
  lists "Schematic binary 4.0", "Orcad SDT Schematic" and "Advanced Schematic template".
- `DoFileSave` returns `True` for a made-up filter name such as "No Such Format" and
  writes no file, without any dialog: the `bool` is not a success indicator, which is why this command reports the
  file it finds on disk.
- `GetKind()` returns `PCB` for a board document, which the command refuses with a message.
- `DXP.IServerDocument.DoSafeChangeFileNameAndSave(newFileName, kind)` could do the rename and the save in one call;
  its behaviour is undocumented and this sample does not use it.
- This command does not prompt before it overwrites an existing target file.
