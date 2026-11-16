# Open a PCB drawing and update it from the board

Opens a Draftsman drawing (`.PCBDwf`), focuses it, and runs the Draftsman process `PcbDrawing:UpdateBoard`
(**Tools › Import Changes From Board**) on it. It shows how an extension opens another editor's document by its
kind and then drives that editor through one of its processes.

## What it shows
- `DXP.IClient.OpenDocument`: open a file as a document of a given kind. The kind of a `.PCBDwf` is `PcbDrawing`.
- `DXP.IClient.GetDocumentByPath`: find a document that is already open, so it is reused instead of opened again.
- `DXP.IServerDocument.GetKind`: check that the opened document really is a drawing.
- `DXP.IClient.ShowDocument`: show and focus that exact document. No index into the hidden-documents list is needed.
- `DXP.IClient.CurrentView` with `DXP.IServerDocumentView.GetOwnerDocument`: confirm which document has the
  focus before a process runs, since processes act on the focused document.
- `DXP.Utils.RunCommand`: run another server's process (`PcbDrawing:UpdateBoard`) with a parameter string.
- `DXP.IServerDocument.Modified`: read back whether the process changed the document, since `RunCommand` returns
  nothing.

## Run it
1. Open a project that has a board and a drawing made for it, for example a copy of the `Mini PC - WiFi` example
   project that ships with Altium Designer (`WiFi_miniPCIe.PrjPcb`, with `WiFi.PcbDoc` and `WiFi.PCBDwf`).
2. **PcbDrawingUpdate › Open Drawing** and choose the `.PCBDwf`, or
   `tools/ad.ps1 -Sample PcbDrawingUpdate run OpenDrawing "File=<folder>\WiFi.PCBDwf"`
3. The drawing opens in the Draftsman editor and imports the changes from its board. The command returns the
   file, its kind, whether it was already open, and the modified flag before and after the update.

| Command | Parameters | Result |
|---|---|---|
| `OpenDrawing` | `File=<.PCBDwf>` (required from the command line; the menu asks with a file dialog), `Update=false` (optional) | `<file>: kind 'PcbDrawing', opened\|was already open, shown and focused. Modified before update: <bool>. Ran PcbDrawing:UpdateBoard. Modified after update: <bool>. ...` |

## How it works
`PcbDrawingUpdateCommands.cs` checks the `File` parameter. The file must exist and end in `.PCBDwf`. With no
`File`, the menu shows a file dialog and a command-line call fails with a clear message. The command then looks for
the drawing among the open documents (`GetDocumentByPath`) and opens it with `OpenDocument("PcbDrawing", file)` if
it is not open. It checks the kind, and focuses the drawing with `ShowDocument`. Before it runs
`RunCommand("PcbDrawing:UpdateBoard", "")`, it confirms that the current view's owner document is the drawing, so
the process cannot hit another document. The result reports `GetModified()` before and after the update.
`Update=false` stops after the focus step.

## Notes
- Nothing is saved. Save the drawing to keep the update, or close it without saving to discard it.
- If the drawing is already open, the update runs on that open document, including any unsaved edits in it.
- The kind `PcbDrawing` and the process `PcbDrawing:UpdateBoard` come from the Draftsman extension files
  (`Altium.Designer.PcbDrawing.ins` / `.rcs`). They are strings, not SDK members, and the SDK reference doesn't
  list them.
- `OpenDocument` opens the drawing but does **not** focus or raise it: the document
  that had the focus keeps it. The `ShowDocument` call is what makes the drawing the current view, and
  `RunCommand` acts on the focused document.
- `PcbDrawing:UpdateBoard` runs synchronously, shows no dialog, and sets the drawing's
  modified flag to `True` without saving. As a side effect it opens the board the drawing references (`WiFi.PcbDoc`
  in the example project).
- `PcbDrawing:UpdateBoard` takes no documented parameters, and this sample passes none.
