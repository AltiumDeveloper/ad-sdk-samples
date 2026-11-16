# Create a text document and edit its lines

Creates a new Text document, writes three lines into it with editor commands, then replaces text in every line by
index. It shows the two text-editing interfaces side by side: `EDP.ITextCommands` (cursor-based editing, like the
keyboard) and `DXP.ITextDocument` (direct line access with `GetLine`/`SetLine`).

## What it shows
- `DXP.Utils.CreateNewDocumentFromDocumentKind`: create a new document of the kind `Text` and get its
  `DXP.IServerDocument`.
- `EDP.ITextCommands.MoveCursorToBeginningOfLine`, `EDP.ITextCommands.MoveCursorToEndOfLine`,
  `EDP.ITextCommands.MoveCursorToTopOfDocument`, `EDP.ITextCommands.InsertText`, `EDP.ITextCommands.InsertReturn`,
  `EDP.ITextCommands.InsertLine`, `EDP.ITextCommands.SelectLine`: write the content at the cursor. Every command
  takes its parameter string by `ref`; for `InsertText` it is the text to insert.
- `DXP.ITextDocument.LineCount`: the number of lines. It is a method, not a property.
- `DXP.ITextDocument.GetLine` (`ref string` out-value) and `DXP.ITextDocument.SetLine`: read and replace a line
  by its zero-based index.
- SDK interfaces are dispatch interfaces: a cast of the document to `EDP.ITextCommands` or `DXP.ITextDocument`
  always succeeds, and only the first call shows whether the object implements it.

## Run it
1. No document is needed; any workspace state.
2. **TextDocumentEdit › Replace Text**, or `tools/ad.ps1 -Sample TextDocumentEdit run ReplaceText`
3. A new unsaved Text document is created. The returned text lists the lines before the replacement
   (`Line before first line`, `First Line`, `Second Line`, possibly an empty last line) and after it, where each
   `line` (any case) became `LINE1`, a line break, `LINE2`. Line breaks inside a line are shown as `\r\n`.

| Command | Parameters | Result |
|---|---|---|
| `ReplaceText` | `Find=<text>` (default `line`, case-insensitive), `Replace=<text>` (default `LINE1\r\nLINE2`; `\r`, `\n`, `\t` are expanded) | `Created Text document '<name>' (ITextCommands on the <IServerDocument or IServerDocumentView>).`, the lines before, `Replaced '<find>' in <n> line(s). After: <m> line(s)`, the lines after |

## How it works
`TextDocumentEditCommands.cs` reads `Find` and `Replace`, then calls
`DXP.Utils.CreateNewDocumentFromDocumentKind("Text")`. It casts the document to `EDP.ITextCommands` and makes the
first call, `MoveCursorToBeginningOfLine`, as a probe; if that throws a `COMException`, it uses the document's
first view (`DXP.IServerDocument.GetView`) instead. It then runs the command sequence: write `First Line`, end of
line, return; write `Second Line`, end of line, return; top of document, insert a line, write
`Line before first line`, select the line.

Next it casts the same document to `DXP.ITextDocument`, reads `LineCount()` and lists the lines with `GetLine`.
It walks the lines from the last to the first, replaces the text case-insensitively, and writes back only the
changed lines with `SetLine`. Finally it reads `LineCount()` and the lines again.

## Notes
- The document is not saved, and each run creates a new one. Close it without saving.
- The `IServerDocument` itself implements both `EDP.ITextCommands` and `DXP.ITextDocument` (the view fallback is
  not needed). After the trailing `InsertReturn`, `LineCount()` is 4, and `GetLine` returns **null** (not an empty
  string) for the empty last line, so the code checks for null; the listing shows it as `(null)`. `SetLine` with
  an embedded CRLF does not split the line: the line count stays 4, `GetLine` returns the CRLF inside the line, and
  the editor draws the break as two blank characters on one line. `CreateNewDocumentFromDocumentKind` shows the
  new document (`DocN.Txt`) and makes it the active editor without a `ShowDocument` call.
- Because `SetLine` does not split lines, the reverse loop is not needed to keep the indexes valid; it keeps the
  indexes valid for an editor whose `SetLine` would split a line.
- `SelectLine` has no effect on the replacement; it is kept to show the command.
- `EDP.ITextCommands` is shown on its own in the `TextDocumentCommands` sample (the same command sequence).
