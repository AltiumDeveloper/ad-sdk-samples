# Create a component with a pin in a schematic library

Adds a new component with one pin to the current schematic library and makes it the current component. It shows how
to create schematic objects with the object factory and add them to their owner: the component to the library, the
pin to the component.

## What it shows
- `SCH.ISch_ServerInterface.SchObjectFactory`: creates the component (`SCH.TObjectId.eSchComponent`,
  `SCH.TObjectCreationMode.eCreate_Default`) and the pin (`SCH.TObjectId.ePin`, `eCreate_GlobalCopy`).
- `SCH.ISch_BasicContainer.ObjectId`: test the current document for `SCH.TObjectId.eSchLib`. A cast to
  `SCH.ISch_Lib` is not a type test.
- `SCH.ISch_Lib.AddSchComponent` and `SCH.ISch_BasicContainer.AddSchObject`: add the component to the library and
  the pin to the component.
- `SCH.ISch_Component.CurrentPartID` / `DisplayMode`: the part and display mode a new pin belongs to.
- `SCH.ISch_Lib.Current_SchComponent`: show the new component in the library editor.
- `DXP.IProcessControl.PreProcess` / `PostProcess` and `SCH.ISch_RobotManager.SendMessage`
  (`SCHMPrimitiveRegistration`): bracket the change and register the new component with the editor.
- `SCH.ISch_Lib.SchLibIterator_Create`: walk the components of a library (used for the counts and `ListComponents`).

## Run it
1. Open a schematic library (for example a copy of `SpiritLevel_2E.SchLib` from the `Libraries` folder of the
   SpiritLevel-SL1 example that ships with Altium Designer) and make it the active document.
2. **CreateSchComponent › Create Sch Component**, or
   `tools/ad.ps1 -Sample CreateSchComponent run CreateSchComponent "LibReference=MyPart"`
3. The library shows a new component with one pin at the origin, and the command returns its name, its pin count
   and the component count before and after. **List Components** shows it in the library.

| Command | Parameters | Result |
|---|---|---|
| `CreateSchComponent` | `LibReference=<name>` (optional) | `<file>: created component '<name>' with 1 pin(s); components <n> -> <n+1>; current component '<name>'.` |
| `ListComponents` | `Max=<n>` (optional, default 200) | `<file>: <n> component(s), current '<name>'.` then one line per component with its pin count |

## How it works
`CreateSchComponentCommands.cs` gets `SCH.GlobalVars.SchServer` and the current document, and stops with a clear
message unless it is a schematic library. With `LibReference` set, it refuses a name the library already has
(`SCH.ISch_Lib.SchComponentByLibRef`). Between `PreProcess` and `PostProcess` it creates the component, moves it to
the origin, gives it the library's owner part and display mode, and adds it with `AddSchComponent`. It then
registers it with the robot manager. It creates a pin, gives it the component's current part and display mode,
adds it with `AddSchObject`, and makes the component the library's current one. After `PostProcess` it calls
`GraphicallyInvalidate` and reads back the component count, the new component's name and pin count, and the
current component. `ListComponents` uses the same library iterator, read-only.

## Notes
- The change is in memory only: the library is modified, not saved. Close it without saving to discard.
- Without `LibReference` the component is added under the name `*`. A second unnamed component would then be
  refused by this sample's duplicate check.
- The library editor shows the new component and its pin without any extra refresh call.
- The `PreProcess`/`PostProcess` bracket with the `SCHMPrimitiveRegistration` message raises no error for a
  component added to a library. The refusal of a duplicate name is this sample's own check, not `AddSchComponent`'s.
- The pin has no name, designator or electrical type; the library editor draws it with the default designator and
  name `0`.
- `ListComponents` counts pins with a pin-filtered iterator on each component.
