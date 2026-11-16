# Assign STEP models to footprints by name match

Opens PCB libraries from code and gives each footprint a 3D body made from a STEP file whose name starts with the
footprint name, then saves the libraries. It teaches the batch-edit technique for libraries: open, walk the
footprints, replace their component bodies, save and close.

## What it shows
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument` / `DXP.IClient.CloseDocument`, `DXP.IClient.IsDocumentOpen`:
  open a `.PcbLib` from code, and leave alone a library the user already has open.
- `PCB.IPCB_Library.ComponentCount` / `PCB.IPCB_Library.GetComponent`: walk the footprints by zero-based index.
- `PCB.IPCB_LibComponent.Pattern`: the footprint name. `IPCB_LibComponent` has no `Name` in the .NET API.
- `PCB.IPCB_Group.GroupIterator_Create` with `PCB.IPCB_GroupIterator.AddFilter_ObjectSet` (`PCB.TObjectId.eComponentBodyObject`):
  collect a footprint's 3D bodies first, remove them afterwards with `PCB.IPCB_Group.RemovePCBObject`.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` and `PCB.IPCB_ComponentBody.ModelFactory_FromFilename`: a new body and
  a model loaded from a STEP file.
- `PCB.IPCB_Model.SetState` / `PCB.IPCB_Model.GetState`: rotation in degrees, Z offset in internal units.
- `PCB.IPCB_ComponentBody.Model`: assigning the model also recalculates the body's outline and heights.
- `PCB.IPCB_Primitive.MoveByXY` then `PCB.IPCB_Group.AddPCBObject`: move the new body from the board origin to the
  footprint reference point and add it to the footprint.
- `DXP.IServerDocument.DoFileSave`: save the library, checking the result.

## Run it
1. Copy the libraries to a work folder (they are saved in place) and close them in Altium Designer. Put STEP files
   whose names start with footprint names in a folder.
2. **PcbLibAssignStepModels › Assign Step Models** (only with the parameters below, the menu has none), or
   `tools/ad.ps1 -Sample PcbLibAssignStepModels run AssignStepModels "Libs=<work>\My.PcbLib|StepFolder=<work>\step"`
3. The result lists each footprint with the STEP file it got, or "no match". Check with
   `run ListBodies "Lib=<work>\My.PcbLib"`.

| Command | Parameters | Result |
|---|---|---|
| `AssignStepModels` | `Libs=<.PcbLib>[;<.PcbLib>...]` (required), `StepFolder=<folder>` (required), `RotX`/`RotY`/`RotZ=<degrees>` (default 0), `Z=<mil>` (default 0), `Subfolders` (search subfolders too), `KeepExisting` (keep the old bodies) | A summary line, then per library one line per footprint: matched file and prefix, bodies removed, model state read back; "saved" or "nothing changed, not saved". |
| `ListBodies` | `Lib=<.PcbLib>` (required) | Each footprint with its 3D bodies: identifier, model file, rotation, Z offset, extent from the reference point. |

## How it works
`PcbLibAssignStepModelsCommands.cs`. `AssignStepModels` checks its parameters and lists the `*.step` / `*.stp`
files of `StepFolder`, sorted by path. For each library it skips the file when it is already open
(`IsDocumentOpen`). Otherwise it opens it with `OpenDocument("PcbLib", ...)` and shows it, and takes the
`IPCB_Library` (the current library, checked against the file path, else `GetPCBLibraryByPath`). Between
`PreProcess` and `PostProcess` it walks `GetComponent(0..ComponentCount-1)`. For each footprint:

1. `FindStepFile` looks for a file name (without extension) that starts with the footprint name, ignoring case.
   With no match it tries the name shortened by one character, for five rounds in all. The first file in sorted
   order wins.
2. `CollectBodies` gathers the footprint's component bodies with a group iterator and destroys the iterator.
3. The footprint is not made the library's `CurrentComponent` (see Notes).
4. A body comes from `PCBObjectFactory(eComponentBodyObject, ...)` and its model from `ModelFactory_FromFilename(file, false)`.
   When the model can't be loaded, the body is destroyed and the footprint is left unchanged.
5. The old bodies are removed with `RemovePCBObject` (unless `KeepExisting`). The model gets `SetState(rotX, rotY, rotZ, dz)`
   and is read back with `GetState`, then assigned with `SetModel`. The body is moved by the footprint's `XLocation`/`YLocation` and added to the footprint.

A library with at least one change is saved with `DoFileSave("PcbLib")`, and every library opened here is closed.
`ListBodies` opens a library (or uses the open one), lists the bodies of each footprint, and closes it only if it
opened it.

## Notes
- The libraries are modified **and saved in place**. Use copies. Libraries that are open in Altium Designer are
  skipped, because `CloseDocument` would discard their unsaved edits.
- Units: angles in degrees, `Z` in mils (converted with `EDP.Utils.MilsToCoord`, as the reference gives
  `IPCB_Model.SetState`'s offset in internal units, 10,000 per mil). A bare 30 would be 0.003 mil.
- The match is a prefix match: `R0805.step` matches the footprint `R0805`, and the footprint `R0805_HD` matches it in
  its fourth round (name shortened to `R0805`). With several candidates the first in sorted path order wins.
- The second argument of `ModelFactory_FromFilename` (`argWatchDirectory`) is not described in the reference; the
  sample passes `false`.
- `ModelFactory_FromFilename` may return null or throw for an unreadable file; both are handled.
- Placement: a new body is at board coordinate 0,0, which in a PcbLib is 50000 mil away from the footprint reference
  point. The body is moved by the footprint's `XLocation`/`YLocation`, which puts the STEP file's origin on the
  reference point.
- `CurrentComponent`: a body removed with `RemovePCBObject` from the library's **current** footprint comes back after
  the save; from a footprint that is not current it is gone. Adding the body with the footprint's `AddPCBObject`
  alone (no board add) is saved. So the command never changes `CurrentComponent`. The footprint that is already
  current when the library opens may keep its old bodies.
- `IPCB_Board.RemovePCBObject` is not used: on a library board it removes footprints only (reference), and calling
  it in addition does not help.
- The model object from the factory is not destroyed after it is assigned. The reference says `Model` stores a
  copy, so `ModelFactory_Destroy` would be allowed.
- No undo: the libraries are saved and closed by the command.
