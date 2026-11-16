# Move all components as one undoable step

Moves every component on the current board in one batch (its X location times a factor) and groups the batch so
that one Undo in Altium Designer reverts all of it. It shows the PCB process messages sent with
`PCB.IPCB_Board.DispatchMessage`, and compares them with the `PreProcess`/`PostProcess` bracket.

## What it shows
- `PCB.IPCB_Board.DispatchMessage`: sends the PCB messages ProcessStart (7) and ProcessEnd (8) around the batch,
  then YieldToRobots (10). `Altium.SDK` has no constants for these IDs, so the sample declares them.
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess`: the documented bracket for changing PCB objects, offered
  as the second grouping route (`Grouping=PreProcess`).
- `PCB.IPCB_Primitive.BeginModify` / `EndModify` around each `PCB.IPCB_Group.XLocation` change: the
  per-object notifications the board's undo history receives.
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eComponentObject`: collect the components first,
  change them afterwards, destroy the iterator in `finally`.
- `PCB.PCBConstant.MinCoord` / `MaxCoord`: validate the whole batch before changing anything.
- `DXP.Utils.RunCommand`: runs the PCB editor's `PCB:Undo` process (helper command).

## Run it
1. Open a `.PcbDoc` with several components (for example a copy of the board of the SpiritLevel-SL1 example project
   that ships with Altium Designer) and make it the active document. Work on a copy: the command really moves the
   components.
2. **ComponentMoveUndoGroup › Component Positions**, or
   `tools/ad.ps1 -Sample ComponentMoveUndoGroup run ComponentPositions`: note the component count and `sum X`.
3. **ComponentMoveUndoGroup › Move Components**, or `tools/ad.ps1 -Sample ComponentMoveUndoGroup run MoveComponents`:
   every component moves to twice its X location; the result shows `sum X` before and after (doubled).
4. Press Ctrl+Z once in the PCB editor (or run `UndoLast`), then **Component Positions** again: `sum X` is back
   to the value of step 2 - one Undo reverts the whole batch.

| Command | Parameters | Result |
|---|---|---|
| `MoveComponents` | `Factor=<positive number>` (default `2`), `Grouping=Messages` (default) or `PreProcess` | `Moved <n> components: X x <factor>, Grouping=<route>.` plus the before and after summaries |
| `ComponentPositions` | `List=<n>` (default `5`): how many components to list | `<board>: <n> components, sum X = <internal units>, X from <a> mil to <b> mil; <designator> X=<x> mil, ...` |
| `UndoLast` | none | `Ran PCB:Undo once. Now: <summary>` |

`ComponentPositions` and `UndoLast` are helpers. When a factor would move any component outside the coordinate range,
`MoveComponents` fails with `... would move <m> of <n> components outside the coordinate range ... Nothing changed.`

## How it works
`ComponentMoveUndoGroupCommands.cs`:

- `MoveComponents` parses `Factor` and `Grouping`, gets the board from `PCB.GlobalVars.PCBServer`, and collects
  the components with a board iterator (`AddFilter_ObjectSet`, `AddFilter_AllLayers`, `eProcessAll`). It computes
  every new X and refuses the whole batch if one would leave `PCBConstant.MinCoord..MaxCoord`.
- It opens the group: `board.DispatchMessage(null, null, PCBM_ProcessStart, null)` (`Messages`) or
  `pcbServer.PreProcess()` (`PreProcess`). For each component it calls `BeginModify`, `SetState_XLocation`,
  `EndModify`. In `finally` it closes the group with ProcessEnd and YieldToRobots, or with `PostProcess`.
- `ComponentPositions` and the before/after lines sum the X locations in internal units, so the board state before
  the move, after it, and after one Undo can be compared exactly.
- `UndoLast` runs `DXP.Utils.RunCommand("PCB:Undo", "")` and reads the board again, because `RunCommand`
  returns nothing.

## Notes
- The change is made in memory only. Close the document without saving to discard it.
- The undo methods on the board (`PCB.IPCB_Board.NewUndo`, `EndUndo`, `DoUndo`, `DoRedo`) are documented as
  having no effect, so the sample does not use them. Undo is triggered through the editor's own process.
- `X` is multiplied as an absolute coordinate: the components move away from the coordinate origin, not from the
  board origin, and may land outside the board outline.
- `DispatchMessage(null, null, id, null)` compiles without casts: the overload taking `object` arguments is chosen.
- **Both** routes group the batch into a single undo step: the messages 7/8/10, and the `PreProcess`/`PostProcess`
  bracket on its own. `PCB:Undo` run through `DXP.Utils.RunCommand` undoes synchronously (the board read right after
  it shows the restored positions) and opens no dialog. The document stays marked modified (`*`) after the undo.
