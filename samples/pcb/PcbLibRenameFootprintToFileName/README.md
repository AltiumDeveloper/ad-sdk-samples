# Rename each PcbLib's footprint to its file name

Opens every `.PcbLib` file of a folder, renames its footprint to the file name (without extension), saves and
closes the library, and returns a log. It shows how to batch-edit library files from code: open a document, get
its `PCB.IPCB_Library`, change a footprint, save through `DXP.IServerDocument`, and close it again.

## What it shows
- `DXP.IClient.IsDocumentOpen`: libraries the user already has open are left alone, because closing them from
  code would also close the user's session.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument` / `DXP.IClient.CloseDocument`: open a library file
  without a dialog, make it the active document, and close it when done.
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary` and `PCB.IPCB_ServerInterface.GetPCBLibraryByPath`: the library
  of the active document, or of a given file.
- `PCB.IPCB_Library.ComponentCount` and `PCB.IPCB_Library.CurrentComponent`: how many footprints the library
  holds, and which one is active in the library editor.
- `PCB.IPCB_LibComponent.Pattern`: the footprint name. `IPCB_LibComponent` has no `Name` in the .NET API.
- `PCB.IPCB_Library.GetComponentByName`: checks that no other footprint already has the new name.
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify`,
  then `PCB.IPCB_Library.RefreshView`: the bracket for changing a PCB object, applied to a footprint.
- `DXP.IServerDocument.DoFileSave`: saves the document without a prompt; its result is checked.
- `PCB.IPCB_Library.LibraryIterator_Create` / `PCB.IPCB_LibraryIterator`: lists the footprints (read-back
  command `ListFootprints`).

## Run it
1. Copy the `.PcbLib` files into a work folder (the `Spirit_Level_Project.PcbLib` library of the SpiritLevel-SL1
   example project that ships with Altium Designer works). **The command saves the libraries in place.** Close
   these libraries in Altium Designer: open ones are skipped.
2. **PcbLibRenameFootprintToFileName › Rename Footprints To File Name** (a folder dialog opens), or
   `tools/ad.ps1 -Sample PcbLibRenameFootprintToFileName run RenameFootprintsToFileName "Folder=<work folder>"`
3. The result lists each file: renamed (old and new name), already named, skipped (with the reason) or failed. The
   log is also written to `Result_ChangePCBLibComponentName.txt` in the temp folder (or `Out`). From the menu it
   opens in Altium Designer's text editor.
4. Check a library: `tools/ad.ps1 -Sample PcbLibRenameFootprintToFileName run ListFootprints "Lib=<file>"`.

| Command | Parameters | Result |
|---|---|---|
| `RenameFootprintsToFileName` | `Folder=<folder>` (required from the command line; the menu asks with a dialog), `MultiFootprint=Skip` (default) or `Current`, `Out=<folder>` (default: temp folder), `Open=true\|false` (default: true from the menu, false from the command line) | `<folder>: <n> PcbLib files, <r> renamed, <u> already named, <s> skipped, <f> failed.`, one line per file, then `Log written to <path>` |
| `ListFootprints` | `Lib=<path to .PcbLib>` (required) | `<file>: <n> footprints (ComponentCount <c>), current "<name>"`, then one footprint name per line |

## How it works
`PcbLibRenameFootprintToFileNameCommands.cs`: `RenameFootprintsToFileName` reads `Folder` (or asks with a
`FolderBrowserDialog` when started from a menu), lists the folder's `*.PcbLib` files (not its subfolders), and
calls `RenameOne` for each. `RenameOne` skips files that `IsDocumentOpen` reports as open, then opens the file with
`OpenDocument("PcbLib", …)` and shows it. It takes the current library if that is this file, otherwise the library
found by `GetPCBLibraryByPath`. Libraries with no footprint are skipped; so are libraries with several footprints,
unless `MultiFootprint=Current`. It then takes `CurrentComponent`, compares its `Pattern` with the file name,
checks the name is free (`GetComponentByName`), and sets the new `Pattern` inside the modification bracket. After
`RefreshView` it saves with `DoFileSave("PcbLib")`, reports a failed save, and always closes the document in a
`finally`. The log goes to the result file and is returned as text.

`ListFootprints` opens a library (if it is not open), iterates its footprints with a library iterator (destroyed
in a `finally`), and closes it again if it opened it.

## Notes
- The libraries are saved in place: run the command on copies only. There is no undo once the file is saved.
- Setting `Pattern` inside the modification bracket and then calling `DoFileSave("PcbLib")` renames the footprint
  in the saved file.
- `CurrentComponent` is whichever footprint is active in the library editor when the file opens, and it is
  **not** the same footprint on every open of the same file. So `MultiFootprint=Current` renames an arbitrary
  footprint. The default skips such libraries.
- `ComponentCount` equals the number of footprints the library iterator returns, and `IsDocumentOpen` is true for
  a library open in its editor (so `ListFootprints` leaves such a library open).
- File names are used as footprint names unchanged. The reference says `Pattern` removes leading and trailing
  spaces; other characters that a footprint name may not contain are not checked.
