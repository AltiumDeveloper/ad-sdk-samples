# Copy the current footprint into a new PcbLib

Copies the footprint shown in the active PCB library into a new PCB library, under a new name. It shows how to
clone a footprint with all its primitives, and how to move the clone into another library: assign that library's
board, register it, then make it the current footprint.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary`: the library of the focused PcbLib document. It follows the
  focus, so it is read once before and once after the new library is created.
- `PCB.IPCB_Library.CurrentComponent`: the footprint shown in the library editor, the source of the copy.
- `PCB.IPCB_Group.ReplicateWithChildren`: a copy of the footprint with copies of its primitives. It is declared on
  `PCB.IPCB_Group` and returns `PCB.IPCB_Primitive`, so the result is cast back to `PCB.IPCB_LibComponent`.
- `PCB.IPCB_LibComponent.Pattern`: the footprint name, set on the clone before it is registered.
- `PCB.IPCB_Primitive.Board` and `PCB.IPCB_Library.Board`: the clone is given the new library's board (all
  footprints of a library share one board).
- `PCB.IPCB_Library.RegisterComponent`: adds the clone to the new library. It returns false for an empty or already
  used name, so the result is checked.
- `PCB.IPCB_Library.SetBoardToComponentByName`: makes the registered clone the current footprint (registering
  does not).
- `DXP.Utils.RunCommand`: runs the built-in processes `WorkspaceManager:OpenObject` (new PCB library) and
  `PCB:Zoom` (zoom to all).
- `DXP.IClient.CurrentView`, `DXP.IServerDocumentView.GetOwnerDocument`, `DXP.IServerDocument.FileName`
  (`SetFileName`) and `DXP.IServerDocument.DoFileSave`: name and save the new document.

## Run it
1. Open a `.PcbLib` (for example `Spirit_Level_Project.PcbLib` from the SpiritLevel-SL1 example project that ships
   with Altium Designer) and show the footprint to copy.
2. **CopyPcbLibComponent › Copy Current Lib Component**, or
   `tools/ad.ps1 -Sample CopyPcbLibComponent run CopyCurrentLibComponent "Name=MyCopy|Out=<folder>"`
3. A new PCB library opens, saved as `<folder>\CopyPcbLibComponent.PcbLib`, showing the copied footprint named
   `MyCopy`, marked modified. The returned text names the source and target and gives the footprint counts.

| Command | Parameters | Result |
|---|---|---|
| `CopyCurrentLibComponent` | `Name=<footprint name>` (default `TestComp`), `Out=<folder or .PcbLib path>` (default: the temp folder), `Overwrite=true` (replace an existing file) | `Copied footprint '<source>' from <lib> into '<target>' as '<name>'.` plus the footprint counts and the current footprint of the new library |

## How it works
`CopyPcbLibComponentCommands.cs` first checks the parameters: an empty `Name` is refused, and an existing `Out`
file is refused unless `Overwrite=true`. It takes the current library and its current footprint, and clones the
footprint with `ReplicateWithChildren` while the source library still has the focus.

It then runs `WorkspaceManager:OpenObject` with `ObjectKind=NewAnything|Kind=DefaultPcbLib` through
`DXP.Utils.RunCommand`. It takes the document of `IClient.CurrentView` and checks that it is a `PCBLIB` document
other than the source. It names it with `SetFileName` and saves it with `DoFileSave("PCB Library File")`.

`GetCurrentPCBLibrary` is called again and must now return the new file. The clone gets the new name
(`SetState_Pattern`) and the new library's board (`SetState_Board`). It is registered with `RegisterComponent`, the
document is marked modified (`SetModified(true)`), the clone is made current with `SetBoardToComponentByName`, and
the view is zoomed with `PCB:Zoom Action=All`. The returned text reports the footprint count before and after, and
the footprint that is now current.

## Notes
- The new library is saved before the copy is added, so the file on disk does not
  contain the copy. Save the new library to keep it, or close it without saving. The source library is not changed.
- The new library is created from AD's default PcbLib template, which already contains a footprint
  (`PCBCOMPONENT_1`). If `Name` equals a footprint name in it, `RegisterComponent` refuses the copy: the
  command reports a free name (`GetUniqueCompName`) and leaves the saved library open.
- If the focused document after `WorkspaceManager:OpenObject` is not a new PCB library, the command stops before
  renaming or saving anything, instead of renaming and saving whatever document has the focus.
- The SDK reference does not document the process names and parameters (`WorkspaceManager:OpenObject`,
  `Kind=DefaultPcbLib`, `PCB:Zoom Action=All`) or the save kind `PCB Library File`.
- `RunCommand("WorkspaceManager:OpenObject", ...)` returns after the new library is
  created and focused, `DoFileSave("PCB Library File")` returns true, and `SetState_Board` plus `RegisterComponent`
  is all a clone needs to move to another library.
- `RegisterComponent` does not mark the library modified, so the command calls `SetModified(true)` on the new
  document. Without that call, closing the library would drop the copy without asking.
- `PCB:Zoom Action=All` does not zoom the view to fit the copied footprint, even when run again afterwards.
- The menu is inserted into the PCB editor menu (`MNPCBMenu`). `tools/ad.ps1` does not depend on the menu.
