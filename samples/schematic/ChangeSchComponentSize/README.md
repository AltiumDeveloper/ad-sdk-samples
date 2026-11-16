# Pick schematic components and recalculate their size

Lets the user click components on a schematic sheet. For each one it reports the component's properties as a
parameter string and calls `SetState_xSizeySize`. It teaches interactive picking with a hit test, and the
change bracket for a schematic object.

## What it shows
- `SCH.ISch_Document.ChooseLocationInteractively`: prompt for a click on the sheet. It returns false on Escape, so
  the loop can end.
- `SCH.ISch_Document.CreateHitTest` with `SCH.THitTestMode.eHitTest_AllObjects`, `SCH.ISch_HitTest.HitTestCount`
  and `SCH.ISch_HitTest.HitObject`: list the objects under a point.
- `SCH.ISch_BasicContainer.ObjectId` (`SCH.TObjectId.eSchComponent`): pick out the component among the hit
  objects. A C# type test can't do this.
- `SCH.ISch_BasicContainer.ExportToParameters`: an object's properties as one parameter string (format in Notes).
- `SCH.ISch_GraphicalObject.SetState_xSizeySize`: per the reference, sets the X and Y size of the object's
  graphical bounds. `SCH.ISch_GraphicalObject.BoundingRectangle` is read before and after so you can see the effect.
- `SCH.ISch_RobotManager.SendMessage` (`SCHMBeginModify` / `SCHMEndModify`) inside `DXP.IProcessControl.PreProcess`
  / `PostProcess`, then `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: tell the editor about the change and
  redraw.
- `SCH.ISch_GraphicalObject.Selection`: the selected components, used by `Mode=Selected` and `SelectComponents`.

## Run it
1. Open a `.SchDoc` with placed components (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example that
   ships with Altium Designer) and make it the active document.
2. **ChangeSchComponentSize › Change Component Size**, then click components. Click an empty spot or press Escape
   to finish. From the command line: `tools/ad.ps1 -Sample ChangeSchComponentSize run ChangeComponentSize "Mode=Selected"`
   or `... run ChangeComponentSize "At=<x>,<y>"` with an `At` value from `ListComponents`.
3. The result lists each component that was handled: `<designator>: bounds (<x1>, <y1>)-(<x2>, <y2>) -> ...`,
   followed by its `ExportToParameters` text. The sheet is modified but not saved.

| Command | Parameters | Result |
|---|---|---|
| `ChangeComponentSize` | `Mode=Pick` (default, menu only) or `Mode=Selected`; `At=<x,y>` (mils, one hit test, no prompt); `Max=<n>` (pick limit); `Export=true\|false` (default true) | `<file>: SetState_xSizeySize called on <n> component(s) (<how it ended>):`, then one bounds line per component (plus its parameter string) |
| `ListComponents` | `Max=<n>` (default 50) | One line per component: designator, `selected=`, location, bounds, and the bounds centre as `At=<x>,<y>` |
| `SelectComponents` | `Designators=<d1,d2,...>` (empty value clears) | `<n> component(s) selected: ...`, plus any designators that weren't found |

## How it works
`ChangeSchComponentSizeCommands.cs` takes the current sheet from `SCH.GlobalVars.SchServer` and rejects a
schematic library. In `Pick` mode it loops. Each pass calls `ChooseLocationInteractively` and stops if it returns
false. It then runs `CreateHitTest` at the clicked point and stops if nothing is hit. If something is hit, it takes the first hit object whose `ObjectId` is `eSchComponent`. Clicks on other
objects are ignored.
`Resize` reads `ExportToParameters` and `BoundingRectangle`. It then calls `SetState_xSizeySize` between
`SCHMBeginModify` and `SCHMEndModify`, inside `PreProcess`/`PostProcess` on the sheet, redraws with
`GraphicallyInvalidate`, and reads the bounds again.
`Mode=Selected` iterates the sheet's components (`SchIterator_Create` with an `eSchComponent` filter) and keeps the
selected ones. `At=x,y` runs a single hit test without a prompt.
`ListComponents` and `SelectComponents` are helpers that make the non-interactive paths easy to drive.

## Notes
- `Mode=Pick` waits for clicks, so it only runs from the menu. Through `tools/ad.ps1` it returns an error and never
  blocks Altium Designer.
- On a placed, unchanged component `SetState_xSizeySize` leaves `BoundingRectangle` exactly as it was:
  it recalculates the graphical bounds from the object's content, so it matters after the content changed, not as
  a resize. The document is still marked modified, because of the change bracket.
- A hit test with `eHitTest_AllObjects` inside a component returns the component itself (`eSchComponent`) **and**
  the child primitive under the point (for example `ePolyline` for a resistor), the component first. Off the
  sheet, `HitTestCount` is 0. The returned text lists all hit object IDs.
- A component's `Location` is its symbol anchor (for a resistor, a pin end), not the centre of its bounds.
- Coordinates are converted with `EDP.Utils.CoordToMils` / `MilsToCoord` (10,000 internal units per mil). The
  docs' API conventions name these helpers for coordinates in general with PCB examples only; they are
  also right for schematic coordinates (the reported mils match the drawing).
- `ExportToParameters` returns the object's own properties as `Key=Value` pairs joined by `|` (booleans `T`/`F`,
  colours as integers), with no child objects. Its `LOCATION.X` / `LOCATION.Y` are in the schematic file unit of
  10 mils per unit, so they are a tenth of the mil values this sample prints.
- Each run is bracketed with `PreProcess`/`PostProcess`. The change is in memory only.
- `SelectComponents` sets `Selection` directly, without a modify bracket. Selection is view state, not design data.
