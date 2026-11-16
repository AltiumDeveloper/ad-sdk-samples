# Check a picked via against a hole size rule

Checks one via against a hole size design rule that exists only while the command runs: the rule is created with
the PCB rule factory, added to the board, configured, asked for a violation with `ActualCheck`, and removed again.
It shows how to create and configure a rule object in code and how to check a single object against it.

## What it shows
- `PCB.IPCB_ServerInterface.PCBRuleFactory` with `PCB.TRuleKind.eRule_MaxMinHoleSize`: create a design rule of a
  given kind. It is not on a board until `PCB.IPCB_Board.AddPCBObject` adds it.
- `PCB.IPCB_MaxMinHoleSizeConstraint` (`AbsoluteValues`, `MinLimit`, `MaxLimit`, `MinPercent`, `MaxPercent`): the
  constraint values of a hole size rule. Cast the `PCB.IPCB_Rule` to this interface to set them. Limits are in
  internal units (10,000 per mil), percentages are plain numbers.
- `PCB.IPCB_Rule.ActualCheck`: check one object against the rule (second object null for a single-object rule). It
  returns a `PCB.IPCB_Violation`, or null when there is no violation.
- `PCB.IPCB_Violation.Name` / `PCB.IPCB_Violation.Description`: what the violation says.
- `PCB.IPCB_Board.AddPCBObject` / `PCB.IPCB_Board.RemovePCBObject`: put the rule (and the violation) on the board
  for the check and take them off again, between `PCB.IPCB_ServerInterface.PreProcess` and `PostProcess`.
- `PCB.IPCB_Board.ChooseLocation` and `PCB.IPCB_Board.GetObjectAtXYAskUserIfAmbiguous` with
  `PCB.TObjectId.eViaObject`, `PCB.PCBConstant.V6AllLayersSet` and `PCB.TEditingAction.eEditAction_Select`: let
  the user click a via (menu only).
- `PCB.IPCB_Board.SelectecObjectCount` / `PCB.IPCB_Board.SelectecObject`: use a via that is already selected.
- `PCB.IPCB_Via.HoleSize` / `PCB.IPCB_Via.Size`: the values the rule checks.

## Run it
1. Open any `.PcbDoc` with a via and make it the active document. On a board without vias, run
   **ViaHoleSizeCheck › Add Test Via** first (a 10 mil hole, 20 mil diameter via near the bottom left corner).
2. Select a via, then **ViaHoleSizeCheck › Check Via Hole Size** (with nothing selected the menu asks you to click a
   via), or `tools/ad.ps1 -Sample ViaHoleSizeCheck run CheckViaHoleSize "X=<mil>|Y=<mil>"` with a location from
   `ListVias`.
3. The result names the via (location, hole, diameter, net), its hole as a percentage of the diameter, the rule
   limits and either `no violation: the hole size is within the limits.` or `violation '<name>': <description>`.
   With the default limits (1 to 2 mil) nearly every real via violates the rule.

| Command | Parameters | Result |
|---|---|---|
| `CheckViaHoleSize` | `X=<mil>`, `Y=<mil>` (from the board origin; optional, but pass both — the missing one is taken as 0), `MinMil=1`, `MaxMil=2`, `MinPercent=20`, `MaxPercent=80`, `Percent=true` (check the percentages instead of the absolute limits), `AddViolation=false` (do not add the violation to the board and remove it) | Via, limits and the violation or `no violation` |
| `ListVias` | `Max=20` (how many to list) | `<board>: <n> vias:` and one line per via: location in mils from the board origin, hole, diameter, net |
| `AddTestVia` | `X=<mil>`, `Y=<mil>` (optional; default 500 mil inside the bottom left corner of the board outline — pass both, a missing one is taken as 0), `HoleMil=10`, `SizeMil=20` | `Placed Via at X=..., Y=... mil, ...` |

## How it works
`ViaHoleSizeCheckCommands.cs`: `CheckViaHoleSize` first finds the via. With `X`/`Y` it looks through the board's
vias (board iterator on `eViaObject`) for the one whose pad contains the point, so nothing waits for a user. Without
them it takes the first selected via. When nothing is selected and the command was started from the menu, it calls
`ChooseLocation` and `GetObjectAtXYAskUserIfAmbiguous`. Called through `tools/ad.ps1` it raises
an error instead, because those calls wait for a click and would block AD.

Between `PreProcess` and `PostProcess` it creates the rule with `PCBRuleFactory(eRule_MaxMinHoleSize)`, names it
`ViaHoleSizeCheck_Temp`, adds it to the board, casts it to `IPCB_MaxMinHoleSizeConstraint` and sets the limits
(mils converted with `EDP.Utils.MilsToCoord`). `ActualCheck(via, null)` runs the check. If a violation comes back,
its name and description are read, then it is added to the board and removed again (skip that
with `AddViolation=false`). The rule is removed in a `finally`, so a failure cannot leave it on the board.

`ListVias` and `AddTestVia` are helpers: one shows where the vias are, the other places a via with a known hole size
with `PCBObjectFactory(eViaObject, ...)` and `AddPCBObject`.

## Notes
- Units: `HoleSize`, `Size`, `MinLimit` and `MaxLimit` are internal units (10,000 per mil). Locations in parameters
  and results are mils relative to the board origin (`XOrigin`/`YOrigin`).
- `AbsoluteValues` selects which pair of limits the rule checks. Setting both pairs while checking the absolute ones
  leaves the percentages without effect. Here `Percent=true` checks the percentages.
- The default limits (1 to 2 mil) are smaller than any usual drill, so expect a
  violation for real vias. Pass `MaxMil=` to see the `no violation` case.
- The reference says `ActualCheck` returns null when there is no violation, so this sample checks for null before it
  adds the result to the board.
- A type test such as `rule as IPCB_MaxMinHoleSizeConstraint` succeeds for any SDK object (they are all `IDispatch`
  interfaces). The rule kind and `ObjectID` checks are what make the casts valid.
- The rule **must be on the board** before `ActualCheck`. A rule straight from `PCBRuleFactory`,
  configured but not passed to `AddPCBObject`, makes `ActualCheck` throw a `NullReferenceException` inside the COM
  call, so the add-and-remove is required.
- `ActualCheck(via, null)` returns null when the hole is within the limits, and otherwise a
  violation named like `0002: Via` (a global counter that advances by two for each violation returned) with the
  description `Actual Hole Size = 0.254mm` (the measured value in the board's display unit), or
  `Actual Hole Size = 50%` with percentage limits. `DataSummaryString` follows `AbsoluteValues`:
  `Min = 0.025mm    Max = 0.051mm` or `Min = 20%    Max = 80%`.
- Adding the returned violation to the board and removing it again raises no error and leaves the
  via count unchanged.
- Open points: whether a violation that is never added to the board has to be destroyed explicitly; whether the
  add/remove of the rule or of the violation leaves entries in the undo history or marks the document modified;
  what `ActualCheck` returns for a back-drilled via (the reference says absolute checks skip them).
- `AddTestVia` changes the board in memory. Close the document without saving to discard it.
