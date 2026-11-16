# Find a schematic component and zoom to it

Finds the first component on the current schematic sheet whose designator matches a parameter and moves the view
onto it. It shows a component iterator with a designator test, and launching a schematic server process
(`SCH:Zoom`) with `DXP.Utils.RunCommand`.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document, or none.
- `SCH.ISch_Document.SchIterator_Create` / `SCH.ISch_Document.SchIterator_Destroy`: the document owns the
  iterator; destroy it in a `finally` block, also when the loop stops early on a match.
- `SCH.ISch_Iterator.AddFilter_ObjectSet` with `SCH.TObjectSet` and `SCH.TObjectId.eSchComponent`: visit
  components only, so the cast of `SCH.ISch_Iterator.FirstSchObject` / `SCH.ISch_Iterator.NextSchObject` results
  to `SCH.ISch_Component` is safe.
- `SCH.ISch_Component.SchDesignator` and `SCH.ISch_Designator.Text`: the designator text to compare.
- `SCH.ISch_GraphicalObject.BoundingRectangle_Full` returning a `SCH.CoordRect`: the area to zoom to.
- `DXP.Utils.RunCommand`: launches `SCH:Zoom` with `Object=Point|Location.X|Location.Y|Width|Height`.

## Run it
1. Open a schematic sheet and make it active, for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example
   project that ships with Altium Designer (it has `C1`, `U3`, `JP6`, ... but no `C4`).
2. **FindComponent › Find Component** (searches for `C4`, the default), or
   `tools/ad.ps1 -Sample FindComponent run FindComponent "Designator=U3"`
3. The result names the sheet and the component's bounding rectangle, and the view centres on the component.
   Without a match the result says so and the view does not change.

| Command | Parameters | Result |
|---|---|---|
| `FindComponent` | `Designator=<text>` (default `C4`, exact, case-sensitive); `Zoom=true\|false` (default `true`) | `<sheet>: found '<des>', bounding rectangle (x1, y1) - (x2, y2) mil [internal ...]. Ran SCH:Zoom '<params>'.` (or `Zoom=false: view not changed.`), or `<sheet>: no component with designator '<des>' among <n> component(s).` |

## How it works
`FindComponentCommands.cs` reads `Designator` and `Zoom`, gets the schematic server from `SCH.GlobalVars.SchServer`
and the active sheet from `GetCurrentSchDocument` (a `UserException` when either is missing). It creates an
iterator filtered on `eSchComponent` and walks it, comparing `GetState_SchDesignator().GetState_Text()` with the
parameter; the first match ends the loop, and `SchIterator_Destroy` runs in `finally`. For a match it reads
`BoundingRectangle_Full`, computes the centre, width and height, and passes them unchanged to
`DXP.Utils.RunCommand("SCH:Zoom", ...)`, formatted with the invariant culture. The returned text reports the
rectangle in mils and in internal units, and the parameter string it ran.

## Notes
- Read-only for the document: only the view changes, and the sheet is not marked modified.
- The `SCH:Zoom` parameters (`Object=Point`, `Location.X`, `Location.Y`, `Width`, `Height`) are not
  described in the SDK docs. The values are **internal schematic units** (mils would
  zoom to a region 10,000 times too small), and the view ends up centred on the component at roughly the requested
  size.
- `DXP.Utils.RunCommand` returns nothing: the result text says what was asked for, not what the view did. The
  `SCH:Zoom` call is not interactive (no dialog) and does not modify the document.
- The mil values use `EDP.Utils.CoordToMils` (10,000 internal units per mil, documented for PCB). The docs do not
  state that schematic coordinates use the same factor, but they do; the internal values are reported
  as well.
- Only the active sheet is searched, not the other sheets of the project. The first match wins, so for a
  multi-part component the part found first by the iterator is used (the iterator order is not documented).
- The comparison is exact and case-sensitive. A component without a designator object is skipped.
