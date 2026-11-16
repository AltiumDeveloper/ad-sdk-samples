# Rotate component designator text on a board

Sets the rotation of the designator text (or, with `Target=Comment`, the comment text) of every component on the
current board. It shows how to reach a component's child text objects and change them in a board iteration.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eComponentObject` and
  `PCB.IPCB_AbstractIterator.AddFilter_AllLayers`: iterate every component of the board.
- `PCB.IPCB_Component.Name` / `PCB.IPCB_Component.Comment`: read-only helper extensions (`GetState_Name()`,
  `GetState_Comment()`, in scope with `using PCB;`) that return the component's designator and comment as
  `PCB.IPCB_Text` objects. You change the text object, not the component.
- `PCB.IPCB_Text.Rotation` (inherited from `PCB.IPCB_RectangularPrimitive`): the text's angle in degrees.
- `PCB.IPCB_Primitive.BeginModify` / `EndModify` on the component, with `PCB.IPCB_Primitive.GraphicallyInvalidate`,
  inside `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess`: the bracket for changing PCB objects.
- `PCB.IPCB_Component.NameAutoPos` / `CommentAutoPos`: listed by the read-back command, because autoposition may
  interact with a manually set angle.

## Run it
1. Open a scratch copy of a `.PcbDoc` with components (for example the board of the SpiritLevel-SL1 example project
   that ships with Altium Designer) and make it the active document.
2. **PcbComponentTextRotation › Get Component Text Rotation**, or
   `tools/ad.ps1 -Sample PcbComponentTextRotation run GetComponentTextRotation "Max=5"`, to record the current
   angles.
3. **PcbComponentTextRotation › Rotate Designator Text**, or
   `tools/ad.ps1 -Sample PcbComponentTextRotation run RotateComponentText "Angle=90|Target=Name"`.
4. Run **Get Component Text Rotation** again: the `Name.Rotation` summary reads `90 deg x<n>`. Close the document
   without saving to discard the change.

| Command | Parameters | Result |
|---|---|---|
| `RotateComponentText` | `Angle=<degrees>` (default 90), `Target=Name\|Comment` (default `Name`) | `<board>: Name.Rotation set to 90 deg on <n> of <n> components, <c> changed. First: <designator>: Name.Rotation <old> -> <new>.` |
| `GetComponentTextRotation` | `Max=<n>` (default 20, `0` = all) | A summary line with a histogram of the Name and Comment angles, then one line per component: layer, rotation, text, angle and autoposition of the designator and comment. |

## How it works
`PcbComponentTextRotationCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer` (a `UserException`
covers no PCB editor or no PCB document). `RotateComponentText` parses `Angle` (invariant culture) and `Target`,
calls `PreProcess`, and walks the components with a board iterator. For each component it takes the text object
from `GetState_Name()` or `GetState_Comment()`, and calls `BeginModify` on the component,
`SetState_Rotation(angle)` on the text, `GraphicallyInvalidate` and `EndModify` on the component. It reads the angle
back to count the components that changed. The iterator is destroyed and `PostProcess` called in a `finally`.
`GetComponentTextRotation` walks the components the same way without changing anything and reports their angles.

## Notes
- The angle is set as given (absolute, in degrees). No normalisation is done.
- Bracketing the change on the component, not on the text object, applies the text rotation and marks the PCB
  document modified. Whether it is undone as a single step is not known.
- A component's text autoposition (for example `CommentAutoPos = eAutoPos_BottomLeft`), its own rotation (0, 90,
  180 deg) and its layer do not override the angle set here: the value read back is the absolute angle assigned,
  and 45 deg (not a multiple of 90) is accepted.
- `GraphicallyInvalidate` is called on each changed component and raises no error.
- There is no `cAllLayers` constant in .NET for `AddFilter_LayerSet_2`; `AddFilter_AllLayers` selects all layers.
