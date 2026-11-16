# Create a PcbLib with a footprint and extruded body

Creates and saves a new PCB library, adds a footprint holding one extruded 3D component body, makes it the
current footprint and zooms to it. It shows how to create a library document from code, register a new footprint,
and build a component body with an extruded model.

## What it shows
- `DXP.Utils.RunCommand` with `WorkspaceManager:OpenObject` and `ObjectKind=NewAnything|Kind=DefaultPcbLib`: create
  a new PCB library through the workspace manager's own process.
- `DXP.IClient.CurrentView` and `DXP.IServerDocument.FileName` / `DXP.IServerDocument.DoFileSave`: reach the new
  document, name it with `SetFileName` and save it with the kind `PCB Library File`.
- `PCB.IPCB_ServerInterface.GetCurrentPCBLibrary`: the library of the focused PcbLib document.
- `PCB.IPCB_ServerInterface.CreatePCBLibComp` and `PCB.IPCB_Library.RegisterComponent`: a new footprint, named
  with `SetState_Pattern`. `RegisterComponent` returns false when it refuses the footprint.
- `PCB.IPCB_ServerInterface.PCBObjectFactory` with `PCB.TObjectId.eComponentBodyObject`: a new component body.
- `PCB.IPCB_ComponentBody.ModelFactory_CreateExtruded` and `PCB.IPCB_Model.SetState`: an extruded model with a Z
  range, no rotation and no offset, assigned with `SetModel`.
- `PCB.IPCB_Region.GetMainContour` with `PCB.IPCB_Contour.Count` / `PointX` / `PointY`: the body's outline, the
  shape that is extruded.
- `PCB.IPCB_Group.AddPCBObject` inside `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess`: add the body to the
  footprint.
- `PCB.IPCB_Library.SetBoardToComponentByName`: make the new footprint the one shown in the editor.

## Run it
1. No document is needed. The command creates the library itself.
2. **CreatePcbLib › Create Pcb Lib**, or `tools/ad.ps1 -Sample CreatePcbLib run CreatePcbLib "Out=<folder>"`
3. A new `test.PcbLib` opens in the PcbLib editor with the footprint `TestComp`, marked modified. The returned text
   lists the footprint counts and the body read back: 1 body on Mechanical 1, 0.254 mm high, a square outline from
   (2.54, 2.54) mm to (5.08, 5.08) mm relative to the library origin.

| Command | Parameters | Result |
|---|---|---|
| `CreatePcbLib` | `Out=<folder>` (default: temp folder), `Name=<file name>` (default `test.PcbLib`), `Pattern=<footprint name>` (default `TestComp`), `Overwrite=true` (optional) | `Created '<path>' (DoFileSave: True) with footprint '<Pattern>'.` plus footprint counts and the body's properties |

## How it works
`CreatePcbLibCommands.cs` first checks the parameters and refuses to replace an existing file unless
`Overwrite=true`. It notes the focused document, then runs `WorkspaceManager:OpenObject` to create the library. It
checks that the newly focused document is a `PCBLIB` and not the one focused before. Then it names the document
and saves it.

It re-queries `PCB.GlobalVars.PCBServer` and `GetCurrentPCBLibrary`, and checks that the library's board file is
the new file. It creates the footprint with `CreatePCBLibComp`, names it and registers it. `CreateExtrudedBody`
creates the body with `PCBObjectFactory` and fills its main contour with a closed five-point square. Only then
does it create the extruded model with `ModelFactory_CreateExtruded(0, height, 0)`, because the reference says the
call extrudes the outline only when the outline has an area. It sets the model's rotation and offset with
`SetState`, then sets the overall height, the model, the standoff height, the 3D colour and the identifier. The
body is added to the footprint between `PreProcess` and `PostProcess`.

Finally it marks the document modified, shows the footprint with `SetBoardToComponentByName`, runs `PCB:Zoom`
`Action=All`, and returns a summary read back from the library and the body.

## Notes
- The file is saved before the footprint is added, so the file on disk does not contain it
  yet. Save the library to keep the footprint, or close it without saving.
- Units: the outline is a 100 mil square from (100, 100) to (200, 200) mil, 10 mil high (internal units, 10,000 per
  mil, written with `EDP.Utils.MilsToCoord`). That is 2.54 to 5.08 mm.
- The outline is filled before the extruded model is created, as the reference of `ModelFactory_CreateExtruded`
  implies.
- `RunCommand` returns after the new library is created and focused (it starts with one
  default footprint, `PCBCOMPONENT_1`), `DoFileSave("PCB Library File")` returns true and writes the file, and the
  new body is on Mechanical 1 (the code does not set a layer).
- Once the footprint is the current one, its primitives are on the library board, offset by the board origin
  (1270 mm in a new library). Points read back are absolute, so the summary prints the outline relative to
  `XOrigin`/`YOrigin`.
- The colour format of `BodyColor3D` (255 here) and of the `argColor` parameter of `ModelFactory_CreateExtruded` (0)
  is not documented; both read back unchanged.
- The command is inserted into the PCB menu (`MNPCBMenu`) and is also on the no-document menu.
