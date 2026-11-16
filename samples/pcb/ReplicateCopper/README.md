# Replicate top-layer copper 40 mm to the right

Copies every free arc, pad, via, track, fill, region and polygon on the Top and Multi layers of the current board,
plus the components on those layers, and moves each copy by an X offset (40 mm by default). It shows how to copy PCB
objects with `Replicate` and `ReplicateWithChildren`, put the copy on the board and move it.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.IPCB_BoardIterator.AddFilter_ObjectSet`,
  `PCB.IPCB_BoardIterator.AddFilter_LayerSet` (`PCB.TV6_LayerSet` of `eV6_TopLayer`, `eV6_MultiLayer`) and
  `PCB.IPCB_BoardIterator.AddFilter_Method` with `PCB.TIterationMethod.eProcessFree`: only free objects and group
  objects (components, polygons) are fetched, not the primitives inside them.
- `PCB.IPCB_Primitive.ViewableObjectID` = `PCB.TViewableObjectID.eViewableObject_BoardRegion`: recognizes the board
  shape region so it is not copied.
- `PCB.IPCB_Primitive.Replicate`: copies a single primitive.
- `PCB.IPCB_Group.ReplicateWithChildren`: copies a group (component, polygon) with its child objects.
- `PCB.IPCB_Board.AddPCBObject`: the copy belongs to no board until it is added.
- `PCB.IPCB_Primitive.MoveByXY` between `PCB.IPCB_Primitive.BeginModify` and `PCB.IPCB_Primitive.EndModify`, with
  `EDP.Utils.MMsToCoord` for the offset.
- `PCB.IPCB_ServerInterface.PreProcess` / `PCB.IPCB_ServerInterface.PostProcess` around the whole change.

## Run it
1. Open a copy of a `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document. The command adds objects to the board: close it without saving
   afterwards.
2. **ReplicateCopper › Replicate Copper**, or `tools/ad.ps1 -Sample ReplicateCopper run ReplicateCopper "OffsetMM=40"`
3. The result reads `<board>.PcbDoc: replicated N of N objects by 40 mm in X (arcs a, components c, ...); first copy
   (<kind>) moved 40 mm.` and the copies appear 40 mm to the right of the originals. **ReplicateCopper › Count Copper**
   before and after shows each count doubled.

| Command | Parameters | Result |
|---|---|---|
| `ReplicateCopper` | `OffsetMM=<mm>` (optional, default 40, -1000 to 1000) | Number of objects copied per kind and the shift measured on the first copy |
| `CountCopper` | none | Number of objects `ReplicateCopper` would copy, per kind (read-only helper) |

## How it works
`ReplicateCopperCommands.cs`: `CollectFreeCopper` creates a board iterator with eight object
kinds, Top and Multi layers and `eProcessFree`, skips board-shape regions, and stores the objects in a list. The
iterator is destroyed in a `finally` before anything changes, so copies added later are never fetched by it.

`ReplicateCopper` then calls `PreProcess` and the board's `BeginModify`. For each object it calls
`ReplicateWithChildren` (polygons and components, cast to `PCB.IPCB_Group`) or `Replicate` (everything else), adds the
copy with `AddPCBObject`, and moves it with `MoveByXY(MMsToCoord(OffsetMM), 0)` inside the copy's
`BeginModify`/`EndModify`. For the first copy it compares the `BoundingRectangle().Left` of copy and original to report
the shift it really got. `EndModify` and `PostProcess` run in a `finally`, then `GraphicallyInvalidate` redraws the
board. `CountCopper` runs the same collection and only counts.

## Notes
- The objects are collected first and copied afterwards, so a copy on the Top or Multi layer can't be fetched again by
  the same iterator. With this order the iterator fetches exactly the pre-existing objects (1206 on the SpiritLevel
  board), although all the copies land on the same layers. Whether adding copies inside the iterator loop makes it
  loop or skip objects is not known.
- The copies keep the source object's designator, child primitives and nets (53 components became 53
  duplicated designators; a component and its copy both had 10 children and the same child net), so a replicated
  component is a duplicate designator until the caller renames it. The command doesn't adjust nets or designators,
  and copies can land outside the board shape. What DRC reports about the duplicates, and whether the copies keep the
  originals' unique IDs, is not known.
- `MoveByXY` on a component copy moves the whole component: copy and original end up exactly 40 mm apart in X
  (bounding rectangles, which come from the children).
- Open points: how a copied polygon is repoured, and whether the copies can be undone in one step.
- `OffsetMM` is parsed with the invariant culture (`40.5`, not `40,5`) and limited to ±1000 mm so the coordinate stays
  well inside the `int` range of `EDP.Utils.MMsToCoord`.
- Only free objects are copied: pads and tracks that belong to a component are copied with the component, not on
  their own. Objects on other layers (bottom copper, silkscreen not owned by a component) are not copied.
