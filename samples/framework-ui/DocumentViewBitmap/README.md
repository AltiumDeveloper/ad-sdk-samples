# Save the active document view as a bitmap

Copies the pixels the current document view shows on screen into a BMP file. It shows how to get from the SDK's
active view to its Win32 window handle and bounds, which plain GDI then uses to grab the image. Works for any
document kind.

## What it shows
- `DXP.IClient.CurrentView`: the active document view, read with `GetCurrentView()`. It is null when no document is
  open, so check it.
- `DXP.IServerDocumentView.GetHandle`: the view's window handle, returned as `uint`. Widen it to `IntPtr` before
  passing it to Win32.
- `DXP.IServerDocumentView.GetBounds`: left, top, width and height of the view. The sample sizes the bitmap from
  the width and height.
- `DXP.IServerDocumentView.GetOwnerDocument` / `DXP.IServerDocument.FileName`: names the document in the result.
- Win32 `GetWindowDC` / `BitBlt` / `ReleaseDC` (P/Invoke in `NativeMethods.cs`, not SDK APIs) into a
  `System.Drawing.Bitmap`.

## Run it
1. Open any document (for example a `.SchDoc` or `.PcbDoc` of an example project that ships with Altium Designer,
   such as SpiritLevel-SL1), make it the active document, and keep it visible (not minimised, not covered by
   another window).
2. **DocumentViewBitmap › Create Images**, or
   `tools/ad.ps1 -Sample DocumentViewBitmap run CreateImages "Out=<folder or file.bmp>"`
3. The result names the BMP file written (default `DocumentViewBitmap.bmp` in the temp folder) and its size. It
   also lists the values from `GetBounds` and the size of the window's visible area as GDI+ reports it.

| Command | Parameters | Result |
|---|---|---|
| `CreateImages` | `Out=<file or folder>` (optional, default: temp folder) | `Saved <w> x <h> bitmap of view '<view>' (<document>) to <path>` plus a line with the GetBounds values and the visible clip bounds of the window |

## How it works
`DocumentViewBitmapCommands.cs` gets the active view with `DXP.GlobalVars.Client.GetCurrentView()`. It throws a
`UserException` when there is no view, no window handle, or empty bounds. It
converts `GetHandle()` to an `IntPtr` and reads `GetBounds`. Then it creates a 24 bpp `Bitmap` of that width and
height. `BitBlt` copies from the window DC (`GetWindowDC`) into the bitmap's DC (`Graphics.GetHdc`), starting at
the top left of the window. Both DCs are released in a `finally` block. The bitmap is saved as BMP, and
`Graphics.FromHwnd(...).VisibleClipBounds` gives the size reported next to the bounds. The output
path comes from the `Out` parameter: a folder (existing, or ending with a separator) gets `DocumentViewBitmap.bmp`,
a name without an extension gets `.bmp`, and missing folders are created.

## Notes
- The capture copies what is on screen. Parts of the view covered by other windows, or outside the screen, come out
  wrong. The command does not check this: a minimised view gives a meaningless image.
- `GetWindowDC` covers the whole window, frame included. `GetDC` would cover the client area
  only. With a schematic view at 200% display scaling, `GetBounds` returns left=0, top=0,
  and the window's full size in device pixels. The visible clip bounds of the window (`Graphics.FromHwnd`) are the
  same size. The view window has no frame, so the capture holds the sheet, the floating toolbar and the "Editor" tab
  strip, without the document tabs. Left/top are not screen coordinates.
- The `<w> x <h>` in the result is the size taken from `GetBounds`, not a size measured on the window DC. If the
  bounds are larger than the window, the extra pixels come from outside it.
- The bitmap is 24 bpp: it has no alpha channel, so the GDI copy cannot come out transparent.
- Pass an absolute `Out` path. A relative path is resolved against Altium Designer's current folder.
- The command changes nothing in the design. It only writes the BMP file.
