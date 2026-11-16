# List every bus and its member net names

Lists, for each schematic sheet of every open project, its buses with the net name of each bus wire. It shows how
to walk the data model from the workspace down to buses and their nets. Compiling the projects first is optional.

## What it shows
- `EDP.IWorkspace.DM_ProjectCount` / `EDP.IWorkspace.DM_Projects`: all projects open in the workspace.
- `EDP.IProject.DM_Compile`: recompiles the project's data model. Optional here (`Compile=false` skips it): AD keeps
  the model of an open project populated. The result is a bool, reported in the output.
- `EDP.IProject.DM_PhysicalDocumentCount` / `EDP.IProject.DM_PhysicalDocuments`: the project's source documents.
- `EDP.IDocument.DM_DocumentKind`: `"SCH"` for a schematic sheet.
- `EDP.IDocument.DM_FileName` / `EDP.IDocument.DM_FullPath`: the sheet's name and path.
- `EDP.IDocument.DM_BusCount` / `EDP.IDocument.DM_Buses`: the buses of a sheet.
- `EDP.IBus.DM_BusName`: the bus's name.
- `EDP.IBus.DM_WireCount` / `EDP.IBus.DM_Wires`: the bus's wires, typed as `EDP.INet`.
- `EDP.INet.DM_NetName`: the net name of each wire.

## Run it
1. Open a project whose schematic sheets have buses, for example the SpiritLevel-SL1 example project that ships with
   Altium Designer: three sheets each carry a bus reported as `LCD_DB`, `LEDS` and `SW` (labelled `LCD_DB[7..0]`,
   `LEDS[7..0]`, `SW[7..0]`), with 8 wires each, the nets `LCD_DB0`…`LCD_DB7`, `LEDS0`…`LEDS7` and `SW0`…`SW7`.
2. **SchBusNetReport › Get Bus Info**, or `tools/ad.ps1 -Sample SchBusNetReport run GetBusInfo`
3. The result starts with `<p> projects, <s> schematic documents, <b> buses, <w> bus wires.`, then per project a
   line `Project #<i>: <name> (compiled)`, per sheet its file name, full path and bus count, and per bus
   `Bus Name = <name>`, `Number of Wires for this Bus: <n>` and one `Wire Net Name = <net>` line per wire.

| Command | Parameters | Result |
|---|---|---|
| `GetBusInfo` | `Compile=true\|false` (default `true`), `Out=<file or folder>` (optional), `Open=true` (optional: write the report, to a temporary file when `Out` is not given, and open it in AD) | The report as text; with `Out`/`Open` also `Report written to <path>` |

## How it works
`SchBusNetReportCommands.cs`: `GetBusInfo` gets the workspace with `GetDXPWorkspace() as EDP.IWorkspace` and fails
when no project is open. For each project it calls `DM_Compile` (unless `Compile=false`) and records the result. It
then loops over the physical documents, keeps those whose `DM_DocumentKind` is `SCH`, and for each sheet loops over
`DM_Buses` and each bus's `DM_Wires`, writing the net names. Every data-model object is null-checked. A summary line
with the counts goes on top. `Out` writes the same lines to a file (`SchBusNetReport.txt` when `Out` is a folder);
`Open` opens that file as a text document.

## Notes
- Compiling changes only the in-memory data model. Nothing is saved.
- All open projects are listed, not only the focused one. `DM_Projects` also lists the `Free Documents` project
  and script projects.
- `DM_Wires(i)` is the *i*-th bus member net in ascending index order and `DM_WireCount`
  equals `DM_BusWidth`, even for a high-value-first bus such as `LEDS[7..0]`. `DM_Sections` is different: one net
  per bus section, named with the full bus name.
- `DM_BusName` returns the name without the range (`LEDS`), `DM_FullBusName` with it (`LEDS[7..0]`).
- The buses are in the data model as soon as the project is open: `Compile=false` gives the same bus data as the
  default run, so `DM_Compile` is not a precondition for reading them.
- What `DM_Compile` returning false means is not documented (it returns `true` for every open project, including
  `Free Documents`). The report shows it next to the project.
