# Read 3D camera and board Z range, set body opacity

Reads the 3D state of the current board (its Z range, the camera of its main graphical view, one value of the
board's exported parameter string), writes the camera, and sets the 3D opacity of the first component body. It
shows the 3D camera accessors, how to pick a value out of a `Key=Value|...` parameter string, and how to change a
component body.

## What it shows
- `PCB.IPCB_Board.GetState_ZTopBottom`: the top and bottom Z of the board, returned through two `out float`s.
- `PCB.IPCB_Board.MainGraphicalView` with `PCB.IPCB_GraphicalView.GetState_Camera`: the look-at point, orientation
  quaternion and zoom of the board's main view.
- `PCB.IPCB_GraphicalView.Camera`: a write-only indexed property. In C# it is the method `SetState_Camera` with the
  seven floats as index arguments and the zoom as the value.
- `PCB.IPCB_Primitive.Export_ToParameters` with `DXP.Utils.GetParamterValue`: export the board's settings as a
  parameter string and read one entry (`ROUTETOOLPATHLAYER`). The helper's name is misspelt in the SDK.
- `PCB.IPCB_Board.RouteToolPathLayer` and `PCB.IPCB_Board.LayerName`: the same setting through the typed property.
- `PCB.TObjectId.eComponentBodyObject` and `PCB.IPCB_ComponentBody.BodyOpacity3D`: find a component body with a board
  iterator and change its opacity (0 transparent, 1 opaque).
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify`: the
  bracket for changing a PCB object.

## Run it
1. Open a `.PcbDoc` with 3D component bodies (for example the board of the SpiritLevel-SL1 example project that
   ships with Altium Designer) and make it the active document. Switch to the 3D view (key 3) to see camera and
   opacity changes.
2. **ComponentBodyOpacity › Board Info 3D**, or `tools/ad.ps1 -Sample ComponentBodyOpacity run BoardInfo3D`
3. `tools/ad.ps1 -Sample ComponentBodyOpacity run SetBodyOpacity "Opacity=0.3"`: the command reports
   `BodyOpacity3D` before and after; in a 3D view the first body should become see-through. Run it again without
   parameters to make it opaque.

| Command | Parameters | Result |
|---|---|---|
| `BoardInfo3D` | none | Z range, camera, `ROUTETOOLPATHLAYER` value from the parameter string, the typed property, the string length |
| `SetCamera` | `LookAtX`, `LookAtY`, `LookAtZ`, `QuatX`, `QuatY`, `QuatZ`, `QuatW`, `Zoom` (each optional; missing = current value) | `Camera before:` and `Camera after:` lines |
| `SetBodyOpacity` | `Opacity=<0..1>` (default 1) | the body's identifier and owner, `BodyOpacity3D` before and after |

## How it works
`ComponentBodyOpacityCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`.

`BoardInfo3D` calls `GetState_ZTopBottom`, then `GetState_MainGraphicalView` and `GetState_Camera`. It exports the
board's parameters with `Export_ToParameters(ref string)` and looks up `ROUTETOOLPATHLAYER` with
`DXP.Utils.GetParamterValue`. It then reads `GetState_RouteToolPathLayer` and names the layer with `LayerName`.

`SetCamera` reads the current camera, replaces each value given as a parameter, writes the camera with
`SetState_Camera`, and reads it back. Numbers are printed with the round-trip format, so `BoardInfo3D` output can
be pasted into `SetCamera` parameters to restore a camera.

`SetBodyOpacity` iterates the board for `eComponentBodyObject` on all layers and takes the first body. It sets
`SetBodyOpacity3D` between `PreProcess`/`BeginModify` and `EndModify`/`PostProcess`, and reports the value read
back.

## Notes
- The opacity change is made in memory and marks the document modified. Save the document to keep it.
- The docs say an opacity outside 0 to 1 has no effect. The command passes such values through and does not clamp
  them. Such a value is silently ignored: the body keeps its previous opacity, with no error.
- "First body" is the iterator's order, not a designer's choice. The result names the body's identifier and
  its component. On the SpiritLevel-SL1 board the body's `Identifier` is empty, so the owning component's
  designator is the usable label.
- `GetState_ZTopBottom` returns values in internal coordinate units (10,000 per mil, inferred from the magnitudes),
  and the "top" value is the smaller one: on the SpiritLevel-SL1 board ZTop = -4000 and ZBottom = 652000.
- The camera can be read and written in a 2D view too (a 2D view reads back an identity quaternion and LookAt Z = 0).
  Writing it does not mark the document modified: it is view state. The zoom is kept in single precision, so a
  double does not come back bit-identical.
- `ROUTETOOLPATHLAYER` is not a key of the board's exported parameter string. The string is a list of `Key=Value`
  pairs separated by `|`; nested records inside it separate their own fields with a backtick and URL-encode the
  text. `GetParamterValue` returns false for a missing key, and the command then prints the text "Do Not Use".
