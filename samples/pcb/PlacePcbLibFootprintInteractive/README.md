# Start interactive placement of a PcbLib footprint

Registers a `.PcbLib` with the Integrated Library server, finds a footprint by name, and starts the PCB editor's own
interactive placement of that footprint with the process `IntegratedLibrary:PlaceComponent`. It shows how to get
library and footprint GUIDs from `SCH.ILibraryDataReader` and hand them to a built-in process.

## What it shows
- `DXP.IClient.GetServerModuleByName`: get the `IntegratedLibrary` server module, cast to `SCH.ILibraryServer` (the
  cast always compiles and succeeds in .NET; whether the object really is the library server shows only when
  `GetDataReader` is called).
- `SCH.ILibraryServer.GetDataReader`: the server's `SCH.ILibraryDataReader`.
- `SCH.ILibraryDataReader.RegisterLibrary` / `SCH.ILibraryDataReader.UnregisterLibrary`: register a library file to
  get an `SCH.ILibraryData` with a GUID, and unregister it by that GUID in a `finally`.
- `SCH.ILibraryData.First` / `SCH.ILibraryData.Next`: walk the library's entries (`SCH.ILibraryComponentData`),
  stateful, until `null`.
- `SCH.ILibraryComponentData.Name` / `SCH.ILibraryComponentData.GUID`: match the footprint and get its GUID.
- `DXP.Utils.RunCommand`: run `IntegratedLibrary:PlaceComponent` with `LibraryGUID=<lib>|ComponentGUID=<footprint>`.

## Run it
1. Open a `.PcbDoc` and make it the active document. Have a `.PcbLib` file with a known footprint, for example
   `Spirit_Level_Project.PcbLib` from the `Libraries` folder of the SpiritLevel-SL1 example project that ships with
   Altium Designer.
2. `tools/ad.ps1 -Sample PlacePcbLibFootprintInteractive run ListLibFootprints "PcbLibPath=<path to .PcbLib>"` to
   see the footprint names, then
   `tools/ad.ps1 -Sample PlacePcbLibFootprintInteractive run PlaceLibComponent "PcbLibPath=<path>|FootprintName=<name>"`.
   **PlacePcbLibFootprintInteractive › Place Lib Component** asks for the library with a file dialog, but it still
   needs `FootprintName`: without it the command lists the library's footprints and stops.
3. The PCB editor is in placement mode with the footprint on the cursor. Click to drop it, right-click or Escape to
   stop. The command returns `Ran IntegratedLibrary:PlaceComponent for footprint '<name>' ... RunCommand returned
   after <n> ms; components on the board: <before> -> <after>.`

| Command | Parameters | Result |
|---|---|---|
| `PlaceLibComponent` | `PcbLibPath=<full path to .PcbLib>` (required; a menu click asks with a file dialog), `FootprintName=<name>` (required, exact match) | Starts placement; returns the GUIDs passed, the time RunCommand took and the component count before and after. Fails with the footprint list if the name is missing or not found |
| `ListLibFootprints` | `PcbLibPath=<full path to .PcbLib>` (required; a menu click asks with a file dialog) | Read-only: the registered library's name, kind, GUID and file, the number of registered libraries before and after, and one line per footprint with its GUID |

## How it works
`PlacePcbLibFootprintInteractiveCommands.cs`: `PlaceLibComponent` checks that a PCB document is active, reads
`PcbLibPath` and checks that the file exists. `DataReader` gets the module with `GetServerModuleByName("IntegratedLibrary")`,
casts it to `SCH.ILibraryServer` and calls `GetDataReader`. `RegisterLibrary` returns the `SCH.ILibraryData`.
Inside `try`, `Find` walks `First`/`Next` until an entry's `Name` equals `FootprintName`. The command counts the
board's components, runs `DXP.Utils.RunCommand("IntegratedLibrary:PlaceComponent", "LibraryGUID=...|ComponentGUID=...")`,
times it and counts the components again. The `finally` calls `UnregisterLibrary` with the library GUID, also when
the footprint was not found. `ListLibFootprints` does the same registration and lists every entry instead.

## Notes
- `IntegratedLibrary:PlaceComponent` and its parameters `LibraryGUID` and `ComponentGUID` are not in the SDK
  reference. The process starts placement mode with the footprint on the cursor, and `RunCommand` **returns
  immediately** (about 330 ms) instead of waiting for the placement to end.
- The library is unregistered right after `RunCommand`. The active placement keeps working after
  `UnregisterLibrary`.
- For a `.PcbLib`: `RegisterLibrary` accepts the file without it being open in AD (and does not
  open it), returns an `ILibraryData` with `Kind` `eLibDatafile`, the file name as `Name` and a GUID that stays the
  same across register/unregister cycles; `ILibraryDataReader.Count` rises by one while registered and returns to
  its old value after `UnregisterLibrary`; every entry from `First`/`Next` is one footprint and `Next` ends with
  `null`.
- Starting the placement does not change the board. Dropping the footprint does; close the board without saving to
  discard the change.
- The footprint name is compared exactly (case-sensitive).
