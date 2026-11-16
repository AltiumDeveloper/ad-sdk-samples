# List wire vertex coordinates of a named net

Compiles the focused project, finds a net by name in the flattened (netlist) document and lists the vertices of every
schematic wire of that net in millimetres. It shows how to go from a compiled netlist line back to the live
`SCH.ISch_Wire` on the sheet through the project's unified data model.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` and `EDP.IProject.DM_Compile`: nets exist only in the compiled model, so
  compile first. `DM_Compile` returns `false` on failure.
- `EDP.IProject.DM_DocumentFlattened`: the flat netlist of the whole (possibly hierarchical) project.
- `EDP.IDocument.DM_NetCount` / `EDP.IDocument.DM_Nets`, `EDP.INet.DM_NetName`: find the net by name.
- `EDP.INet.DM_LineCount` / `EDP.INet.DM_Lines`: the net's lines (`EDP.ILine`).
- `EDP.IDMObject.DM_SchHandle` with `EDP.IUnifiedDataModel.SupportsInterface`: resolve a line to the schematic
  object, between `EDP.IUnifiedDataModel.BeginRead` and `EDP.IUnifiedDataModel.EndRead`.
- `SCH.ISch_BasicContainer.ObjectId` = `SCH.TObjectId.eWire`: the kind check that tells a wire apart. A cast alone
  proves nothing, because SDK interfaces are dispatch interfaces.
- `SCH.ISch_Polygon.VerticesCount` / `SCH.ISch_Polygon.Vertex` (inherited by `SCH.ISch_Wire`): the wire's points.
- `EDP.Utils.CoordToMMs`: internal units to millimetres.

## Run it
1. Open a schematic project and **all** of its sheets (a line resolves only if its sheet is open in an editor), and
   make one sheet the active document. The SpiritLevel-SL1 example project that ships with Altium Designer works.
2. **NetWireVertices › Get Wire Vertices** lists the nets that have lines. Then
   `tools/ad.ps1 -Sample NetWireVertices run GetWireVertices "Net=<name>"` lists the wires of one net.
3. The result starts with `<project>.PrjPcb: net '<name>' matched 1 time(s), <l> line(s), <w> wire(s) with <v>
   vertices, ...`, then one `WireN (<sheet>): <c> vertices, line box (...) - (...)` line per wire and one
   `[k] X = 12.7mm (5000000) Y = ...` line per vertex.

| Command | Parameters | Result |
|---|---|---|
| `GetWireVertices` | none | `<project>: <n> nets with lines. ... Nets: <name> (<k> line(s)), ...` (first 40) |
| `GetWireVertices` | `Net=<name>` (case-insensitive), `Out=<file or folder>` (optional), `Open=true` (optional: write the list, to a temporary file when `Out` is not given, and open it in AD), `Base=1` (default) or `Base=0` (vertex index base, see Notes) | The wire and vertex list; with `Out`/`Open` also `Report written to <path>`. An unknown net is an error. |

## How it works
`NetWireVerticesCommands.cs`: `GetWireVertices` gets the workspace from `DXP.GlobalVars.Client.GetDXPWorkspace()`,
the focused project, compiles it, and takes its data model. Inside one `BeginRead`/`EndRead` bracket it walks
`DM_Nets` of `DM_DocumentFlattened`. Without `Net` it only collects the names of nets with lines. With `Net`, every
net whose `DM_NetName` matches (a net can appear more than once) is listed. `ReportLine` passes each line's
`DM_SchHandle` to `SupportsInterface`, checks `GetState_ObjectId()` on the returned object as an
`ISch_BasicContainer`, and for a wire reads `GetState_VerticesCount()` and `GetState_Vertex(k)`. Lines that don't
resolve, or resolve to something other than a wire, are listed and counted. `WriteReport` handles `Out`/`Open`.

## Notes
- Read-only: compiling refreshes the compiled model but changes no document.
- **`Vertex` is 1-based**: valid indices are `1..VerticesCount`, as in the docs' `SCH.ISch_Wire` example, not
  `0..VerticesCount-1`. The reference doesn't state the base. `Base=0` reads `0..VerticesCount-1`; it may read
  outside the vertex list.
- The object from `SupportsInterface` reports `eWire` and works as `SCH.ISch_Wire` in .NET, and a wire with several
  segments gives **one line per segment**, all resolving to the same wire (so the same vertex list is printed once
  per segment).
- Each wire line also prints `EDP.ILine.DM_LX`/`DM_LY`/`DM_HX`/`DM_HY`, the lower left and upper right coordinates,
  for comparison with the vertices. It is that segment's box, in the same internal units as the vertices.
- Wires are numbered by the line index in the net (`Wire<j+1>`), so lines that are not wires leave gaps in the
  numbering.
- Lines on sheets that are not open in an editor don't resolve. The summary then adds a hint.
- `CoordUnitToString(coord, eMetric)` of the scripting API has no .NET counterpart, so each value is printed as
  `<mm>mm (<raw internal value>)`.
