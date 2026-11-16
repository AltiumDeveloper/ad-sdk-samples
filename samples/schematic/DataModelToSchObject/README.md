# Resolve compiled components and nets to schematic objects

Compiles a project and goes from its compiled data model (components, nets, lines) to the live schematic objects
behind them: it lists the vault components, and recolours the wires and buses of a named net. The SDK technique is
the bridge `EDP.IDMObject.DM_SchHandle` -> `EDP.IUnifiedDataModel.SupportsInterface` -> `SCH.ISch_BasicContainer`,
checked with `ObjectId` before the typed interface is used.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` / `EDP.IWorkspace.DM_Projects`: the project to work on (focused, or named by
  `Project=`). The virtual "Free Documents" project is refused.
- `EDP.IProject.DM_CompileEx`: compiles the project so its components, nets and lines exist; the command reports
  both results.
- `EDP.IProject.DM_DataModel` with `EDP.IUnifiedDataModel.BeginRead` / `EDP.IUnifiedDataModel.EndRead`: one read
  bracket around all access to the compiled model.
- `EDP.IProject.DM_PhysicalDocuments`, `EDP.IDocument.DM_DocumentKind` (`"SCH"`), `EDP.IDocument.DM_Components`,
  `EDP.IDocument.DM_Nets`, `EDP.INet.DM_NetName`, `EDP.INet.DM_Lines`: the compiled objects of each sheet.
- `EDP.IDMObject.DM_SchHandle` and `EDP.IUnifiedDataModel.SupportsInterface`: turn a compiled object's handle string
  into its schematic object (an `object` out parameter, used as `SCH.ISch_BasicContainer`).
- `SCH.ISch_BasicContainer.ObjectId` (`SCH.TObjectId.eSchComponent`, `SCH.TObjectId.eWire`, `SCH.TObjectId.eBus`):
  check the kind before using `SCH.ISch_Component` or `SCH.ISch_Wire`.
- `SCH.ISch_Component.VaultGUID`: non-empty for a component placed from a workspace (vault).
- `SCH.ISch_RobotManager.SendMessage` with `SCH.SCHConstant.SCHMBeginModify` / `SCH.SCHConstant.SCHMEndModify`,
  `SCH.ISch_GraphicalObject.Color` and `SCH.ISch_GraphicalObject.GraphicallyInvalidate`: change a wire or bus the
  editor's way. `SCH.ISch_Bus` derives from `SCH.ISch_Wire`, so one code path sets both.

## Run it
1. Open a schematic project (for example the SpiritLevel-SL1 example that ships with Altium Designer), **open all
   its schematic sheets** in editors, and focus one of them. Handles resolve only for sheets that are open.
2. **DataModelToSchObject › List Vault Components**, or
   `tools/ad.ps1 -Sample DataModelToSchObject run ListVaultComponents "All=true"`
3. The first line counts the schematic documents, component handles, resolved handles, `ISch_Component` objects and
   vault components; then one line per sheet, and one line per vault component (with `All=true`, per component).
4. **DataModelToSchObject › Recolor Net Objects** (menu default `Net=LEDS[7..0]`), or
   `tools/ad.ps1 -Sample DataModelToSchObject run RecolorNetObjects "Net=LEDS[7..0]|Color=0x00FF00"`
5. The net's wires and buses turn the given colour (default red). The result lists each object with
   `Color <previous> -> <new>`.

| Command | Parameters | Result |
|---|---|---|
| `ListVaultComponents` | `Project=<file name or path>` (optional, default the focused project), `All=true` (optional: also list components without a VaultGUID) | Summary line with counts, then per sheet `<file>: <n> component(s)` and `  <designator>: vault component, VaultGUID=<guid>, LibReference=<ref>` |
| `RecolorNetObjects` | `Net=<net name>` (required), `Color=<BGR>` (decimal, `0x…` or `#…`; default `0x0000FF`, red), `Project=` (as above) | Summary `net '<name>' found <k> time(s), … <w> wire(s) and <b> bus(es) set to <colour>`, then one line per object `<sheet>: wire|bus Color 0x… -> 0x…` |

## How it works
`DataModelToSchObjectCommands.cs`: `FindProject` gets the workspace, the focused (or named) project, and refuses
"Free Documents". `Compile` calls `DM_CompileEx(true, ref cancelled)` and reports both values. Both commands then
take the project's data model, call `BeginRead`, and walk `DM_PhysicalDocuments`, keeping the documents whose
`DM_DocumentKind` is `SCH`.

`Resolve` is the bridge: it passes `DM_SchHandle()` to `SupportsInterface`, uses the returned object as
`SCH.ISch_BasicContainer` and reads `GetState_ObjectId()`. It counts handles that do not resolve (false, null, or a
`COMException` on the first call).

`ListVaultComponents` resolves every `IComponent` of each sheet, casts objects of kind `eSchComponent` to
`SCH.ISch_Component`, and reads `GetState_VaultGUID()` and `GetState_LibReference()` inside the read bracket.

`RecolorNetObjects` matches nets by `DM_NetName` (case-insensitive) and resolves each `ILine` of the matches. It keeps
objects of kind `eWire` and `eBus`, and skips a line that resolves to an object already listed (same handle or same
object): two lines of one net can lead to the same bus. After `EndRead` it changes them sheet by sheet. For each sheet it calls
`DXP.IProcessControl.PreProcess`, and for each object `SCHMBeginModify`, `SetState_Color`, `SCHMEndModify` and
`GraphicallyInvalidate`, then `PostProcess`. When no net matches, it reports net names with the same prefix.

## Notes
- **Open the sheets.** `SupportsInterface` returns false for objects on sheets that are not open in an
  editor. Both commands then report "not resolved" and add a hint.
- Vault components need a design placed from a workspace. The installed examples have none, so
  `ListVaultComponents` finds no vault component there. `All=true` still shows that each handle resolves to a
  working `SCH.ISch_Component`.
- On SpiritLevel-SL1 the bus net appears in `DM_Nets` as one net named `LEDS[7..0]` on three sheets, its lines
  resolve to bus objects (`eBus`), and on `SL_LCD_SW_LED_2E.SchDoc` two of its lines resolve to the same bus. If no
  net matches, the command lists similar names.
- In a long-running AD session `SupportsInterface` can return false for every handle although all sheets are open;
  after restarting Altium Designer the same setup resolves all of them. The cause is not known. If every handle
  reports "not resolved" with the sheets open, restart AD.
- The colour change is made after `EndRead`, with the robot manager notifications. The objects change, and the
  sheets are marked modified and redrawn.
- `RecolorNetObjects` changes the documents in memory. Close them without saving to discard it, or run it again with
  `Color=<previous colour>` from its report. If the previous colours differ per object, each object has to be reset
  on its own.
- Not documented in the reference: the description of `SupportsInterface` and of the first argument of
  `DM_CompileEx`.
