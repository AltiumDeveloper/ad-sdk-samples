# Place a library component with models and parameters

Places a component from a schematic library on the active sheet with one call to
`SCH.IIntegratedLibraryManager.PlaceLibraryComponent`. Everything beyond "which component from which library" goes
into the call's third argument, an AD style `Key=Value|Key=Value` string: the location, PCB footprint models,
component parameters, or the part of a multi-part component.

## What it shows
- `EDP.Utils.LoadIntegratedLibraryManager`: the .NET way to get the integrated library manager.
- `SCH.IIntegratedLibraryManager.PlaceLibraryComponent`: place a component by library reference and library path;
  returns `true`/`false` and nothing else.
- The extra-parameter string: `Location.X`/`Location.Y` (internal units, from `EDP.Utils.MilsToCoord`),
  `PartID=<n>`, `PlacingFromDatabase=true`, `ModelTypeN`/`ModelNameN`/`LocationN` for footprint models and
  `ParameterNameN`/`ParameterValueN`/`ParameterVisibleN` for component parameters. Only the location keys are
  mentioned in the SDK reference. See **Notes** for what each key does.
- `SCH.IIntegratedLibraryManager.InstalledLibraryCount` / `InstalledLibraryPath`: check that a bare library name
  is installed before passing it.
- `SCH.ISch_Component.CurrentPartID`, `SCH.ISch_Component.LibReference`, `SCH.ISch_Implementation.ModelName`,
  `SCH.ISch_Parameter.Name`: read the placed component back, because the call itself reports nothing.

## Run it
1. Open a schematic sheet, for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with
   Altium Designer, and make it active. The example's `Libraries` folder has `SpiritLevel_2E.SchLib` (with `RES`
   and the multi-part `XC2S300E-6PQ208C`) and `Spirit_Level_Project.PcbLib` (with the footprint `CR2012-0805`).
2. `tools/ad.ps1 -Sample PlaceLibraryComponent run PlaceComponent "Lib=<folder>/SpiritLevel_2E.SchLib|LibRef=RES|Models=CR2012-0805@<folder>/Spirit_Level_Project.PcbLib|X=1000|Y=1000"`,
   or `tools/ad.ps1 -Sample PlaceLibraryComponent run PlacePart "Lib=<folder>/SpiritLevel_2E.SchLib|PartID=2"`.
   The menu items **PlaceLibraryComponent › Place Component** and **Place Part** cannot know a library path, so
   from the menu they report the missing `Lib` parameter.
3. The command returns the call's result, the parameter string it passed and one line per new component on the
   sheet: designator, library reference, part, location in mils, models and parameters.

| Command | Parameters | Result |
|---|---|---|
| `PlaceComponent` | `Lib=` (required), `LibRef=` (default `RES2`), `Models=footprint@pcblib;...`, `Params=Name=Value;...` (default four demo parameters, empty = none), `X=`/`Y=` mils (default 1000), `Interactive` (AD UI only) | `PlaceLibraryComponent('RES', 'SpiritLevel_2E.SchLib') on SL_Power.SchDoc returned True. Parameters: ... 1 new component(s): ...` |
| `PlacePart` | `Lib=` (required), `LibRef=` (default `XC2S300E-6PQ208C`), `PartID=` (default 2), `X=`/`Y=`, `Interactive` | the same, with `part <n> of <count>` for the new component |

## How it works
`PlaceLibraryComponentCommands.cs` builds the parameter list for each command, then both share `Place`.
`PlaceComponent` adds `PlacingFromDatabase=true`, one `ModelTypeN=PCBLIB|ModelNameN|LocationN` group per
`Models` entry and one hidden `ParameterNameN|ParameterValueN|ParameterVisibleN=False` group per `Params` entry.
`PlacePart` adds `PartID=<n>`. Both add `Location.X`/`Location.Y`, converted from mils with
`EDP.Utils.MilsToCoord`, unless `Interactive` is set.

`Place` gets the active sheet from `SCH.GlobalVars.SchServer`, loads the manager with
`EDP.Utils.LoadIntegratedLibraryManager` and checks the library: a full path must exist, a bare file name must
match one of the installed libraries. It lists the sheet's components with an `SCH.ISch_Iterator`, calls
`PlaceLibraryComponent`, calls `GraphicallyInvalidate` on the sheet and lists the components again. The
difference is the new component, described with its `CurrentPartID`, its `ISch_Implementation` models (`*` marks
the current one) and its `ISch_Parameter` children.

## Notes
What the keys do:
- `Location.X`/`Location.Y` in internal units place the component **without a click**: the call returns at once
  and the component sits at exactly the point passed, with no grid snapping.
- `PartID` is **1-based**: `PartID=2` places part 2 of 3 (`U?B`) of `XC2S300E-6PQ208C`.
- `ParameterNameN`/`ParameterValueN`/`ParameterVisibleN` work and add to the library component's own parameters;
  `ParameterVisibleN=False` makes them hidden.
- `PlacingFromDatabase=true` is accepted with no visible effect and no dialog.
- A full path to a `.SchLib` that is **not** an installed library is accepted, and the call returns `True`.
- `ModelTypeN`/`ModelNameN`/`LocationN` have **no observable effect**: the placed component carries the same models
  with the same current one with and without the keys, and nothing is added from the named `.PcbLib`. The call
  still returns `True`, so an ignored key is not reported.

Also:
- Without `Location.X`/`Location.Y` the component is expected to attach to the cursor until the user clicks. That would block `tools/ad.ps1`, so `Interactive` is refused there.
- The library is checked before the call, because what `PlaceLibraryComponent` does with a library it cannot find
  (an error dialog, `false`, or nothing) is not documented.
- The call is not bracketed with `SCHMBeginModify`/`SCHMEndModify`: the manager adds the component itself, and the
  sheet is marked modified without one.
- The default `Params` are four demo parameters (`PartNumber=931H451`, `Value=100R`, `Power=1/4W`,
  `Tolerance=5%`).
- `Params` and `Models` use `;` between entries because `|` separates the command's own parameters.
- The change is in memory only. Close the sheet without saving to discard it.
