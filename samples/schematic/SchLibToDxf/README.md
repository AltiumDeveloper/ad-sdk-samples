# Export a schematic library to AutoCAD DXF

Attempts a DXF export of a schematic library, and shows how `DXP.IServerDocument.DoFileSave` saves a document in a
format chosen by name, writing to the document's current file name, and how to point a document at another file
with `SetFileName` and restore it afterwards. **No DXF is produced**: AutoCAD is an export filter, which
`DoFileSave` cannot reach, so an unknown format name is ignored and the editor's default format is written under the
`.dxf` name (see Notes).

## What it shows
- `DXP.IClient.GetDocumentByPath` / `DXP.IClient.OpenDocument`: reuse a library that is already open, or open it
  with the kind `SCHLib`.
- `SCH.ISch_ServerInterface.GetSchDocumentByPath` and `SCH.ISch_Lib`: check that the opened file really is a
  schematic library, not a sheet.
- `DXP.IServerDocument.FileName` (`GetFileName` / `SetFileName`): `DoFileSave` writes to the document's file name,
  so the document is pointed at the .dxf path first and back afterwards.
- `DXP.IServerDocument.DoFileSave`: save in a format given by name. The name must match a format the editor
  registers (see Notes).
- `DXP.IServerDocument.Modified`: restore the document's modified flag after the save.
- `DXP.IClient.CloseDocument`: close only what the command opened.

## Run it
1. Copy a `.SchLib` to a scratch folder (for example the library of the SpiritLevel-SL1 example project that ships
   with Altium Designer). Don't use an installed library: the command re-points the open document during the save.
2. `tools/ad.ps1 -Sample SchLibToDxf run SchLibToDxf "SchLibFileName=<folder>/Lib.SchLib|DXFFileName=<folder>/Lib.dxf"`,
   or focus the library and run `SchLibToDxf` without `SchLibFileName`.
3. The result names the file written, its size and its detected content (with the default format name that is
   `OLE compound file (an Altium binary library, not DXF)`, see Notes). Pass `Filter=<name>` to try another
   save format name.

| Command | Parameters | Result |
|---|---|---|
| `SchLibToDxf` | `SchLibFileName=<.SchLib>` (default: the focused library), `DXFFileName=<.dxf>` (default: `<Out>/<library>.dxf`), `Out=<folder>` (default: temp folder), `Filter=<format>` (default `AutoCAD Files`) | `<lib>: DoFileSave('<filter>') returned <bool>. Wrote <path>, <n> bytes, content: <kind>.` plus whether the library was closed or its name restored |

## How it works
`SchLibToDxfCommands.cs` finds the library: the `SchLibFileName` file (reused with `GetDocumentByPath`, or opened
with `OpenDocument`), or the owner document of `GetCurrentView`. `GetSchDocumentByPath` must return an `ISch_Lib`.
The command remembers the document's file name and modified flag, calls `SetFileName` with the DXF path and then
`DoFileSave(Filter)`. In a `finally` it calls `SetFileName` with the old name and `SetModified` with the old flag,
then closes the document if it opened it. It reads the first bytes of the output file to report whether
`DoFileSave` wrote a DXF, a DWG or an Altium binary file.

## Notes
- **`DoFileSave` cannot write a DXF.** `argKind` must be one of the exact `SaveFilters` entries of that editor's
  `EditorWindowKind` in its `.ins` file. For `SchLib` those are only
  `Advanced Schematic binary library (*.SchLib)` and `Schematic binary 4.0 library (*.lib)`; AutoCAD is an
  `ExportFilter` (served by `SchDwgUtility`), and there is no .NET API for export filters. `AutoCAD Files`,
  `AutoCAD` and `AUTOCAD DXF` are each **silently ignored**: `DoFileSave` returns `true` and writes the native
  binary library (an OLE compound file) under the `.dxf` name. `Schematic binary 4.0 library (*.lib)` writes a
  genuinely different, smaller file (starting with `Protel for Wind…`), which shows the name is matched against
  the SaveFilters. So the sample demonstrates the technique, and its content sniffing shows that the return value
  alone does not prove the requested format was used. A real DXF needs File › Export in the UI.
- No modal dialog appears during `DoFileSave`.
- `SetFileName` back to the old name undoes the re-pointing: the library stays open under its `.SchLib` name
  (title bar and tab), closes without a save prompt, and its bytes on disk are unchanged.
- The reference describes `FileName` as read-only. The assemblies have a `SetFileName` setter, and it works.
- The generated menu appears in the schematic-sheet and no-document menus, not in the SchLib editor. From the
  menu, pass `SchLibFileName`, or use `tools/ad.ps1` with the library focused.
- An existing file at `DXFFileName` is overwritten if `DoFileSave` writes. The result says when the file was not
  changed.
