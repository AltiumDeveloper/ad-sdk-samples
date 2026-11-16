# Add, read and remove parameters on a schematic wire

Adds a parameter to a schematic wire, lists the wire's parameters and removes them again. It shows how a
parameter becomes a child object of a wire, and how a context menu item is shown only while a wire is under the
right-click position.

## What it shows
- `SCH.ISch_ServerInterface.SchObjectFactory` with `SCH.TObjectId.eParameter` and
  `SCH.TObjectCreationMode.eCreate_Default`: creates a free `SCH.ISch_Parameter`.
- `SCH.ISch_Parameter.Name`, `SCH.ISch_BasicContainer.Text`, `SCH.ISch_ComplexText.IsHidden`,
  `SCH.ISch_Parameter.ShowName`, `SCH.ISch_GraphicalObject.Location`: set before the parameter is added.
- `SCH.ISch_BasicContainer.AddSchObject` / `SCH.ISch_BasicContainer.RemoveSchObject`: a wire is a container, so
  it can own parameters like a component does.
- `SCH.ISch_BasicContainer.SchIterator_Create` on the wire, with `SCH.ISch_Iterator.AddFilter_ObjectSet` and
  `SCH.ISch_Iterator.IterationDepth` = `SCH.TIterationDepth.eIterateFirstLevel`: only the wire's own children.
- `SCH.ISch_Document.PopupMenuHitTest` with `SCH.ISch_HitTest.HitTestCount` / `SCH.ISch_HitTest.HitObject`: the
  objects under the right-click position, used both by the menu item's GetState and by the command.
- `DXP.CommandLauncher.RegisterCommand` with a GetState procedure: the shim's `RunOnWire` process is visible and
  enabled only when a wire was hit, and the `.rcs` inserts its items into the schematic right-click menu
  (`PUSCHMENU`).
- `SCH.ISch_RobotManager.SendMessage` (`SCHMBeginModify`, `SCHMPrimitiveRegistration`, `SCHMEndModify`) and
  `DXP.IServerDocument.Modified`: announce the change and mark the sheet modified.
- `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: redraw the wire after its children changed.

## Run it
The extension adds items to the schematic right-click menu, so deploy it with
`tools/deploy.ps1 -Sample WireAddParameter` and restart Altium Designer before the first run.

1. Open any `.SchDoc` with wires, for example `SL_Power.SchDoc` of SpiritLevel-SL1, an example project that ships
   with Altium Designer.
2. Right-click a wire: **Wire: Add Parameter**, **Wire: Get Parameters**, **Wire: Remove Parameters** (shown only
   over a wire). Or select a wire and use **WireAddParameter › Add Parameter To Selected Wire** etc., or
   `tools/ad.ps1 -Sample WireAddParameter run AddParameterToWire "Name=Rev|Value=B"`.
3. Add returns `<sheet>: added parameter NewParamName=NewParamValue to <wire>. The wire now has 1 parameter(s):
   NewParamName=NewParamValue`; Get lists the pairs; Remove reports what it removed and what is left.

| Command | Parameters | Result |
|---|---|---|
| `AddParameterToWire` | `Name=<text>` (default `NewParamName`), `Value=<text>` (default `NewParamValue`), wire choice* | the added pair and the wire's parameters read back |
| `GetParameterFromWire` | wire choice* | `<n> parameter(s): Name=Value, ...` (read-only) |
| `RemoveParameterFromWire` | `Name=<text>` (optional: only that one; default all), wire choice* | the removed names and the parameters left |
| `ListWires` | `Max=<n>` (default 20) | each wire's index, location, selection and parameter count (helper) |
| `SelectWire` | `Index=<n>` (default 0) | selects wire n of `ListWires`, deselects the other wires (helper) |

\* Wire choice: `Wire=selected` (default) takes selected wire number `Index=<n>` (0-based, default 0);
`Wire=popup` takes the first wire under the last right-click position (what the context menu items pass).

## How it works
`WireAddParameter/WireContextMenu.cs` (shim) registers the extra process `RunOnWire` in the partial method
`RegisterViews`, which the framework calls once after `InitializeCommands`. Its GetState calls
`GetCurrentSchDocument`, `PopupMenuHitTest` and looks for an `eWire` hit object; any exception there shows
the item, so the command can report the error. `RunOnWire` itself forwards to the framework's `Run`.

`WireAddParameter.Impl/Commands/WireAddParameterCommands.cs` resolves the wire (`ResolveWire`: popup hit test or
the selected wires of a sheet iterator filtered to `eWire`). `AddParameterToWire` creates the parameter with
`SchObjectFactory`, sets name, text, `IsHidden=false`, `ShowName=true` and the wire's location, then adds it inside
`PreProcess`/`PostProcess` and `SCHMBeginModify` / `SCHMPrimitiveRegistration` / `SCHMEndModify`. It marks the
sheet's server document modified (`GetDocumentByPath` + `SetModified`) and redraws the wire. `ReadParameters`
walks the wire's first-level `eParameter` children. `RemoveParameterFromWire` collects them first, then removes
them with `RemoveSchObject`.

## Notes
- A wire accepts several parameters made with `SchObjectFactory` and `AddSchObject`, and a fresh first-level
  iterator on the wire finds them again.
- With no context menu open, `PopupMenuHitTest` returns a non-null hit test with `HitTestCount` = 0, so
  `Wire=popup` reports "No wire under the right-click position" instead of a stale hit.
- `ICommandLauncher.GetCommandState` cannot tell whether a process is registered: called from another extension's
  command with a null context, it returns all-false both for a registered process (even one whose GetState always
  enables it) and for an unknown one. It does not call the GetState procedure.
- `RunOnWire` shows its result in a modal message box (through the framework's `Run`); use the `Run` commands
  for scripted calls.
- Wire parameters may also need `EnableDraw` to be drawn; this sample does not set it (the SchWireParameterPanel
  sample does).
- `RemoveParameterFromWire` collects the parameters before removing them: calling `RemoveSchObject` and then
  `NextSchObject` on the same iterator changes the list the iterator walks.
- The parameter goes to the wire's `Location`; which point of the wire that is (its first vertex?) is not
  documented.
- Changes are in memory only: save the document to keep them.
