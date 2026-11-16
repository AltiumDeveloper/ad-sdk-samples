# Build a text document with ITextCommands

Creates a new Text document and fills it with editor commands: cursor moves, text and line insertion, and line
selection. It shows how to drive a text editor through `EDP.ITextCommands`, the same commands a user runs with
the keyboard, and how to create a new document of a given kind.

## What it shows
- `DXP.Utils.CreateNewDocumentFromDocumentKind`: create a new document of the kind `Text` and get its
  `DXP.IServerDocument`.
- `EDP.ITextCommands.MoveCursorToBeginningOfLine`, `EDP.ITextCommands.MoveCursorToEndOfLine`,
  `EDP.ITextCommands.MoveCursorToTopOfDocument`: move the cursor. Every command takes its parameter string by
  `ref`.
- `EDP.ITextCommands.InsertText`: insert the text passed as the parameter string at the cursor.
- `EDP.ITextCommands.InsertReturn` / `EDP.ITextCommands.InsertLine`: break the line at the cursor, or open an
  empty line at the cursor's line.
- `EDP.ITextCommands.SelectLine`: select the cursor's line.
- `DXP.ITextDocument.LineCount`, `DXP.ITextDocument.GetLine`, `DXP.ITextDocument.GetSelectedText`: read the
  result back as text.
- SDK interfaces are dispatch interfaces: a cast to `EDP.ITextCommands` always succeeds, and only the first call
  shows whether the object implements it.

## Run it
1. No document is needed; any workspace state.
2. **TextDocumentCommands › Text Command**, or `tools/ad.ps1 -Sample TextDocumentCommands run TextCommand`
3. A new unsaved Text document is created and becomes the active tab. It holds `Line before first line`
   (selected), `First Line`, `Second Line` and an empty last line. The returned text names the object that took the
   commands and lists the lines read back.

| Command | Parameters | Result |
|---|---|---|
| `TextCommand` | none | `Created a new Text document '<name>' and filled it via ITextCommands on the <IServerDocument or IServerDocumentView>.`, then the line count, each line, and the selected text |

## How it works
`TextDocumentCommandsCommands.cs` calls `DXP.Utils.CreateNewDocumentFromDocumentKind("Text")`. It casts the
returned document to `EDP.ITextCommands` and probes it with `MoveCursorToBeginningOfLine`, which changes nothing
in an empty document. If that call throws a `COMException`, it tries the document's first view
(`DXP.IServerDocument.GetView(0)`) instead. It then runs the command sequence: write `First Line`, end of
line, return; write `Second Line`, end of line, return; go to the top, insert a line, write
`Line before first line`, select the line. Each call gets a fresh empty `ref string`, except `InsertText`,
which gets the text. Finally it casts the document to `DXP.ITextDocument` and returns the lines and the
selection. If that cast doesn't work, it returns the error message instead.

## Notes
- The document is not saved. Close it without saving, or save it to keep it.
- Each run creates a new document.
- The `IServerDocument` implements `EDP.ITextCommands` itself, so the fallback to the view is not needed. The
  same document implements `DXP.ITextDocument`. The line count is 4 because it includes the empty line after the
  trailing `InsertReturn`. The selected text includes the first line's line break.
  `DXP.Utils.CreateNewDocumentFromDocumentKind` names the new document `Doc<n>.Txt`, shows it and makes it the
  active tab, without a `ShowDocument` call.
- In .NET the cast to an SDK interface always succeeds, so the sample tests it with a first call instead.
