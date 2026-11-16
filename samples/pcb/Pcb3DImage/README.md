# Switch a board to 3D and save the view as a bitmap

Switches the active PCB document to its 3D view by running the built-in `PCB:SwitchTo3D` process, then saves the
pixels the view window shows as a BMP file. It shows how to run a built-in process from an extension, how to read
the board's view mode, and how to get from the active view to its window handle and bounds for a GDI capture.

## What it shows
- `DXP.IClient.CurrentView`: the active document view, read with `GetCurrentView()`. Check it for null.
- `DXP.IServerDocumentView.GetOwnerDocument` / `DXP.IServerDocument.GetKind`: checks that the active document is
  a PCB document (kind `PCB`) before doing anything.
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document.
- `PCB.IPCB_Board.ViewConfigIs3D`: read with `GetState_ViewConfigIs3D()`, tells whether the board's view is in
  3D mode. The sample only runs the switch when it is not.
- `DXP.Utils.RunCommand(string, string)`: runs a built-in process by its `Server:Process` id, here
  `PCB:SwitchTo3D` with an empty parameter string.
- `DXP.IServerDocumentView.GetHandle` / `DXP.IServerDocumentView.GetBounds`: the view's window handle (as `uint`,
  widened to `IntPtr`) and its size, used for the capture.
- Win32 `GetWindowDC` / `BitBlt` / `ReleaseDC` (P/Invoke in `NativeMethods.cs`, not SDK APIs) into a
  `System.Drawing.Bitmap`.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer), make it the active document, and keep it visible (not minimised, not covered by another window).
2. **Pcb3DImage › Create PCB 3D Image**, or
   `tools/ad.ps1 -Sample Pcb3DImage run CreatePcb3DImage "Out=<folder or file.bmp>"`
3. The PCB view switches to 3D. The result names the BMP file written (default `Pcb3DImage.bmp` in the temp
   folder) and its size, says whether the switch was run and what `ViewConfigIs3D` reads afterwards, and lists the
   view name and the `GetBounds` values.

| Command | Parameters | Result |
|---|---|---|
| `CreatePcb3DImage` | `Out=<file or folder>` (optional, default: temp folder) | `Saved <w> x <h> bitmap of <board file> to <path>` plus a line with the 3D state, the view name and the GetBounds values |

## How it works
`Pcb3DImageCommands.cs` gets the active view with `DXP.GlobalVars.Client.GetCurrentView()` and checks that its
owner document's kind is `PCB`. It then gets the board with `GetCurrentPCBBoard()`. If
`GetState_ViewConfigIs3D()` is false it calls `DXP.Utils.RunCommand("PCB:SwitchTo3D", "")` and reads the flag
again. It re-reads the current view, converts `GetHandle()` to an `IntPtr` and reads `GetBounds`. A 24 bpp
`Bitmap` of that width and height receives the window's pixels: `BitBlt` copies from the window DC (`GetWindowDC`)
into the bitmap's DC (`Graphics.GetHdc`), and both DCs are released in a `finally` block. The bitmap is saved as
BMP. The output path comes from the `Out` parameter: a folder (existing, or ending with a separator) gets
`Pcb3DImage.bmp`, a name without an extension gets `.bmp`, and missing folders are created.

## Notes
- The view stays in 3D afterwards. This is a view state change, not a design change: the document is **not** marked
  modified by the switch. Switch back with the editor's 2D view command if needed.
- `RunCommand("PCB:SwitchTo3D", "")` is synchronous: it returns only after the 3D view
  has rendered. The capture taken on the next statement, with no wait and no message pumping, already shows the
  shaded 3D board, and `ViewConfigIs3D` reads `True` right after the call. A second run finds the flag still set and
  skips the switch.
- A 3D view is drawn by the graphics card, but `GetWindowDC` / `BitBlt` do read its pixels: the capture is not
  black.
- The capture copies what is on screen. Parts of the view covered by other windows, or outside the screen, come out
  wrong. The command does not check this.
- `GetWindowDC` covers the whole window, frame included. `GetBounds` returns the view window's
  own rectangle (`left=0 top=0` and its width and height, not screen coordinates), and `BitBlt` of that size copies
  the window in full. The view name reported is `PCBEditor`.
- `PCB:SwitchTo3D` and its parameters are not described in the SDK reference.
- Pass an absolute `Out` path. A relative path is resolved against Altium Designer's current folder.
