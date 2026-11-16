# Build a grouped BOM from the BOM Manager, the project or the PCB

Builds a bill of materials grouped by parameter values and writes it as one `|Name=Value|...` row per BOM line. It
shows four ways to get BOM data: the BOM engine (`EDP.IBomManagerFactory`) with and without its own grouping, the
compiled project's flattened document with its current variant, and the components of a PCB.

## What it shows
- `EDP.Utils.GetBomManagerFactory`: the entry point of the BOM engine.
- `EDP.IBomManagerFactory.GetManagerForDocument`: the BOM of one schematic or PCB document.
- `EDP.IBomManager.GetOptions`, `EDP.IBomManagerOptions.GetState` / `EDP.IBomManagerOptions.RestoreState`: change
  the BOM options for one call and put them back.
- `EDP.IBomManagerOptions.AutoGroup`, `EDP.IBomManagerOptions.GroupOrder`, `EDP.IBomManagerOptions.UseCurrentVariant`,
  `EDP.IBomManagerOptions.IncludeNotFitted`, `EDP.IBomManagerOptions.IncludeAlternatives`,
  `EDP.IBomManagerOptions.IncludeDbParameters`, `EDP.IBomManagerOptions.IncludeDbParametersInVariations`,
  `EDP.IBomManagerOptions.IncludePcbParameters`: the BOM's view and content.
- `EDP.IBomManager.GetSchema`, `EDP.IBomSchema.GetComponents`, `EDP.IBomSchema.GetParameterDefinitions`: the rows
  and columns of the BOM.
- `EDP.IBomComponent.GetParameters`, `EDP.IBomParameterDictionary.TryGetParameter`,
  `EDP.IBomParameterDictionary.GetEnumerator`, `EDP.IBomParameter.StrValue`: the values of a row.
- `EDP.IProject.DM_DocumentFlattened`, `EDP.IProject.DM_CurrentProjectVariant`,
  `EDP.IProjectVariant.DM_FindComponentVariationByUniqueId`, `EDP.IComponentVariation.DM_VariationKind`,
  `EDP.IParameterVariation.DM_VariedValue`: a BOM from the project model, with variant data applied.
- `EDP.IComponent.DM_ComponentKind` / `PCB.IPCB_Component.ComponentKind` with `EDP.TComponentKind`: kinds left out of
  a BOM (graphical, mechanical, net tie and standard without BOM).
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eComponentObject` and `PCB.PCBConstant.ElectricalLayersSet`,
  `PCB.IPCB_PrimitiveParameters.GetParameterByIndex`: a BOM from the PCB components and their parameters.

## Run it
1. Open a project, for example a copy of the SpiritLevel-SL1 example project that ships with Altium Designer. For
   `Source=Schema` and `Source=BomManager` focus one of its schematic or PCB documents; for `Source=Project` focus
   any document of the project; for `Source=Pcb` make its `.PcbDoc` the active document.
2. **BomReport › Generate Bom** (runs `Source=Schema`), or
   `tools/ad.ps1 -Sample BomReport run GenerateBom "Source=Project"`
3. The result starts with `Source=<source>: <n> BOM lines, <m> designators, <k> columns from <document>.`, a few
   lines about the source, `BOM written to <path>`, then the first rows. A `Source=Project` row on SpiritLevel:
   `|Designator=C1,C2,C3|Quantity=3|AEMS Code=|Comment=100uF 16V|Component Kind=Standard|Description=Polar Capacitor|Footprint=CAPPR2-5X6.8|...`
   (columns in order of first appearance). `Source=Schema` and `Source=BomManager` rows start with the BOM engine's
   own columns (`|#=1|%CATALOGID%=...`).

| Command | Parameters | Result |
|---|---|---|
| `GenerateBom` | `Source=Schema` (default), `BomManager`, `Project` or `Pcb`; `GroupBy=Comment,Footprint` (default; used by `Schema`, `Project`, `Pcb`); `Out=<file or folder>` (default: `BomReport_<Source>_<document>.txt` in the temp folder); `Max=<n>` (rows returned, default 5, 0 = all); `Update=true` (BOM engine sources: call `UpdateSchema` before `GetSchema`) | Summary, the source's details, the output path and the first rows |

