# Add a custom parameter to every component

Adds a named parameter, or updates its value, on every component of the current PCB board. It shows how to create a
parameter with the PCB parameters factory and store it on a component through its `PCB.IPCB_PrimitiveParameters` view.

## What it shows
- `PCB.IPCB_ServerInterface.GetParametersFactory`: the factory for parameters and parameter views.
- `PCB.IPCB_ParametersFactory.CreateParameter`: a free-standing `PCB.IPCB_Parameter` with a name, a value and an
  inherited mark. The third argument is `argIsInherited`, not a visibility switch.
- `PCB.IPCB_PrimitiveParameters`: the parameters of a component, obtained by casting the `PCB.IPCB_Component`. The
  cast works in .NET. `PCB.IPCB_ParametersFactory.CreatePrimitiveParameters` is a fallback in case it doesn't.
- `PCB.IPCB_PrimitiveParameters.AddOrSetParameter`: adds the parameter, or replaces the one of the same name, so a
  second run updates and doesn't duplicate.
- `PCB.IPCB_PrimitiveParameters.TryGetValueByName` / `GetValueByNameDef`: tell added from updated, and read the
  value back.
- `PCB.IPCB_BoardIterator` with `AddFilter_ObjectSet` (`PCB.TObjectId.eComponentObject`), `AddFilter_LayerSet`
  (`PCB.PCBConstant.V6SignalLayersSet`) and `AddFilter_Method` (`PCB.TIterationMethod.eProcessAll`).
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify` around
  each component's change.

## Run it
1. Open a `.PcbDoc` with components (for example the board of the SpiritLevel-SL1 example project that ships with
   Altium Designer) and make it the active document.
2. **ComponentParameterAdd › Add Parameter To Components**, or
   `tools/ad.ps1 -Sample ComponentParameterAdd run AddParamToComponent "Name=OpenBOMPartNumber|Value=123456"`
3. The result names the parameter and how many components were changed, on the first run
   `OpenBOMPartNumber='123456' on <n> of <total> components: <n> added, 0 updated, <n> read back with the new value (route: cast).`
   Run it again: the same components are now counted as updated. The parameter is stored in the component's
   parameters and reads back through the same view.

| Command | Parameters | Result |
|---|---|---|
| `AddParamToComponent` | `Name=<text>` (default `OpenBOMPartNumber`), `Value=<text>` (default `123456`) | `<Name>='<Value>' on <n> of <total> components: <a> added, <u> updated, <r> read back with the new value (route: <cast or factory>).` |

An empty `Name` is rejected with `The parameter name is empty.`

## How it works
`ComponentParameterAddCommands.cs` reads `Name` and `Value` and gets the current board and the parameters factory
from `PCB.GlobalVars.PCBServer`. Inside `PreProcess`/`PostProcess`, a board iterator walks the components on signal
layers. For each component, between `BeginModify` and `EndModify`, it creates a parameter with
`CreateParameter(name, value, false)`, casts the component to `IPCB_PrimitiveParameters`, notes whether the name
already exists (`TryGetValueByName`), stores it with `AddOrSetParameter` and reads the value back with
`GetValueByNameDef`. A cast to an SDK interface always succeeds in .NET, so a cast the object doesn't support would
only fail at the first call with a `COMException`. In that case the code uses `CreatePrimitiveParameters(component, "")`,
which is also on the current variant, and the result reports that route. A second, unfiltered iterator counts all
components, so the result shows whether the signal-layer filter skipped any.

## Notes
- The change is in memory only and marks the document modified. Save the document to keep it.
- The parameter goes into the project's current variant (the cast's starting variant) and the user parameter set
  (`PCB.TParameterAppurtenance.paUser`). On a board without variants, this is the default variant
  (`[No Variations]`).
- Names are compared without regard to case: `Name=openbompartnumber` updates the value of an existing
  `OpenBOMPartNumber` and keeps the stored name's case, with one entry per name.
- An empty value (`Value=`) is stored as an empty string.
- The cast view also lists the component's own parameters from the library (for example `Supplier`), in the same
  user set.
- On the SpiritLevel-SL1 board, the signal-layer filter visits all 115 components (top and bottom are signal layers).
