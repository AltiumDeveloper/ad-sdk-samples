# Convert 3D bodies in a PCB library to generic STEP

Replaces every non-generic 3D body model (extruded, cylinder, sphere) of every footprint in the focused PCB library
with a generic copy. It shows how to walk all footprints of a library and the bodies of each footprint, and how to
swap a body's model with `PCB.IPCB_Model.I_ReplicateToGeneric`.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary`: the library of the focused PCB library document, or null when the
  focused document is not a .PcbLib.
- `PCB.IPCB_Library.LibraryIterator_Create` / `LibraryIterator_Destroy`: every footprint (`PCB.IPCB_LibComponent`)
  of the library, not only the current one.
- `PCB.IPCB_LibComponent.GroupIterator_Create` / `GroupIterator_Destroy` with
  `PCB.IPCB_GroupIterator.AddFilter_ObjectSet` and `AddFilter_AllLayers`: the 3D bodies of one footprint.
- `PCB.IPCB_ComponentBody.Model` and `PCB.IPCB_Model.ModelType` (`PCB.T3DModelType`): the body's model and its kind.
- `PCB.IPCB_Model.I_ReplicateToGeneric`: a generic copy of a model, assigned back with `SetModel`.
- `PCB.IPCB_ComponentBody.ModelFactory_CreateCylinder` (helper command only): a new body with a cylinder model.

## Run it
1. Copy a PCB library to a work folder, for example `Spirit_Level_Project.PcbLib` from the SpiritLevel-SL1 example
   project that ships with Altium Designer. Open the copy and focus it.
2. **ConvertToStep › Convert To Step**, or `tools/ad.ps1 -Sample ConvertToStep run ConvertToStep "DryRun=true"` to
   count first, then `tools/ad.ps1 -Sample ConvertToStep run ConvertToStep`.
3. The result lists the footprints, bodies and model types before and after. After a real run, "Model types after"
   lists only `Generic`, and the library is marked modified. If the library has no non-generic bodies, run
   `AddTestBody` first.

| Command | Parameters | Result |
|---|---|---|
| `ConvertToStep` | `DryRun=true` (optional) | `Converted: <n> footprints, <m> 3D bodies (...)`, model types before/after, `<k> bodies converted to generic, <f> failed.` With `DryRun`, `<k> bodies would be converted. Nothing was changed.` |
| `AddTestBody` | `Radius=<mils>` (default 20), `Height=<mils>` (default 50) | `Added 3D body 'ConvertToStep_TestCylinder' (Cylinder model, ...) to footprint '<name>'.` |

## How it works
`ConvertToStepCommands.cs`, `ConvertToStep`: gets the library from `GetCurrentPCBLibrary` and walks it with the library
iterator. For each footprint it creates a group iterator filtered to `TObjectId.eComponentBodyObject` on all layers.
For each body with a model that is not `e3DModelType_Generic`, it calls `I_ReplicateToGeneric` and assigns the copy
with `SetModel`, between `BeginModify` and `EndModify`. One `PreProcess`/`PostProcess` pair brackets the whole run.
The model type is read back from the body after the swap, and that is what the result counts. Both iterators are
destroyed in `finally` blocks. Last, the library document is marked modified (`IServerDocument.SetModified`).

`AddTestBody` creates a body with `PCBObjectFactory` and gives it a cylinder model from `ModelFactory_CreateCylinder`
on Mechanical 1. It moves the body to the footprint's reference point, then adds it to the library board with
`IPCB_Board.AddPCBObject`. That call already puts the body into the current footprint; adding it again with
`IPCB_LibComponent.AddPCBObject` lists it twice in the footprint, so that second call is made only when the footprint
did not gain a body.

## Notes
- The change is made in memory only. Save the library to keep it, or close it without saving.
- The model type of every body, in the current footprint or not, reads back as Generic after the swap.
- The reference says `I_ReplicateToGeneric` returns a copy whose `Embed` is true, and that `SetModel` stores a copy of
  the model. The sample does not call `ModelFactory_Destroy` on the returned copy.
- The menu appears in the PCB editor menu. The extension template has no menu target for the PCB library editor.
- `AddTestBody` is a helper that adds a body to convert.
