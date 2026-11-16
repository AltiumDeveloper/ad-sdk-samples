# Export PcbLib footprints as PNG thumbnails

Renders every footprint of a PCB library to a bitmap and saves it as a PNG file. It shows how to iterate a PCB
library, how `PCB.IPCB_ServerInterface.PaintFootprintThumbnail` turns a footprint into a GDI bitmap handle, and how
.NET turns that handle into an image file.

## What it shows
- `PCB.IPCB_ServerInterface.PaintFootprintThumbnail`: draws a footprint, given by name and library *file* path,
  into a new bitmap of the given size and returns its handle as a `uint` (0 when the footprint cannot be loaded).
- `PCB.IPCB_Library.LibraryIterator_Create` / `PCB.IPCB_Library.LibraryIterator_Destroy` with
  `PCB.IPCB_LibraryIterator.FirstPCBObject` / `NextPCBObject`: walking the footprints of a library.
- `PCB.IPCB_LibComponent.Pattern`: the footprint name, which is also the name `PaintFootprintThumbnail` expects.
- `DXP.IClient.GetDocumentByPath`, `DXP.IClient.OpenDocument`, `DXP.IClient.CloseDocument`: open a library file for
  the export and close it again only if it was not open before.
- `PCB.IPCB_ServerInterface.GetPCBLibraryByPath`, with `DXP.IClient.ShowDocument` +
  `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary` as fallback: getting the `IPCB_Library` of an open file.
- `System.Drawing.Image.FromHbitmap` and `Bitmap.Save(path, ImageFormat.Png)`: converting the handle to a PNG file.

## Run it
1. Copy a saved `.PcbLib` to a work folder (for example `Spirit_Level_Project.PcbLib` from the SpiritLevel-SL1
   example project that ships with Altium Designer), or open and focus any saved `.PcbLib`.
2. `tools/ad.ps1 -Sample PcbLibToImage run GenerateImage "Lib=<work folder>\Spirit_Level_Project.PcbLib|Out=<work folder>\img"`,
   or, with a PcbLib focused, **PcbLibToImage › Generate Image** (exports the focused library to the temp folder).
3. The result reads `Spirit_Level_Project.PcbLib (GetPCBLibraryByPath): N of N footprints written as 500x500 PNG
   to <work folder>\img\Spirit_Level_Project` (N is the library's footprint count), and that folder holds one
   `<footprint>.png` per footprint.

| Command | Parameters | Result |
|---|---|---|
| `GenerateImage` | `Lib=<.PcbLib path>` (default: the focused PCB library), `Out=<folder>` (default: temp folder), `Size=<pixels>` (1 to 4096, default 500) | `<lib> (<how found>): <written> of <footprints> footprints written as <size>x<size> PNG to <Out>\<lib name>`, plus a line listing footprints whose thumbnail failed |

## How it works
`PcbLibToImageCommands.cs` validates `Size`, then finds the library. Without `Lib` it takes
`GetCurrentPCBLibrary` and the file name of its board. With `Lib` it opens the file with `OpenDocument("PCBLib", …)`
unless `GetDocumentByPath` shows it is already open, then asks `GetPCBLibraryByPath` for the library. If that returns
nothing it falls back to `ShowDocument` and `GetCurrentPCBLibrary`, and checks that the current
library is the requested file. The library must exist on disk, because `PaintFootprintThumbnail` takes a file path.

The command creates `<Out>\<library name>` and iterates the library. For each `IPCB_LibComponent` it calls
`PaintFootprintThumbnail(pattern, libraryPath, size, size)`. The call is made late-bound with `Type.InvokeMember`,
because the typed call throws `OverflowException` for some footprints (the declared `uint` result
does not convert). It converts the raw result to a sign-extended `IntPtr`, copies it into a `Bitmap` with `Image.FromHbitmap` and saves it as PNG. The iterator is destroyed and a document the command opened is closed in a `finally` block.

## Notes
- File names are the footprint names without characters Windows forbids in file names. Names that become equal
  after that get `_2`, `_3`. Existing files are overwritten.
- Open point: who frees the bitmap handle is not documented. The reference says the method draws "into a new
  bitmap", which suggests the caller owns it, but the .NET API has no call to free it (disposing the `Bitmap` made
  by `Image.FromHbitmap` does not). The sample doesn't free it. If the caller does own it, each
  footprint leaks one GDI object until AD exits. Windows limits a process to 10,000 GDI objects by default, so very
  large libraries or many runs could exhaust them.
- `GetPCBLibraryByPath` returns the library of a document opened with `OpenDocument` that was never shown,
  so the `ShowDocument` fallback is normally not needed. The result text names the way used.
- Open point: whether `PaintFootprintThumbnail` reads the file on disk or the open document. Unsaved edits may not
  show in the images.
- The handle is returned as `uint`. Only the lower 32 bits of a Windows handle are significant, so sign-extending it
  (`new IntPtr(int)`) gives a valid 64-bit handle.
- The menu is inserted into the PCB editor menu (`MNPCBMenu`). `tools/ad.ps1` does not depend on it.
