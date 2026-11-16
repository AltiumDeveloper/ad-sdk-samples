# Jump to a component pin in the schematic

Jumps to a pin of a placed component on the current schematic sheet. By default it launches the schematic editor's
own cross-probe process `Sch:CrossProbeNotify` with `DXP.Utils.RunCommand`. With `Method=Select` it finds the pin with
schematic iterators and selects it.

## What it shows
- `DXP.Utils.RunCommand`: launch a built-in server process (`Sch:CrossProbeNotify`) with a `Key=Value|Key=Value`
  parameter string. It returns nothing, so the result can't be read back.
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document, or null.
- `SCH.ISch_BasicContainer.SchIterator_Create` / `SCH.ISch_BasicContainer.SchIterator_Destroy`: an iterator over a
  sheet, and a second one over the children of a component.
- `SCH.ISch_Iterator.AddFilter_ObjectSet` with `SCH.TObjectId.eSchComponent` and `SCH.TObjectId.ePin`: only
  components, then only pins.
- `SCH.ISch_Iterator.IterationDepth` = `SCH.TIterationDepth.eIterateAllLevels`: pins of every part are found.
- `SCH.ISch_Component.SchDesignator` / `SCH.ISch_Designator.Text`: the component's designator.
- `SCH.ISch_Pin.Designator` and `SCH.ISch_Pin.Name`: the pin number to match, and its name for the report.
- `SCH.ISch_GraphicalObject.Selection` and `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: select the pin and
  redraw it.

## Run it
1. Open `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with Altium Designer (or any sheet) and
   make it the active document.
2. **SchJumpToPin › Jump To Pin**, or `tools/ad.ps1 -Sample SchJumpToPin run JumpToPin "Designator=U3|Pin=2"`
3. The result is `SL_Power.SchDoc: found pin '2' (name 'OUT') of 'U3', part 1. Ran Sch:CrossProbeNotify
   'Kind=Pin|Id=2:U3'. Pin selected afterwards = False.` The view zooms onto the pin and centres it; the pin is
   not selected (`Pin selected afterwards = False`, printed as `True`/`False`).
   From the menu the defaults `HDR3` / pin `8` are used: on a sheet without `HDR3` the result says so and the
   cross-probe still runs.

| Command | Parameters | Result |
|---|---|---|
| `JumpToPin` | `Designator=<text>` (default `HDR3`), `Pin=<pin designator>` (default `8`), `Method=CrossProbe` (default) or `Select` | What was found on the sheet (or the component's pins when the pin is missing), then `Ran Sch:CrossProbeNotify '<params>'` or `Method=Select: pin selected = True` |

## How it works
`SchJumpToPinCommands.cs`: `JumpToPin` reads the parameters and gets the current sheet from
`SCH.GlobalVars.SchServer`. `FindPin` walks the components of the sheet with an iterator filtered to
`eSchComponent`, compares `SchDesignator.Text` case-insensitively, and for the first match opens an iterator on the
component, filtered to `ePin` with depth `eIterateAllLevels`, and compares each pin's `Designator`. Both iterators
are destroyed in `finally` blocks.

`Method=CrossProbe` then calls `DXP.Utils.RunCommand("Sch:CrossProbeNotify", "Kind=Pin|Id=<pin>:<designator>")`, even
when the pin is not on the sheet. It reports the pin's `Selection` afterwards as a hint. `Method=Select` sets
`Selection` to true on the pin and calls `GraphicallyInvalidate`. It doesn't move the view.

## Notes
- `Sch:CrossProbeNotify` and its parameters (`Kind=Pin`, `Id=<pin designator>:<component designator>`) are not in
  the SDK reference or guides. It zooms the editor onto the pin and centres it, does not select or highlight it,
  acts on the active sheet only (no switch to another sheet of the project), and does not mark the document
  modified.
- For an unknown component or pin (for example a pin name instead of its designator), `Sch:CrossProbeNotify`
  silently does nothing: no error, no dialog, the view stays where it was. `RunCommand` returns nothing, so a
  caller cannot tell a jump from an ignored call. That is why this sample looks the pin up itself and reports what
  it found.
- The pin is matched by `Designator` (the pin number), not by `Name`. On `SL_Power.SchDoc`, `D1` has pins `1` (name
  `A`) and `2` (name `K`). Component designators match case-insensitively. `eIterateAllLevels` returns a
  component's pins in storage order (`3, 2, 1` for U3), not sorted.
- Pins and designators containing `|` or `:` can't be passed: `|` separates the command parameters, and `:`
  separates pin and component in the cross-probe `Id`.
- `Method=Select` adds the pin to the current selection and nothing is restored afterwards. With
  `SetState_Selection(true)` plus `GraphicallyInvalidate()` the selection marquee is drawn on the pin at once, with
  no `SCHMBeginModify`/`SCHMEndModify`. Selection is view state: the document stays unmodified. This sample doesn't
  zoom in Select mode.
- For a multi-part component the first pin with the designator is used, whichever part it belongs to. The result
  gives the part number (`OwnerPartId`).
