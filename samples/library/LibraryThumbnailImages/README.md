# Render PNG thumbnails of library symbols and footprints

Opens every `.SchLib` in one folder and every `.PcbLib` in another, and saves a PNG thumbnail of each symbol and
each footprint. It shows the two SDK thumbnail renderers, which return a GDI bitmap handle that .NET turns into
an image file, and how to open and close library files without touching the libraries the user already has open.

## What it shows
- `SCH.ISch_ServerInterface.PaintLoadedComponentThumbnail`: draws a symbol *object* (an `SCH.ISch_Component`
  from a loaded library) into a new bitmap and returns its handle (declared `uint`, but see Notes).
- `PCB.IPCB_ServerInterface.PaintFootprintThumbnail`: draws a footprint given by *name and library file path*.
  It returns 0 when the footprint cannot be loaded.
- `DXP.IClient.GetDocumentByPath`, `DXP.IClient.OpenDocument` and `DXP.IClient.CloseDocument`: open a library
  for the job and close it again, but only if the user didn't already have it open.
- `SCH.ISch_ServerInterface.GetSchDocumentByPath` and `PCB.IPCB_ServerInterface.GetPCBLibraryByPath`: get the
  editor object of a library opened from a path.
- `SCH.ISch_Lib.SchLibIterator_Create` with `SCH.ISch_Iterator.AddFilter_ObjectSet`
  (`new SCH.TObjectSet { SCH.TObjectId.eSchComponent }`): walk the symbols of a schematic library.
- `PCB.IPCB_Library.LibraryIterator_Create` and `PCB.IPCB_LibraryIterator.FirstPCBObject`: walk the footprints
  of a PCB library. The footprint name is `PCB.IPCB_LibComponent.Pattern`, because the .NET API has no `Name`.
- `SCH.ISch_Component.SymbolReference`: the symbol name, used as the image file name.

## Run it
1. Have a folder with `.SchLib` files and/or a folder with `.PcbLib` files, for example a copy of the `Libraries`
   folder of the SpiritLevel-SL1 example project that ships with Altium Designer. One folder can hold both kinds.
2. `tools/ad.ps1 -Sample LibraryThumbnailImages run GenerateThumbnails "SchFolder=<folder>|PcbFolder=<folder>|SchSize=300|PcbSize=200"`.
   The menu item **LibraryThumbnailImages › Generate Thumbnails** passes no folders, so it only tells you to pass
   them.
3. The command returns one line per library and the total count. The images are written to
   `<Out>\SCH\<file.SchLib>\<SymbolReference>.png` and `<Out>\PCB\<file.PcbLib>\<footprint>.png`.

| Command | Parameters | Result |
|---|---|---|
| `GenerateThumbnails` | `SchFolder=<folder>` and/or `PcbFolder=<folder>` (at least one); `Out=<folder>` (default `%TEMP%\LibraryThumbnailImages`); `SchSize=<pixels>` (default 2000); `PcbSize=<pixels>` (default 500), sizes 1 to 4096 | `N of M thumbnails written to <Out>`, then per library `<file>: n of m symbols/footprints as WxH PNG (images WxH) -> <folder>` and any failures |

## How it works
Everything is in `Commands/LibraryThumbnailImagesCommands.cs`. `GenerateThumbnails` checks the parameters, then
lists `*.SchLib` and `*.PcbLib` in the given folders (not recursively). `RunForLibrary`
handles each file:

1. If `GetDocumentByPath` doesn't find the file, it opens it with `OpenDocument("SCHLib"|"PCBLib", path)` and
   remembers that it did.
2. It renders the library into a folder named after the file.
3. In `finally`, it calls `CloseDocument` only for a document it opened itself.

A failure in one library is reported on that library's line, and the next library still runs.

`RenderSymbols` gets the `ISch_Lib` with `GetSchDocumentByPath`. If that returns nothing, it falls back to
`ShowDocument` + `GetCurrentSchDocument` and checks the document name. It then checks that the object ID is
`eSchLib`, walks the components with a filtered `SchLibIterator_Create`, and calls
`PaintLoadedComponentThumbnail(symbol, 0, size, size)` for each one. `RenderFootprints` gets the `IPCB_Library`
with `GetPCBLibraryByPath`, walks `LibraryIterator_Create`, and calls `PaintFootprintThumbnail(Pattern, path,
size, size)`. Both Paint calls go through `PaintLateBound` (`Type.InvokeMember`, see Notes), and both
methods destroy their iterator in `finally`.

`PaintLateBound` turns the returned value into an `IntPtr`; `SaveAsPng` copies it with `Image.FromHbitmap`,
saves it with `ImageFormat.Png` and disposes the copy straight away.

## Notes
- **The typed Paint methods cannot be called.** The wrappers declare a `uint` result, but AD
  returns a `UInt64` (the sign-extended handle), so the typed call throws `OverflowException`. The sample calls
  both methods late-bound with `Type.InvokeMember` and converts the result to `IntPtr` (`PaintLateBound`).
- **Bitmap handles are not freed, and they leak.** The caller owns the HBITMAP: the AD process gains
  exactly one GDI object per image. Freeing it needs the Win32 `DeleteObject`, which the .NET API doesn't offer and
  samples don't P/Invoke, so the sample leaves it alone. Each image keeps its pixels too (about 16 MB at the
  default 2000x2000). A large library can exhaust GDI handles or memory and make Altium Designer fail, so use small sizes and few libraries
  per AD session.
- Only part 0 of a multi-part symbol is rendered. What other part indexes
  select is not documented.
- `PaintFootprintThumbnail` reads the footprint from the library *file*. For a library that is already open with
  unsaved changes, the image may show the saved state.
- Libraries the user already has open are used as they are and left open. Libraries the command opens are closed
  without being saved (nothing in them is changed).
- If `GetSchDocumentByPath` does not find a library that was just opened with `OpenDocument`, the fallback changes
  the focus. The document name check accepts the full path or only the file name in `GetState_DocumentName`.
- The items of the PCB library iterator are cast to `IPCB_LibComponent` without a check.
- File names: characters Windows forbids are removed from footprint and symbol names:
  `SO-G5/P.95` is written as `SO-G5P.95.png`. Names that collide get `_2`, `_3`, and
  so on.
- The output folder per library is named after the file, extension included
  (`SCH\SpiritLevel_2E.SchLib\`).
