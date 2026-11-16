# Place a DIFFPAIR parameter set directive

Places a parameter set named `DIFFPAIR`, holding one hidden string parameter `DifferentialPair=TRUE`, on the
current schematic sheet. It shows how to build a container object and its child with the schematic object factory
and add both to a sheet.

## What it shows
- `SCH.ISch_ServerInterface.SchObjectFactory`: create a parameter set (`SCH.TObjectId.eParameterSet`) and a
  parameter (`SCH.TObjectId.eParameter`) with `SCH.TObjectCreationMode.eCreate_Default`.
- `SCH.ISch_GraphicalObject.Location`: position the set with a `DXP.Point` built from `EDP.Utils.MilsToCoord`.
- `SCH.ISch_ParameterSet.Name`: the set's name (`DIFFPAIR`).
- `SCH.ISch_Parameter.Name`, `SCH.ISch_BasicContainer.Text`, `SCH.ISch_Parameter.ParamType`,
  `SCH.ISch_ComplexText.IsHidden`, `SCH.ISch_Parameter.ShowName`: configure the parameter.
- `SCH.ISch_BasicContainer.AddSchObject`: add the parameter to the set, then the set to the sheet.
- `DXP.IProcessControl.PreProcess` / `PostProcess` and `SCH.ISch_RobotManager.SendMessage` with
  `SCH.SCHConstant.SCHMPrimitiveRegistration`: tell the editor about the new object.
- `SCH.ISch_BasicContainer.SchIterator_Create`: read back the sets of the sheet and the parameters of each set
  (a parameter set is a container, iterated like a sheet).

## Run it
1. Open any `.SchDoc` (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document.
2. **SchDifferentialPair › PlaceDiffPairDirective** in the schematic editor, or
   `tools/ad.ps1 -Sample SchDifferentialPair run PlaceDiffPairDirective "X=2400|Y=4000"`
3. The command returns the placed set as read back, for example
   `'DIFFPAIR' at (2400, 4000) mil [internal 24000000, 40000000], parameters: DifferentialPair='TRUE' (...)`,
   and the number of parameter sets on the sheet before and after. The sheet is modified, not saved.

| Command | Parameters | Result |
|---|---|---|
| `PlaceDiffPairDirective` | `X=<mil>`, `Y=<mil>` (default 2400, 4000), `Name=<set name>` (default `DIFFPAIR`), `Value=<text>` (default `TRUE`) | Adds the set to the current sheet and returns it as read back |
| `ListParameterSets` | `Max=<n>` (default 200) | Lists the sheet's parameter sets with location and parameters (read-only) |

## How it works
`Commands/SchDifferentialPairCommands.cs`: `PlaceDiffPairDirective` gets the sheet with
`SCH.ISch_ServerInterface.GetCurrentSchDocument` (and rejects a schematic library). Between
`DXP.IProcessControl.PreProcess` and `PostProcess` it creates the parameter set with `SchObjectFactory`, sets its
location and name, creates the parameter and sets its name, text, type (`SCH.TParameterType.eParameterType_String`),
hidden flag and name visibility. The parameter is added to the set with `AddSchObject` and the set to the sheet
with `AddSchObject`; the sample then sends `SCHMPrimitiveRegistration` for the set to the robot manager and calls
`GraphicallyInvalidate`. `ListParameterSets` iterates the sheet with an `SCH.ISch_Iterator.AddFilter_ObjectSet`
filter on `eParameterSet` and, for each set, its own iterator filtered on `eParameter`.

## Notes
- Units: the command takes mils and converts with `EDP.Utils.MilsToCoord`. Schematic coordinates use the same
  factor as PCB coordinates (10,000 per mil): `MilsToCoord(2400)` is 24000000 internal units, and the directive is
  drawn 2400 mil from the sheet's left edge. The internal units are not nanometres.
- Registration: the docs example for `DXP.IProcessControl.PreProcess` uses
  `ISch_Document.RegisterSchObjectInContainer` plus `SCHMPrimitiveRegistration` for a new object. `AddSchObject` on
  the sheet plus `SCHMPrimitiveRegistration` inside `PreProcess`/`PostProcess` is enough: the set is drawn, a fresh
  iterator finds it and the sheet is marked modified, with no `RegisterSchObjectInContainer`.
- A parameter set acts as a directive when it is attached to a wire, bus or net. A set placed at an arbitrary
  location is probably not attached to anything. Use `X`/`Y` to place it on a wire.
- The parameter's own position inside the set is never set; where it ends up is not known.
- The change is in memory only: save the sheet to keep it, or undo it.
