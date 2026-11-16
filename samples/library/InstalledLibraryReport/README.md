# Report installed libraries, components and parameters

Lists every installed library of Altium Designer with its components and each component's parameters, writes the
report to a text file and returns a summary per library. It shows how `SCH.IIntegratedLibraryManager` addresses
libraries, components and parameters by library path and index, without library or component objects.

## What it shows
- `EDP.Utils.LoadIntegratedLibraryManager`: gets the integrated library manager. The type lives in namespace `SCH`,
  although `EDP.Utils` hands it out.
- `SCH.IIntegratedLibraryManager.InstalledLibraryCount` / `SCH.IIntegratedLibraryManager.InstalledLibraryPath`: the
  libraries of the Installed tab, by index from 0.
- `SCH.IIntegratedLibraryManager.GetComponentCount` / `SCH.IIntegratedLibraryManager.GetComponentName`: the components
  of one library, addressed by the library path and a component index.
- `SCH.IIntegratedLibraryManager.GetParameterCount`, `SCH.IIntegratedLibraryManager.GetParameterName`,
  `SCH.IIntegratedLibraryManager.GetParameterValue`: the parameters of one component, addressed by library path,
  component index and parameter index.
- `DXP.IClient.OpenDocument` with kind `Text` and `DXP.IClient.ShowDocument`: open the report file in AD.

## Run it
1. No document needs to be open. At least one library must be installed (the Installed tab of the
   Available Libraries dialog, as the reference calls it).
2. **InstalledLibraryReport › Installed Library Report**, or
   `tools/ad.ps1 -Sample InstalledLibraryReport run InstalledLibraryReport "Out=<folder>"`
3. The result starts with `Installed libraries: <n>, components: <c>, parameters listed: <p>`, then one line per
   library `[<i>] <path>: <k> components, <q> parameters`, then `Report written to <folder>\InstalledLibraries.txt`.
   The menu item also opens the report as a text document in AD.

| Command | Parameters | Result |
|---|---|---|
| `InstalledLibraryReport` | `Out=<folder or file>` (optional, default the temp folder; a folder gets `InstalledLibraries.txt`), `Open=true` (optional, menu only: open the report in AD; the menu item passes it), `MaxComponents=<n>` (optional, default 0 = all: components written per library) | Counts per library and the report path. The file has every library path, component name and `<parameter> = <value>` line |

## How it works
`InstalledLibraryReportCommands.cs`: the command gets the manager from `EDP.Utils.LoadIntegratedLibraryManager` and
loops over `InstalledLibraryCount` libraries, reading each path with `InstalledLibraryPath`. For each path it reads
`GetComponentCount`, then for each component index `GetParameterCount`, `GetComponentName` and, for each parameter
index, `GetParameterName` and `GetParameterValue`. Every call passes the library path again: the manager holds no
library or component objects. The lines are written with `File.WriteAllLines`. With `Open=true` from the menu, the
file is opened with `OpenDocument("Text", path)` and shown with `ShowDocument`. Progress is logged per library.

## Notes
- The report can be large: every parameter of every component of every installed library. `MaxComponents` limits
  the components written per library; the summary still gives the full component count.
- `Open=true` is ignored when the command runs from `tools/ad.ps1` (the result says so), because opening a document
  changes the workspace of the running AD.
- `Out` that is not an existing folder is used as a file name; its folder must exist (checked before the
  libraries are read, otherwise the command fails with a message and writes nothing).
- Component and parameter indexes are 0-based, and the library path is the one
  `InstalledLibraryPath` returns. Parameters include simulation model parameters, so a simulation component can have
  hundreds. An installed library whose file is missing is still listed and reports 0 components, without an error.
- If `EDP.Utils.LoadIntegratedLibraryManager` returns null, the command reports it as an error.
