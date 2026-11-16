# Merge PcbLib files from a folder into a new library

Creates a new PCB library and copies into it every footprint of every `.PcbLib` in a folder, then saves it. It
teaches how to move footprints between libraries: walk a library with its iterator, clone each footprint with
`ReplicateWithChildren`, give the clone the target library's board and register it there.

## What it shows
- `DXP.Utils.RunCommand` with `WorkspaceManager:OpenObject` (`ObjectKind=NewAnything|Kind=DefaultPcbLib`): create
  a new, untitled PCB library; `DXP.IClient.CurrentView` / `DXP.IServerDocumentView.GetOwnerDocument` then give its
  document.
- `DXP.IClient.OpenDocument` / `DXP.IClient.IsDocumentOpen` / `DXP.IClient.CloseDocument`: open each source library
  without showing it, and close it again only if the command opened it.
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary` and `PCB.IPCB_ServerInterface.GetPCBLibraryByPath`: the
  `IPCB_Library` of the new document and of each source file.
- `PCB.IPCB_Library.LibraryIterator_Create` / `PCB.IPCB_Library.LibraryIterator_Destroy`: iterate the footprints
  (`PCB.IPCB_LibComponent`) of a library.
- `PCB.IPCB_Group.ReplicateWithChildren`: a deep copy of a footprint. It returns `IPCB_Primitive`, so cast it to
  `IPCB_LibComponent`.
- `PCB.IPCB_LibComponent.Pattern` and `PCB.IPCB_Primitive.Board`: name the copy and attach it to the target
  library's board (`PCB.IPCB_Library.Board`) before registering it.
- `PCB.IPCB_Library.RegisterComponent`: add the copy. It returns false for a name the library already has, so the
  sample checks with `PCB.IPCB_Library.GetComponentByName` first and renames with
  `PCB.IPCB_Library.GetUniqueCompName`.
- `PCB.IPCB_Library.CurrentComponent` and `PCB.IPCB_Library.RemoveComponent`: find and remove the new library's
  default footprint once a real one is in.
- `PCB.IPCB_Library.SetBoardToComponentByName`, `PCB.IPCB_Library.CurrentComponent`, `PCB.IPCB_Group.XLocation` /
  `YLocation` and `PCB.IPCB_Board.XOrigin` / `YOrigin`: make each copy current and assign its location the library
  origin (the reference does not say what this means for a library footprint, see Notes).
- `PCB.IPCB_Board.ViewManager_FullUpdate` and `PCB.IPCB_Primitive.BoundingRectangle`: refresh the library editor
  after the programmatic changes, and measure the last footprint against the origin for the returned text.
- `DXP.IServerDocument.FileName` (`SetFileName`) and `DXP.IServerDocument.DoFileSave`: name and save the new
  library.

## Run it
1. Put two or more `.PcbLib` files in a folder (for example two copies of the footprint library of an example
   project that ships with Altium Designer, such as SpiritLevel-SL1). Any document, or none, may be open.
2. **PcbLibMerge › Merge Pcb Libs** (pick the folder in the dialog; the menu is added to the PCB editor's menu and
   the no-document menu), or
   `tools/ad.ps1 -Sample PcbLibMerge run MergePcbLibs "Folder=<folder>|Out=<folder>\Merged.PcbLib"`
3. A new library opens, holding all footprints of the folder, and is saved as `Out`. The returned text gives the
   counts, the last footprint's bounding box and centre relative to the library origin (mil), a line per source
   library, every renamed or skipped footprint, and the footprint names of the result.
   `ListFootprints "Lib=<folder>\Merged.PcbLib"` reads the saved file back.

| Command | Parameters | Result |
|---|---|---|
| `MergePcbLibs` | `Folder=<folder>` (required when called with parameters; a menu click without it shows a folder picker), `Out=<folder or .PcbLib>` (default: temp folder, `PcbLibMerge.PcbLib`), `Overwrite=true`, `Duplicates=Rename` (default) or `Skip`, `Reference=Center` (default: runs "Set Reference > Center" on every copy that has pads, so it is the option that changes the footprints) or `Keep` | `Merged <folder>: <n> PcbLib files, <n> read, <n> footprints copied (<n> renamed, <n> skipped as duplicates, <n> failed).`, the saved path with the `DoFileSave` result, `Last footprint '<name>' relative to the library origin (mil): ...`, per-library lines and the footprint names |
| `ListFootprints` | `Lib=<path to .PcbLib>` | `<file>: <n> footprints (ComponentCount <n>)` and one name per line |

## How it works
`PcbLibMergeCommands.cs`, `MergePcbLibs`:

1. Reads the parameters and lists the folder's `.PcbLib` files (not recursive; the output file is left out).
2. Runs `WorkspaceManager:OpenObject` and checks that the focused document is now a new `PCBLIB` document. It keeps
   that document and its `IPCB_Library` for the rest of the command, and remembers the library's only footprint
   (the default placeholder).
3. For each source file: `OpenDocument`, `GetPCBLibraryByPath`, then the library iterator. Each footprint is
   checked against the merged library's names (rename or skip), cloned with `ReplicateWithChildren`, renamed with
   `SetState_Pattern`, given the merged board with `SetState_Board` and registered with `RegisterComponent`.
   After the first copy the placeholder is removed with `RemoveComponent`. `AlignCopy` makes the copy current
   (`SetBoardToComponentByName`, then `CurrentComponent`), assigns `XLocation` / `YLocation` the board origin inside
   `PreProcess`/`BeginModify` … `EndModify`/`PostProcess`, and with `Reference=Center` (only for footprints whose
   `GetPrimitiveCount` of `ePadObject` is not 0) refreshes the editor
   (`ViewManager_FullUpdate`), then runs `PCB:Zoom Action=All` and `PCB:SetComponentReference Location=CENTER` with
   the new library focused (`RunInLibrary`). The source is closed if the command opened it.
4. Makes the last footprint current and measures its `BoundingRectangle` against the board origin, refreshes and
   runs `PCB:Zoom Action=All`, then `SetFileName(Out)` and `DoFileSave("PCB Library File")` on the kept document,
   and lists the result's footprints.

`ListFootprints` opens a library (if needed), iterates it and closes it again if it opened it.

## Notes
- `PCB:SetComponentReference Location=CENTER` changes each copied footprint: it is the
  editor's "Set Reference > Center". Pass `Reference=Keep` to copy the footprints as they are. Footprints without
  pads (logos, symbols) are always copied unchanged and listed in the result: the process
  moves such a footprint 50000 mil, the library board's origin offset, away from the origin, while it centres
  footprints with pads correctly.
- The effect of `PCB:SetComponentReference` on `XLocation` / `YLocation` is only visible to the next command, not
  to the command that ran the process.
- Footprint names are unique per library (`RegisterComponent` compares them ignoring case). With
  `Duplicates=Rename` the copy gets `GetUniqueCompName`'s name (it appends " - duplicate"); with `Skip` it is left
  out. Both are reported.
- Source libraries are only read. A library that was already open stays open; the others are closed without saving.
- The new library is saved once at the end, so the file holds the merged footprints. `Out` is not overwritten
  without `Overwrite=true`. From the menu, `Out` is the temp folder.
- `WorkspaceManager:OpenObject` runs synchronously and leaves the new, untitled
  library focused; a new library holds one default footprint, `PCBComponent_1`; `ReplicateWithChildren` copies
  footprints that are not the source's current one (same pad and primitive counts); removing the placeholder after
  the first `RegisterComponent` is safe; closing a source keeps the copies; `SetFileName` then
  `DoFileSave("PCB Library File")` saves the untitled library.
- Alignment: with `Reference=Center` footprints with pads end up centred on the library origin. The
  `Last footprint ...` line is only a rough read-back: `BoundingRectangle` keeps the same values after Set Reference >
  Center while `XLocation` / `YLocation` change, so check the centring in the editor. `BoundingRectangle` returns
  0,0,0,0 for a footprint that is not the library's current one, which is why the sample makes it current first.
  The reference does not say what `XLocation` / `YLocation` mean for a library footprint. If you want
  the footprints unchanged, pass `Reference=Keep`.
- With `Reference=Center` the zoom runs after every footprint, which is slow on big libraries;
  with `Keep` it runs once at the end.
