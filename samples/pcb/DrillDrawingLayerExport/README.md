# Export drill drawing layer primitives to a text file

Writes every primitive on the Drill Drawing layer of the current PCB board to a text file, one parameter string per
line. It shows how to filter a board iterator on one layer and all object kinds, and how to serialise any primitive
with `PCB.IPCB_Primitive.Export_ToParameters`.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` / `PCB.IPCB_Board.BoardIterator_Destroy`: create the iterator, destroy it
  in a `finally`.
- `PCB.IPCB_BoardIterator.AddFilter_LayerSet` with `PCB.TV6_LayerSet` { `PCB.TV6_Layer.eV6_DrillDrawing` }: restrict
  the iterator to one layer. The .NET API has no V7 layer enumeration.
- `PCB.IPCB_BoardIterator.AddFilter_ObjectSet` with a `PCB.TObjectSet` of every `PCB.TObjectId` except `eNoObject`:
  an all-objects set, which `PCB.PCBConstant` does not provide.
- `PCB.IPCB_Primitive.Export_ToParameters`: serialise a primitive of any kind into a parameter string (`ref string`).
- `PCB.IPCB_Primitive.ViewableObjectID` with `PCB.TViewableObjectID.eViewableObject_Text`: the optional text-only
  filter, and the per-kind count in the result.
- Helper command: `PCB.IPCB_ServerInterface.PCBObjectFactory`, `PCB.IPCB_Text`, `PCB.IPCB_Board.AddPCBObject` place a
  text on the Drill Drawing layer.

## Run it
1. Open a `.PcbDoc` and make it the active document, for example the board of the SpiritLevel-SL1 example project
   that ships with Altium Designer. A board with a drill table (**Place › Drill Table**) is the typical use case. On
   any board, **DrillDrawingLayerExport › Add Test Text** places one known text on the layer.
2. **DrillDrawingLayerExport › Export Drill Drawing**, or
   `tools/ad.ps1 -Sample DrillDrawingLayerExport run ExportDrillDrawing "Out=<folder>"`
3. The result reads `<board>.PcbDoc: <n> primitives on the Drill Drawing layer written to <path>`, followed by
   `By kind: eViewableObject_Text 1, ...` (or `none`). The file holds one `Export_ToParameters` string per primitive.

| Command | Parameters | Result |
|---|---|---|
| `ExportDrillDrawing` | `Out=<folder or file>` (optional, default the temp folder; a folder gets `DrillDrawing_<board>.txt`), `TextOnly=true` (optional: text primitives only) | Primitive count, count per `TViewableObjectID`, file path; a note if any primitive returned an empty string |
| `AddTestText` | `Text=<string>` (default `DrillDrawingLayerExport_Test`) | `Placed text '<text>' on the Drill Drawing layer of <board>.` |

## How it works
`DrillDrawingLayerExportCommands.cs`: `ExportDrillDrawing` gets the current board and creates a board iterator with
three filters: an object set of all `TObjectId` values (`AllObjects`), a layer set holding only `eV6_DrillDrawing`,
and `eProcessAll`. For each primitive it reads `ViewableObjectID` (skipping non-text kinds when `TextOnly` is set),
calls `Export_ToParameters` with a fresh string, and keeps the line and a count per kind. The iterator is destroyed in
a `finally`, then the lines are written with `File.WriteAllLines` to the path from `Out`.

`AddTestText` creates an `IPCB_Text` with `PCBObjectFactory`, sets its location (board origin + 500 mil), layer
`eV6_DrillDrawing`, text and size, and adds it with `AddPCBObject` between `PreProcess` and `PostProcess`.

## Notes
- The V6 layer filter `eV6_DrillDrawing` selects the Drill Drawing layer objects: every primitive returned reports
  `LAYER=DRILLDRAWING`. `AddFilter_LayerSet_2` takes a `PCB.TLayerPartitionSet` in .NET and its behaviour is
  undocumented, so it is not used.
- Whether an iterator without `AddFilter_ObjectSet` returns all kinds is not documented; the sample lists every
  `TObjectId` explicitly.
- `Export_ToParameters` has no description in the reference. It returns `KEY=VALUE` pairs joined by `|`:
  `SELECTION`, `LAYER`, geometry with a `mil` suffix, angles in degrees in scientific notation, and the kind-specific
  properties, e.g. `TEXT=`/`WIDESTRING=` for text. For example
  `LAYER=DRILLDRAWING|...|LOCATION.X=2482.5148mil|RADIUS=33.3333mil|WIDTH=10mil|...` for an arc.
- A board with nothing on the layer writes an empty file and reports 0 primitives; this is not an error.
- `AddTestText` is a helper. It changes the board in memory; close the document without saving to discard it.
- The output folder must exist; the command reports an error otherwise.
