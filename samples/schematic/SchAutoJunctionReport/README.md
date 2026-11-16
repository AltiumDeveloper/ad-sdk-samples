# List auto-placed junctions and their net names

Lists the junctions Altium Designer places automatically on the focused schematic sheet, with their location and
net name. It shows how to read a sheet's wire connection points and how to cross-reference a live wire with the
compiled netlist through its handle.

## What it shows
- `SCH.ISch_Sheet.WireConnections`: the sheet's wire connection points, as a `SCH.IConnectionsArray`
  (`ConnectionsCount`, `Connection[i]`, 0-based).
- `SCH.IConnection.ObjectsCount`, `SCH.IConnection.IsManualJunction`, `SCH.IConnection.Location`: a point with at
  least 3 objects and no manual junction is treated as an automatic junction.
- `SCH.ISch_Document.SchIterator_Create` with `SCH.ISch_Iterator.AddFilter_ObjectSet` (`SCH.TObjectId.eWire`): walk
  the sheet's wires; `SCH.ISch_Document.SchIterator_Destroy` releases the iterator.
- `SCH.ISch_Polygon.VerticesCount` / `SCH.ISch_Polygon.Vertex` (inherited by `SCH.ISch_Wire`): the wire's points,
  read 1..VerticesCount.
- `SCH.ISch_BasicContainer.Handle` and `EDP.IDMObject.DM_SchHandle` (on `EDP.ILine`): the same handle string on the
  live wire and on the netlist line, the bridge between schematic and compiled model.
- `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: compile only when the model is out of date.
- `EDP.IDocument.DM_NetCount` / `EDP.IDocument.DM_Nets`, `EDP.INet.DM_LineCount` / `EDP.INet.DM_Lines`,
  `EDP.INet.DM_NetName`: the nets of the sheet's logical document, read inside `EDP.IUnifiedDataModel.BeginRead` /
  `EDP.IUnifiedDataModel.EndRead`.
- `SCH.ISch_ServerInterface.GetSchDocumentByPath`: from the data-model document (`EDP.IWorkspace.DM_FocusedDocument`)
  to the live schematic document.

## Run it
1. Open a schematic project (for example the SpiritLevel-SL1 example project that ships with Altium Designer) and
   one of its sheets with T-connections, such as `SL_Power.SchDoc`, and make the sheet the active document.
2. **SchAutoJunctionReport › Get All Auto Junctions**, or
   `tools/ad.ps1 -Sample SchAutoJunctionReport run GetAllAutoJunctions`
3. The first line reads `<sheet>: <n> auto-placed junction(s), <k> with a net name (<c> wire connections: ...)`,
   followed by one line per junction, for example `X:12000000, Y:4000000 (1200 mil, 400 mil), NetName: VCC`. A
   sheet without automatic junctions returns `<sheet>: no auto-placed junctions (...)`.

| Command | Parameters | Result |
|---|---|---|
| `GetAllAutoJunctions` | `Threshold=<n>` (optional, default 3: minimum `ObjectsCount`), `Compile=false` (optional: don't compile an out-of-date project) | A summary line, then one `X:<x>, Y:<y> (<x> mil, <y> mil)[, NetName: <net>]` line per automatic junction. Errors when no project or schematic sheet is focused. |

## How it works
`SchAutoJunctionReportCommands.cs`: `GetAllAutoJunctions` gets the focused project and compiles it when
`DM_NeedsCompile` is true (unless `Compile=false`). It checks that the focused document is a schematic, gets the live
`SCH.ISch_Document` with `GetSchDocumentByPath`, checks `ObjectId` is `eSheet` and casts it to `SCH.ISch_Sheet`.

`CollectWires` iterates the sheet's wires once and keeps each wire's `Handle` and vertices.
`NetNamesByLineHandle` finds the sheet's logical document in the project (`DM_LogicalDocuments`, matched by path,
falling back to the focused document) and maps the `DM_SchHandle` of every line of every net to the net name.

The command then walks `WireConnections`. Points with fewer than `Threshold` objects or with a manual junction are
counted and skipped. For each remaining point, `FindWire` takes the first wire with a vertex at the location, or else the first wire with a segment through it, and the handle map gives the net name.

## Notes
- Coordinates are printed raw, with mils next to them (`EDP.Utils.CoordToMils`). `IConnection.Location` is in schematic internal units, 10000 per mil, the units of
  `EDP.Utils.MilsToCoord`.
- The reference doesn't describe what `ObjectsCount` counts, or when `IsManualJunction` is true.
  `ObjectsCount` counts the objects meeting at the point (every wire connection has at least 2, and 3 or more is
  what needs a junction).
- `ILine.DM_SchHandle` matches `ISch_BasicContainer.Handle` of the live wire, so the handle map finds the net of
  every wire on the sheet.
- `Vertex` is read 1-based. Indices 1..`VerticesCount` are the vertices, index 0 returns
  `MaxInt, MaxInt` and an index past the end returns `0, 0`, and neither throws, so an off-by-one here fails
  silently. The index base is not stated in the reference.
- Net names come from the compiled model, so the sheet must belong to the focused project. A wire whose handle is
  not in any net of the sheet's document is listed as `wire not in a compiled net of this sheet`.
- Read-only: compiling updates the project's in-memory model only. The command changes and saves nothing.