## How it works
Everything is in `BomReportCommands.cs` and `BomTable.cs`.

`Source=Schema` and `Source=BomManager` (`FromBomEngine`) take the focused document from `EDP.IWorkspace`, check that
it is a schematic or PCB document and get its manager with `GetManagerForDocument`. They save the options with
`GetState`, set auto-grouping, the current variant, no not-fitted parts, alternatives included and no database or
PCB parameters, and restore the options in `finally`. `Schema` also sets `GroupOrder` to
`Comment=True|Footprint=True` (built from `GroupBy`) and reports the value read back. It reads the columns with
`GetParameterDefinitions`, then each row with `GetParameters`. `Schema` splits the row's `Designator` value at commas,
sorts the designators and counts them for `Quantity`, and takes the other columns from the row. `BomManager` writes
every column as the engine gives it, `Designator` and `Quantity` included.

`Source=Project` (`FromProject`) compiles the focused project if `DM_NeedsCompile` says so, then walks the components
of `DM_DocumentFlattened`. It skips the kinds that are not in a BOM and the components whose variation in the current
variant is `eVariation_NotFitted`. Varied parameter values come first; the component's own parameters are added for
components without a variation or with a variation of kind `eVariation_None`.

`Source=Pcb` (`FromPcb`) iterates the components on the electrical layers of the current board, skips the same
kinds, and reads the comment, description, design item ID, source library, height, footprint, footprint library and
footprint description, then the component's PCB parameters through `IPCB_PrimitiveParameters`. The iterator is
destroyed in `finally`.

`BomTable.Group` puts the components of the Project and Pcb sources into lines keyed by their `GroupBy` values, in
order of first appearance. Columns are `Designator`, `Quantity` and every parameter name in order of appearance.

## Notes
- Nothing in the design is changed and nothing is saved: `Source=Project` may compile the project (in memory),
  and the BOM options of the document are changed for the call and restored.
- The `GroupOrder` format `Comment=True|Footprint=True` is not documented. The value is read back verbatim, but `GetSchema` returned the same rows for `Comment=True`,
  `Comment=True|Footprint=True` and no `GroupOrder` at all; one row listed two footprints
  (`CR2012-0805, CC2012-0805`). So `Source=Schema` shows the engine's own grouping, not one row per `GroupBy`
  combination; `Project` and `Pcb` group by `GroupBy` themselves.
- `GetManagerForDocument` works on a schematic and on a PCB document: a sheet gives the BOM
  of that sheet only, the PcbDoc that of the whole board. The manager returned by `GetManagerForProject` fails in
  `GetOptions`, so this sample uses the document's manager only.
- Whether `GetSchema` reflects options set just before it without `UpdateSchema` is not documented. By default the
  sample does not call `UpdateSchema`; `Update=true` does, so both can be compared. Both gave the same rows.
- `Source=Pcb` takes the footprint name from `IPCB_Component.Pattern`, not from
  `FootprintConfiguratorName`, which was empty for every component of the SpiritLevel board, so different footprints
  would be grouped together.
- `Source=Pcb` does not apply variants: it only skips by component kind. Components on
  non-electrical layers are not iterated. `Height` is written in mils (internal units converted with
  `EDP.Utils.CoordToMils`).
- `Source=Project`: an alternate part (`eVariation_Alternate`) gets only its varied parameters, so
  columns it does not vary are empty.
- A parameter name that repeats (for example a PCB parameter named `Comment`) keeps the first value: the component
  property for PCB, the varied value for Project.
- Designators are sorted naturally (`R2` before `R10`).
- The output file can be large, since every row lists every column. The result returns only `Max` rows.
- Only schematic and PCB documents are accepted for the BOM engine sources, although the BOM engine also handles BomDoc
  documents.
