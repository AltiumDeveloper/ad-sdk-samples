# Export every PcbLib footprint to a STEP file

Writes the 3D model of every footprint of a PCB library to its own `.step` file. It shows how to iterate a library,
turn a library footprint into a transient `PCB.IPCB_Component` with `LoadFromLibrary`, and export its model with
`SaveModelToFile`, and compares that with the direct `PCB.IPCB_LibComponent.SaveModelToFile`.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary` / `PCB.IPCB_ServerInterface.GetPCBLibraryByPath`: the focused
  library, or the library of an open document by its file name (the focus is not changed).
- `DXP.IClient.OpenDocument` with kind `PCBLib` and `DXP.IClient.CloseDocument`: open a library given by path for the
  export, and close it again only when this command opened it.
- `PCB.IPCB_Library.LibraryIterator_Create` / `PCB.IPCB_Library.LibraryIterator_Destroy`: walk the footprints
  (`PCB.IPCB_LibComponent`).
- `PCB.IPCB_LibComponent.Pattern`: the footprint name. .NET has no `Name` member on `PCB.IPCB_LibComponent`.
- `PCB.IPCB_Group.GetPrimitiveCount` with `PCB.TObjectId.eComponentBodyObject`: the number of 3D bodies of a footprint,
  reported next to each result.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` (`eComponentObject`, `eNoDimension`, `eCreate_Default`): a component that
  is not on any board.
- `PCB.IPCB_Component.LoadFromLibrary` with `FileName=<library path>|Footprint=<footprint name>`: load the footprint's
  primitives into it.
- `PCB.IPCB_Component.SaveModelToFile`: write the component's 3D model to a file.
- `PCB.IPCB_ServerInterface.DestroyPCBObject`: free the transient component.
- `PCB.IPCB_LibComponent.SaveModelToFile` (`argSaveAsPart` = false): the same export straight from the footprint
  (`Via=LibComponent`).

## Run it
1. Have a `.PcbLib` saved on disk whose footprints have 3D bodies, for example `Spirit_Level_Project.PcbLib` from the
   SpiritLevel-SL1 example project that ships with Altium Designer. Focus it, or pass its path in `Lib`.
2. **ExportPcbLibComponentToStep › Generate Step**, or
   `tools/ad.ps1 -Sample ExportPcbLibComponentToStep run GenerateStep "Lib=<path to .PcbLib>|Out=<folder>"`
3. The result starts with `<library>: <w> of <n> footprints written as STEP to <Out>\<library name> via <method>.`,
   followed by the count of `SaveModelToFile` calls that returned true, a `Written:` line (footprint, body count, file
   size) and a `No file:` line for footprints that produced no file.

| Command | Parameters | Result |
|---|---|---|
| `GenerateStep` | `Lib=<.PcbLib path>` (optional; default: the focused PCB library), `Out=<folder>` (optional; default: the temp folder), `Via=Component` (default) or `Via=LibComponent`, `Attach=false` (optional, `Via=Component` only: no board; crashes `LoadFromLibrary`) | One `<footprint>.step` per footprint with a 3D model in `<Out>\<library name>\`, and the summary described above |

## How it works
`ExportPcbLibComponentToStepCommands.cs`: `GenerateStep` takes the focused library, or for `Lib` reuses an open
document (`GetPCBLibraryByPath`, with a fallback that shows the document and checks `GetCurrentPCBLibrary`) or opens
it with `OpenDocument("PCBLib", path)`. It creates `<Out>\<library name>\` and walks the footprints with the library
iterator. For each `PCB.IPCB_LibComponent` it reads `Pattern`, counts the 3D bodies and builds a safe, unique file
name.

With `Via=Component` (`ExportViaComponent`), it creates a component with `PCBObjectFactory`, assigns the
library's board to it (unless `Attach=false`), calls `LoadFromLibrary` with the library's file path and the footprint name,
then `SaveModelToFile`, and finally frees the component with `DestroyPCBObject`. The component is never added to a
board. With `Via=LibComponent`, it calls `SaveModelToFile(path, false)` on the footprint itself.

A file counts as written when it exists afterwards and is new or newer than before the call, because neither result
value is documented as "file written". The iterator is destroyed and a library opened by the command is closed in a
`finally`.

## Notes
- The command does not change the library. It only writes files in the output folder, where it overwrites
  `<footprint>.step` files of an earlier run.
- `LoadFromLibrary` reads the footprint from the library file (`FileName=`), so the library must be saved.
- Footprints without a 3D model produce no file. `PCB.IPCB_Component.SaveModelToFile` returns true exactly when a
  file was written, while `PCB.IPCB_LibComponent.SaveModelToFile` returns true for every footprint, also when nothing
  was written.
- The transient component needs a board: `LoadFromLibrary` on a component without a board raises an access violation
  in `ADVPCB.DLL` (`Attach=false` reproduces it, so don't use it except to check this). Assigning the library's board
  without adding the component is enough, and the library is not marked modified.
- `GetPrimitiveCount` counts the 3D bodies of every footprint the iterator returns, not only the current one.
- A footprint can have a 3D body and still produce no file with either method (the `TP` footprint of the
  SpiritLevel-SL1 library is one).
- Whether `SaveModelToFile` writes a STEP file or a part depends on a PCB editor setting (reference remarks); with
  default settings it writes STEP. Use `SaveModelToFileAsPart` to always save a part.
- The menu is inserted into the PCB editor's menu (`MNPCBMenu`). The extension template has no PcbLib menu target, so
  `tools/ad.ps1` is the direct way to run it.
