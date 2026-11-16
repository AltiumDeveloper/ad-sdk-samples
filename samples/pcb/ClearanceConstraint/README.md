# Read and change a clearance rule's pair value

Finds a clearance rule on the current board by name, multiplies the clearance it requires between arcs and tracks,
and reports the value before and after in metric units. It shows how to iterate over a board's design rules and how to
read and write a per-pair clearance of `PCB.IPCB_ClearanceConstraint`.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eRuleObject`, `PCB.IPCB_AbstractIterator.AddFilter_AllLayers`
  and `PCB.IPCB_BoardIterator.AddFilter_Method`: design rules are board objects, found with a board iterator.
- `PCB.IPCB_Rule.RuleKind` and `PCB.IPCB_Rule.Name`: pick the rule by kind (`PCB.TRuleKind.eRule_Clearance`) before
  casting it to `PCB.IPCB_ClearanceConstraint`, since type tests on SDK interfaces always succeed.
- `PCB.IPCB_ClearanceConstraint.Clearance_1` (`GetClearance_1` / `SetClearance_1`): the clearance for a pair of object
  kinds, given as `PCB.TObjectClearanceId` values (here `eObjectClearanceID_Arc` and `eObjectClearanceID_Track`).
- `PCB.IPCB_ClearanceConstraint.Gap` and `PCB.IPCB_ClearanceConstraint.Mode`: the rule's clearance for all other pairs,
  and whether any pair has a value of its own. Both are reported, because a pair assignment can change `Gap`.
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify`: the
  change bracket, applied to a rule.
- `EDP.Utils.CoordUnitToString`: internal units to a metric string.

## Run it
1. Open a `.PcbDoc` that has a clearance rule named `Clearance` (the default rule set has one; for example the board
   of the SpiritLevel-SL1 example project that ships with Altium Designer) and make it the active document.
2. **ClearanceConstraint › Change Clearance**, or `tools/ad.ps1 -Sample ClearanceConstraint run ChangeClearance "Rule=Clearance|Factor=3"`
3. The result names the rule and shows the Arc-Track clearance before and after (three times larger), and the rule's
   Gap and Mode before and after. The document is modified, not saved.

| Command | Parameters | Result |
|---|---|---|
| `ChangeClearance` | `Rule=<name>` (default `Clearance`, case-insensitive), `Factor=<positive number>` (default `3`) | `Rule '<name>': Arc-Track clearance <before> -> <after> (x<factor>, requested <value>). Gap <before> -> <after>. Mode <before> -> <after>. …` |

When no rule matches, the command fails with `No clearance rule named '<name>' on this board.` and lists the board's
clearance rules.

## How it works
`ClearanceConstraintCommands.cs` reads and validates the `Rule` and `Factor` parameters, then gets the current board
from `PCB.GlobalVars.PCBServer`. `FindClearanceRule` creates a board iterator for rule objects on all layers, skips
rules whose `RuleKind` is not `eRule_Clearance`, and compares names case-insensitively. It destroys the iterator in a
`finally` block and returns the names of all clearance rules, which the error message lists when no rule matches.
`ChangeClearance` reads `GetClearance_1(Arc, Track)`, `Gap` and `Mode`, computes the new value (rounded to internal
units), and assigns it with `SetClearance_1` between `PreProcess`/`BeginModify` and `EndModify`/`PostProcess`. It
then reads the three values back and returns them.

## Notes
- Clearances are in internal units (10,000 per mil); the result shows them with `CoordUnitToString` in metric units,
  for example `0.2mm` (shortest decimal, `mm` suffix, no space).
- The order of the two object kinds does not matter. A pair without a value of its own reads as `Gap`. By the
  reference, each assignment can change `Gap`, because the value shared by most pairs becomes the gap. The command
  reports `Gap` before and after so that the effect is visible.
- The change is made in memory only. Save the document to keep it. No DRC runs: run Design Rule Check to see the
  effect on violations.
- A rule of kind `eRule_Clearance` casts to `PCB.IPCB_ClearanceConstraint`, and the
  bracketed change marks the document modified. On the SpiritLevel-SL1 board, setting the Arc-Track pair leaves `Gap`
  at 0.2mm, and `Mode` is already `ObjectsClearance` on the default `Clearance` rule.
