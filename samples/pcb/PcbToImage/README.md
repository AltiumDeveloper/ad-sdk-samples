# Capture the PCB 3D view as a PNG image

Switches the active PCB document to its 3D view with the built-in `PCB:SwitchTo3D` process, then asks the view's
window to paint itself into a bitmap and saves it as PNG. It shows how to run a built-in process from an extension
and how to get the window handle of a document view for a Win32 capture.

## What it shows
- `DXP.IClient.CurrentView`: the focused document view (`GetCurrentView`), re-read after the view switch.
- `DXP.IServerDocumentView.GetOwnerDocument` and `DXP.IServerDocument.GetKind`: check that the view shows a PCB
  document (kind `PCB`) before doing anything.
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document.
- `PCB.IPCB_Board.ViewConfigIs3D`: whether the view is already in 3D, so the switch is run only when needed.
- `DXP.Utils.RunCommand`: run the built-in process `PCB:SwitchTo3D` by its `Server:Process` id with an empty
  parameter string.
- `DXP.IServerDocumentView.GetHandle`: the view's window handle, a `uint` that is widened to `IntPtr` for Win32.

## Run it
1. Open a `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer), make it the active document and keep its window visible (not minimized, not covered by another
   window).
2. **PcbToImage › Pcb To Image**, or `tools/ad.ps1 -Sample PcbToImage run PcbToImage "Out=<folder>|FullContent=true"`
3. The PCB view switches to 3D and the result reads
   `Saved <w> x <h> PNG of <board>.PcbDoc to <folder>\PcbToImage.png`, followed by a line with the 3D state, the
   PrintWindow flags and the window rectangle. The PNG shows the view window.

| Command | Parameters | Result |
|---|---|---|
| `PcbToImage` | `Out=<file or folder>` (optional, default: temp folder, file `PcbToImage.png`), `FullContent=true` (optional: PrintWindow with `PW_RENDERFULLCONTENT`) | `Saved <w> x <h> PNG of <board> to <path>` plus the 3D state and capture details |

## How it works
`PcbToImageCommands.cs`: the command reads `DXP.GlobalVars.Client.GetCurrentView()`, checks the owner document's kind
is `PCB`, and gets the board from `PCB.GlobalVars.PCBServer.GetCurrentPCBBoard()`. If `GetState_ViewConfigIs3D()` is
false it calls `DXP.Utils.RunCommand("PCB:SwitchTo3D", "")` and reads the flag again. It then re-reads the current
view, takes `GetHandle()`, and checks that the window is not minimized (`IsIconic`). `GetWindowRect` gives the size
of the window, frame included. A 24 bpp `System.Drawing.Bitmap` of that size receives the window's drawing through
`PrintWindow` on the bitmap's device context, which is released in a `finally`. The bitmap is saved as PNG and
disposed. The Win32 declarations are in `NativeMethods.cs`. They are plain Windows calls: the SDK has no
screenshot or image-export API for a document view.

## Notes
- The design is not changed (the document is not marked modified), but the editor view is left in 3D. The command
  does not switch back to 2D.
- **Use `FullContent=true` for the 3D view.** PrintWindow with the default flag returns true but captures only the
  GDI-drawn chrome of the view window: the GPU-rendered 3D canvas comes out black. With `FullContent=true`
  (`PW_RENDERFULLCONTENT`, Windows 8.1 and later) the same window captures the full 3D render. The default flag
  is 0.
- `GetHandle` returns the HWND of the **document view window**: the captured PNG holds the view toolbar, the 3D
  canvas and the layer bar, but not the Altium Designer frame or the docked panels.
- `ViewConfigIs3D` already reads `True` on the statement after `RunCommand("PCB:SwitchTo3D", "")`, so the process
  is synchronous at least up to the state change. Whether the 3D view has finished rendering when `RunCommand`
  returns is not documented.
- `GetWindowRect` returns the size in the pixels of the calling process's DPI awareness. On a scaled display the
  PNG may differ in size from the window as seen on screen.
- The window must be visible: a minimized view raises an error.
- The bitmap is 24 bpp, so areas GDI does not draw cannot come out transparent in the PNG.
