# Render each schematic component to an image file

Saves a PNG thumbnail of every component on the current schematic sheet and lists the files written. It shows how
`SCH.ISch_ServerInterface.PaintLoadedComponentThumbnail` draws a placed component into a GDI bitmap, and how .NET turns
the returned handle into an image file.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document, or null when the active document
  is not a schematic.
- `SCH.ISch_Document.SchIterator_Create` / `SCH.ISch_Document.SchIterator_Destroy`: iterate the sheet's objects and
  destroy the iterator in a `finally`.
- `SCH.ISch_Iterator.AddFilter_ObjectSet` with `SCH.TObjectId.eSchComponent`: visit components only.
- `SCH.ISch_BasicContainer.ObjectId`: check the object's kind before casting, because a type test succeeds for every
  SDK object.
- `SCH.ISch_Component.SymbolReference`: the library symbol name, used as the file name.
- `SCH.ISch_Component.SchDesignator`: the designator, shown in the result and used to keep file names unique.
- `SCH.ISch_ServerInterface.PaintLoadedComponentThumbnail`: draws a placed component into a GDI bitmap of the given
  pixel size and returns the bitmap's handle (0 when nothing was drawn). The handle belongs to the caller, and the
  SDK's declared `uint` return type cannot carry it. See the two notes below.

## Run it
1. Open a `.SchDoc` with components, for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships
   with Altium Designer, and make it the active document.
2. **SchComponentToImage › Generate Sch Comp Images**, or
   `tools/ad.ps1 -Sample SchComponentToImage run GenerateSchCompImages "Out=<folder>|Size=300"`
3. The result starts with `<sheet>: <n> of <m> components written as 500x500 PNG (part index 0) to <folder>`, then
   one line per image, such as `  R1 (Res2) -> Res2.png (500x500)`. The files are in `<Out>\<sheet name>\`.

| Command | Parameters | Result |
|---|---|---|
| `GenerateSchCompImages` | `Out=<folder>` (default: the temp folder), `Size=<pixels>` (1 to 4096, default 500), `Part=<part index>` (default 0) | The number of images written, the folder, and one line per component with its file name and pixel size; components for which the thumbnail handle was 0 are listed at the end |

## How it works
`Commands/SchComponentToImageCommands.cs`: `GenerateSchCompImages` parses `Size` and `Part`, gets the schematic server
from `SCH.GlobalVars.SchServer` and the active document from `GetCurrentSchDocument`, and creates
`<Out>\<sheet name>`. It creates an iterator filtered to `eSchComponent` and, for each object whose `ObjectId` is
`eSchComponent`, reads `SymbolReference` and the designator text, then calls
`PaintLoadedComponentThumbnail(component, part, size, size)` through the `PaintThumbnail` helper. A non-zero handle is
copied into a `Bitmap` with `Image.FromHbitmap`, saved with `ImageFormat.Png`, and then freed with `DeleteObject`. The
iterator is destroyed in a `finally` block.

## Notes
- The design is not changed. Existing files with the same name are overwritten.
- File names are the `SymbolReference` with characters Windows forbids replaced by `_`. Components sharing a symbol
  (several resistors, the parts of one multi-part component) would overwrite each other, so the second and later
  ones get `_<designator>`, then `_2`, `_3`.
- **The declared `uint` return type does not work.** The schematic server is a dispatch interface and returns the
  handle in a 64-bit unsigned variant (a sign-extended pointer, for example `18446744071629510860`), which does not fit
  in `uint`: the plain typed call throws `System.OverflowException` inside the IDispatch forwarder before the caller
  sees anything, for every component. `PaintThumbnail` therefore calls the method with `Type.InvokeMember`, where the
  return value stays `object`, and reinterprets the bits as an `IntPtr`.
- **The caller owns the bitmap and must free it.** `Image.FromHbitmap` copies the pixels, and disposing that `Bitmap`
  does not free the handle. Without `DeleteObject`, Altium Designer's GDI object count grows by exactly one per image
  and never comes back down; with `DeleteObject(hBitmap)` it stays flat. The SDK has no call to free the handle, so the sample P/Invokes `gdi32.dll` `DeleteObject`.
- The thumbnail is the symbol with its pins, pin numbers and pin names, on an opaque white background; the designator
  and the component parameters are not drawn. `Size` is the exact pixel size of the image, not a maximum.
- What `argPartIndex` selects for a multi-part component is not documented. For single-part components `Part=0` and
  `Part=1` produce byte-identical images.
- `GetCurrentSchDocument` also returns a schematic library.
