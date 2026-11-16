# Export each PcbLib footprint to STEP via a board

Writes the 3D model of every footprint of a PCB library to its own `.stp` file. Each footprint is placed on the
current PCB document as a temporary component, exported with `PCB.IPCB_Component.SaveModelToFile`, and removed again.
It shows how to turn a library footprint into a board component and how to take it off the board afterwards.

## What it shows
- `PCB.IPCB_ServerInterface.LoadPCBLibraryByPath`: the library of a `.PcbLib` file. An open document's library is
  reused; otherwise the file is loaded without being opened as a document.
- `PCB.IPCB_Library.LibraryIterator_Create` / `LibraryIterator_Destroy`: iterate the footprints
  (`PCB.IPCB_LibComponent`). `PCB.IPCB_LibComponent.Pattern` is the footprint name (there is no `Name` member).
- `PCB.IPCB_ServerInterface.PCBObjectFactory` with `PCB.TObjectId.eComponentObject`, then `PCB.IPCB_Primitive.Board`
  and `PCB.IPCB_Component.LoadFromLibrary` with `FileName=<library path>|Footprint=<footprint name>`: a component
  built from a library footprint.
- `PCB.IPCB_Board.AddPCBObject` / `RemovePCBObject` inside `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess`:
  the temporary component is put on the board and taken off it again.
- `PCB.IPCB_Component.SaveModelToFile`: writes the component's 3D model to a file.

## Run it
1. Open a `.PcbDoc` (for example the SpiritLevel-SL1 example board) and make it the active document. It's only a
   temporary host: don't save it afterwards.
2. `tools/ad.ps1 -Sample PcbLibComponentToStep run PcbLibCompToStep "Lib=<path>\Spirit_Level_Project.PcbLib|Out=<folder>"`.
   The menu item **PcbLibComponentToStep › Pcb Lib Comp To Step** passes no `Lib`, so it only reports that the
   parameter is missing.
3. The result lists how many footprints were written to `<folder>\<footprint>.stp`, which footprints gave no file
   (with their 3D body counts), and the board's component count before and after (these two should be equal).

| Command | Parameters | Result |
|---|---|---|
| `PcbLibCompToStep` | `Lib=<.PcbLib path>` (required), `Out=<folder>` (default: temp folder) | `<lib>: W of N footprints written as STEP to <folder>.`, the `SaveModelToFile` true count, `Components on <PcbDoc>: C before, C after.`, then `Written:` / `No file:` / `LoadFromLibrary returned false:` lists |

## How it works
`PcbLibComponentToStepCommands.cs` checks that the current board is a `.PcbDoc`. It checks that `Lib` exists, creates
`Out`, and loads the library with `LoadPCBLibraryByPath`. It counts the board's components with a board iterator,
then opens one `PreProcess` bracket and iterates the library's footprints. For each footprint, `ExportOne` does the
following:

1. Creates a component with `PCBObjectFactory` and sets its `Board`.
2. Calls `LoadFromLibrary`. If that fails, the component is freed with `DestroyPCBObject` and skipped.
3. Sets the top layer and a location 100 mil from the board origin, and adds the component with `AddPCBObject`.
4. Calls `SaveModelToFile`.
5. In a `finally` block, removes the component with `RemovePCBObject` and clears its `Board`.

The command decides whether a file was written by checking the disk: the file is new, or rewritten since the call
started. It doesn't rely on the bool result. After `PostProcess`, it counts the components again.

## Notes
- The PcbDoc is changed in memory: each footprint adds and removes a component, so the document ends up marked
  modified and its undo history holds the pairs. Close it without saving.
- The removed components aren't destroyed, because the undo history may still refer to them.
- With a `.stp` path, `SaveModelToFile` writes a STEP file and returns true exactly when it writes one. A footprint
  without 3D bodies gives no file and false. A footprint with a body can also give no file (the `TP` footprint of
  the SpiritLevel-SL1 library is one).
- `LoadPCBLibraryByPath` opens a `.PcbLib` that is not open yet as a library document, which stays open afterwards.
  A second run reuses it. Close it yourself when you're done.
- The sibling sample `ExportPcbLibComponentToStep` exports through a component that is not on a board.
- Footprint names are cleaned of characters that file names can't contain, and made unique within the run.
