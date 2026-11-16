# Import a STEP model into the current footprint

Loads a STEP file as the model of a new 3D component body and adds the body to the current footprint of the focused
PCB library. It shows how a body is created, given a model from a file, placed, and added to a library footprint.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary` and `PCB.IPCB_Library.CurrentComponent`: the footprint shown in
  the focused PCB library.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` with `PCB.TObjectId.eComponentBodyObject`: a new, unattached 3D body.
- `PCB.IPCB_ComponentBody.ModelFactory_FromFilename`: creates a model from a STEP file. The model is not attached
  to the body yet.
- `PCB.IPCB_Model.SetState`: rotation about X, Y and Z in degrees and the Z offset in internal units, set on the
  new model before it is assigned. `PCB.IPCB_Model.Embed` controls whether the model data is embedded.
- `PCB.IPCB_ComponentBody.Model`: assigning the model stores a copy and recalculates the body's outline and heights.
- `PCB.IPCB_ComponentBody.Identifier`: named `STEPModel_<file name>`.
- `PCB.IPCB_Primitive.MoveToXY` with `PCB.IPCB_Group.XLocation` / `YLocation`: places the body at the footprint's
  reference point plus an offset.
- `PCB.IPCB_Board.AddPCBObject`: on a library board this also puts the new primitive into the current footprint.
  `PCB.IPCB_Group.AddPCBObject` on the footprint is called only if the board add did not, because calling both lists
  the body twice.
- `PCB.IPCB_Group.GetPrimitiveCount` and `PCB.IPCB_Group.GetPrimitiveAt` (counted from 1): read the footprint's
  bodies back. `PCB.IPCB_Model.GetState` and `PCB.IPCB_Model.GetFileName` read the model's state.
- `DXP.IClient.GetDocumentByPath` and `DXP.IServerDocument.Modified`: mark the library modified.

## Run it
1. Open a `.PcbLib` (for example `Spirit_Level_Project.PcbLib` from the SpiritLevel-SL1 example project that ships
   with Altium Designer) and focus it, with the footprint to change shown. Have a STEP file ready.
   `TestData/TestBox.step` in this folder is a 2 x 2 x 1 mm box.
2. `tools/ad.ps1 -Sample ImportStepModelToFootprint run ImportStepModel "File=<path>\TestBox.step"`. The menu item
   **ImportStepModelToFootprint › Import Step Model** passes no file and only shows the error that asks for one.
3. The result says `Added 3D body 'STEPModel_TestBox.step' to footprint '<name>' (n bodies before, n+1 after).`,
   followed by the body's model file, embed flag, rotation, Z offset and extent, and that the library was marked
   modified. `ListBodies` lists the footprint's bodies again.

| Command | Parameters | Result |
|---|---|---|
| `ImportStepModel` | `File=<.step/.stp>` (required), `RotX`, `RotY`, `RotZ` (degrees, default 0), `X`, `Y` (mil offset from the reference point, default 0), `Z` (mil offset along Z, default 0), `Watch=true` (second argument of `ModelFactory_FromFilename`), `Embed=true\|false` (optional) | `Added 3D body ...`, the body's state, InBoard / InComponent, the modified document |
| `ListBodies` | none | `Footprint '<name>': <n> 3D bodies.` and one line per body |

## How it works
`ImportStepModelToFootprintCommands.cs`: `ImportStepModel` checks the file, then gets the current library, its
current footprint and the footprint's board. Between `PreProcess` and `PostProcess` it creates a body with
`PCBObjectFactory` and calls `ModelFactory_FromFilename`. If no model comes back, it destroys the body and stops.
Otherwise it calls `SetState` (and `SetEmbed` when `Embed` is given) on the model and assigns the model to the body.
Then it sets the identifier, moves the body to the footprint's `XLocation` / `YLocation` plus the offset, sets the
body's board and adds the body to the board. If the footprint's body count did not go up, it also adds the body to
the footprint; always doing both adds the body to the footprint twice. It sets `InBoard` and `InComponent` by hand
only if `AddPCBObject` left them false, and reports which case happened. After the board add, `InBoard` is already
true and `InComponent` is false, so the command sets `InComponent`.
Finally it finds the library document with `GetDocumentByPath(board file name)`, or uses the focused document, and
sets `Modified`. `ListBodies` walks the footprint's bodies with `GetPrimitiveAt` and prints each one with `Describe`.

## Notes
- Units: angles in degrees. `X`, `Y`, `Z` are in mils and converted with `EDP.Utils.MilsToCoord` (10,000 internal
  units per mil). The reference says `SetState` brings an angle outside 0 to 360 into -180 to 180.
- The library is changed in memory only. Save it to keep the body, or close it without saving.
- The second argument of `ModelFactory_FromFilename` does not embed the model: the .NET API names it
  `argWatchDirectory`, and the reference does not describe it. Embedding is `PCB.IPCB_Model.Embed`. With `True`,
  the model reports `Embed=True` and its file name is the full path.
- `ModelFactory_FromFilename` may return null or throw for an unreadable file; both are handled.
- The rotation turns the model about its own origin, which ends up at the body's location (`MoveToXY`). `Z` raises
  the model and adds the same amount to the overall height.
- The board file name of a library footprint is the `.PcbLib` path, so `GetDocumentByPath` finds the library.
- `TestData/TestBox.step` was written by hand for this sample (AP214, one closed shell of six planar faces).
  Altium Designer loads it as a 2 x 2 x 1 mm box.
- The model object from the factory is not destroyed after it is assigned. The reference says `Model` stores a copy,
  so it could be destroyed with `ModelFactory_Destroy`.
- The menu is placed in the PCB editor's menu (`MNPCBMenu`).
