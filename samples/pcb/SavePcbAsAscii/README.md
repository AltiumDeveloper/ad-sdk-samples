# Save the active PCB as ASCII without changing its state

Saves a copy of the active PCB document in the "PCB ASCII File" format and leaves the open document as it was: same
file name, same Modified flag, same file modified date. It teaches the "save a copy" pattern: a temporary file name
swap around `DXP.IServerDocument.DoFileSave`, inside `DXP.IClient.BeginRecoverySave` / `EndRecoverySave`.

## What it shows
- `DXP.IClient.CurrentView` with `DXP.IServerDocumentView.GetOwnerDocument` and `DXP.IServerDocument.GetKind`: find
  the active document and check that it is a PCB.
- `DXP.IServerDocument.FileName`, `DXP.IServerDocument.Modified`, `DXP.IServerDocument.FileModifiedDate`: the
  document state to remember before the save and restore afterwards. `FileName` has a setter (`SetFileName`).
- `DXP.IServerDocument.DoFileSave`: save in a named format, here `"PCB ASCII File"`.
- `DXP.IClient.BeginRecoverySave` / `DXP.IClient.EndRecoverySave`: documented to suppress the client's notification
  of document name changes. `DXP.IClient.InRecoverySave` tells whether one is already running.
- `DXP.IServerDocument.UpdateModifiedDate` and `DXP.IClient.IsDocumentOpen`: to update the date around the save, and to
  refuse a target that is an open document.
- Win32 window automation (not SDK): a background thread that closes the "Information" dialog the ASCII save shows.

## Run it
1. Open a `.PcbDoc` (a copy, for example of the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document.
2. **SavePcbAsAscii › Save As ASCII**, or `tools/ad.ps1 -Sample SavePcbAsAscii run SaveAsAscii "Out=<folder>"`
3. The result names the file written (`<folder>\<board>.ascii.PcbDoc`, by default in the temp folder), says whether
   its start is ASCII text, and compares the document's file name, Modified flag and date before and after
   (`Document state unchanged: ...`).

| Command | Parameters | Result |
|---|---|---|
| `SaveAsAscii` | `Out=<folder or .PcbDoc file>` (default: temp folder), `AutoDismiss=false` (default on), `DialogTitle=<title>` (default `Information`) | `DoFileSave` result, file path, size, ASCII check and first line, before/after state, dialogs dismissed |
| `DocumentState` | none | `<path>: Modified=<bool>, FileModifiedDate=<date> (<raw>)` |
| `SetModified` | `Modified=true\|false` (default `true`) | `<file>: Modified=<bool>` (helper to set up a state) |

## How it works
`Commands/SavePcbAsAsciiCommands.cs`, `SaveAsAscii`:
1. Gets the active document from `DXP.GlobalVars.Client` and checks `GetKind()` is `PCB`.
2. Remembers `GetFileName()`, `GetModified()` and `GetFileModifiedDate()`.
3. Works out the target from `Out` and refuses the document's own path, any open document and a running recovery
   save. An existing file at the target is deleted, so the checks afterwards only see this save.
4. `BeginRecoverySave`, then `SetFileName(target)`, `UpdateModifiedDate()`, starts the dialog dismisser and calls
   `DoFileSave("PCB ASCII File")`.
5. In `finally`: stops the dismisser, restores the file name, Modified flag and date, and calls `EndRecoverySave`.
6. Reads the first 512 bytes of the file and the document state back, and reports both.

`DialogDismisser.cs` polls every 100 ms with `EnumWindows` for a new visible popup window of AD's process with the
given title (a `WS_POPUP` window, or class `TMessageForm` / `#32770`). It posts `BM_CLICK` to a child of class
`Button`/`TButton`, and sends `WM_CLOSE` if the dialog has none or is still open a second after the click. The
dialog's OK button is a `TXPBitBtn`, which ignores a posted `BM_CLICK`, so the dialog is closed with
`WM_CLOSE`. `NativeMethods.cs` holds the Win32 declarations.

## Notes
- `"PCB ASCII File"` is the format name (the SDK reference lists it as "PCB ASCII"), and the save
  shows a modal "Information" message box every time. Not known: whether the rename fires notifications despite the
  recovery save.
- The written copy's first record (`FILENAME=`) holds the copy's own path, the temporary name the document had
  during the save; the open document itself ends unchanged.
- If the dialog is not dismissed (`AutoDismiss=false`, or a title that does not match), the call does not return
  and the document stays under the temporary name until someone clicks OK; a caller that is not a human is blocked.
  The dismisser matches the dialog by its title, which depends on the UI language: pass `DialogTitle` for a
  non-English AD.
- The dismisser only clicks windows that open while the save runs, each once, and is stopped (2 s wait) before the
  document state is restored. It never throws out of its thread, since that would end AD's process.
- `FileModifiedDate` is a double. This sample shows it as an OLE Automation date (TDateTime) and prints the raw value
  too. The reference does not document this.
- `Out` overwrites `<board>.ascii.PcbDoc` (or the `.PcbDoc` you name) without asking. Delete the copy when done.
- `DocumentState` and `SetModified` are helpers to check the result with the Modified flag both on and off.
