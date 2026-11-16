# Hit-test a schematic click and list component parameters

Asks for a click on the current schematic sheet, finds the component at that point with a hit test, and lists its
parameters. It shows interactive point picking, hit testing, and iterating a component's child objects.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document. A schematic library is an
  `SCH.ISch_Document` too, so the sample rejects it by its `ObjectId` (`SCH.TObjectId.eSchLib`).
- `SCH.ISch_Document.ChooseLocationInteractively`: prompts for a click and fills in a `DXP.Point` passed by
  reference. The point must be constructed first. The method returns false when the user cancels.
- `SCH.ISch_Document.CreateHitTest` with `SCH.THitTestMode.eHitTest_AllObjects`: every object under a point,
  read with `SCH.ISch_HitTest.HitTestCount` and `SCH.ISch_HitTest.HitObject`.
- `SCH.ISch_BasicContainer.ObjectId`: identifies the hit object as a component (`SCH.TObjectId.eSchComponent`).
  A C# type test would not work, because the SDK's COM objects accept every interface cast.
- `SCH.ISch_BasicContainer.SchIterator_Create` on the component, with `SCH.ISch_Iterator.AddFilter_ObjectSet`
  (`SCH.TObjectId.eParameter`), `FirstSchObject` / `NextSchObject` and `SchIterator_Destroy`: walks the
  component's parameters.
- `SCH.ISch_Parameter.Name`, `SCH.ISch_BasicContainer.Text` and `SCH.ISch_BasicContainer.ExportToParameters`: each
  parameter's name, value and full property string.

## Run it
1. Open a schematic sheet with components (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project
   that ships with Altium Designer) and make it the active document.
2. **SchComponentHitTest › Check Component Property**, then click on a component's body. From the command line,
   pass the point: `tools/ad.ps1 -Sample SchComponentHitTest run CheckComponentProperty "At=<x>,<y>"`.
   **List Components** (`run ListComponents`) prints a usable `At=` value for each component.
3. The result names the component and lists one `Name = 'Value'` line per parameter, each followed by its
   `ExportToParameters` text.

| Command | Parameters | Result |
|---|---|---|
| `CheckComponentProperty` | none (prompts for a click, menu only), or `At=<x,y in mils>`; `Export=false` leaves out the `ExportToParameters` lines | `<file>: component <designator> at (x, y) (hit objects: ...), <n> parameter(s):` then the parameter lines; or `no component at ...`, `nothing at ...`, `no location chosen (cancelled).` |
| `ListComponents` | `Max=<n>` (default 50) | `<file>: <n> component(s):` then `<designator> bounds=(..)-(..) At=<x>,<y>` per component |

## How it works
`SchComponentHitTestCommands.cs` gets the current sheet from `SCH.GlobalVars.SchServer`, with a `UserException`
when no schematic editor or sheet is active, or when the document is a library. `CheckComponentProperty` either
parses `At` into a point (mils converted with `EDP.Utils.MilsToCoord`) or calls `ChooseLocationInteractively`.
The prompt is refused when the command runs through `tools/ad.ps1`, because it would block Altium Designer until
someone clicks. It then calls `CreateHitTest`, scans the hit objects for the first one whose `ObjectId` is
`eSchComponent`, and lists all hit object IDs in the result. `Parameters` creates a child iterator on that
component, filters it to `eParameter`, reads `Name`, `Text` and `ExportToParameters` from each one, and destroys
the iterator in a `finally` block. `ListComponents` iterates the sheet's components and prints the centre of each
bounding rectangle as an `At` value.

## Notes
- Read-only: nothing on the sheet is changed.
- A hit test inside a component's bounding rectangle returns the component itself first
  (`eSchComponent`), followed by the primitive under the point (`ePolyline` for a resistor). The result lists all
  hit object IDs, so you can see what else was under the click.
- A hit test at an empty point returns a valid hit test with `HitTestCount` 0, not null.
- `ExportToParameters` returns `KEY=VALUE` pairs joined with `|` (keys upper case, no spaces around `=`, booleans
  `T`); its `LOCATION.X/Y` are the SchDoc record values in units of 10 mils, not the internal units the API uses.
  The sample prints the string unchanged. The reference does not describe the format.
- Coordinates are converted with `EDP.Utils.CoordToMils` / `MilsToCoord`: 10,000 internal units per mil applies to
  schematic coordinates as well.
- When `ChooseLocationInteractively` returns `false`, the command returns without a hit test, whatever the point
  holds.
