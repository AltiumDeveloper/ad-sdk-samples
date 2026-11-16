# List the tables and component keys of a DbLib

Gets a database library (`.DbLib`) from the integrated library manager and lists its tables. With `Keys=true` it
also reads each table's component keys, which AD returns by filling a `DXP.IStrings` list that the caller supplies.

## What it shows
- `EDP.Utils.LoadIntegratedLibraryManager`: the integrated library manager (type `SCH.IIntegratedLibraryManager`).
- `SCH.IIntegratedLibraryManager.AvailableLibraryCount`, `AvailableLibraryType` and `AvailableLibraryPath`: find
  the database libraries (`SCH.TLibraryType.eLibDatabase`) among the available libraries when no path is given.
- `SCH.IIntegratedLibraryManager.GetAvailableDBLibDocAtPath`: the `EDP.IDatabaseLibDocument` for a `.DbLib` path.
  The sample checks the result for null.
- `EDP.IDatabaseLibDocument.GetTableCount`, `GetTableNameAt` and `TableEnabled`: the tables of the library,
  indexed from 0.
- `EDP.IDatabaseLibDocument.GetAllComponentKeys`: fills a caller-supplied `DXP.IStrings` with the table's
  component keys. The sample passes a managed `DXP.IStrings` implementation (see Notes).

## Run it
1. The extension must be deployed with `tools/deploy.ps1 -Sample DbLibComponentKeys` and Altium Designer restarted.
2. Have a `.DbLib` whose database AD can reach, installed in Available Libraries.
3. **DbLibComponentKeys › List Component Keys** (all available DbLibs, tables only), or
   `tools/ad.ps1 -Sample DbLibComponentKeys run ListComponentKeys "DbLib=<path to .DbLib>|Keys=true"`
4. The result has one line per DbLib (`<path>: <n> table(s)`) and one line per table
   (`[<index>] <table name>`). With `Keys=true` each table line adds `<n> component key(s): <first keys>`.

| Command | Parameters | Result |
|---|---|---|
| `ListComponentKeys` | `DbLib=<path>` (optional, default: every available database library), `Keys=true` (optional, read the component keys), `Max=<n>` (optional, keys listed per table, default 5, 0 = all) | Tables per DbLib, with key counts and the first keys when `Keys=true` |

## How it works
`DbLibComponentKeysCommands.cs` loads the integrated library manager with `EDP.Utils.LoadIntegratedLibraryManager`.
With `DbLib` it checks that the file exists and uses its full path. Without it, it walks the available libraries
and keeps those whose `AvailableLibraryType` is `eLibDatabase`. For each path, `GetAvailableDBLibDocAtPath`
returns the `EDP.IDatabaseLibDocument`. A null result is reported on that library's line. Then the command loops
over `GetTableCount` and reports `GetTableNameAt` for each table, marking disabled tables (`TableEnabled`). With
`Keys=true`, it creates a `StringList` (the sample's managed `DXP.IStrings`) per table, passes it to
`GetAllComponentKeys`, and reads the count and the first `Max` keys back with `GetCount` and `GetItem`. A
`COMException` from that call is reported on the table's line and does not stop the command.

## Notes
- **Passing a managed `DXP.IStrings` to AD.** The .NET SDK has no type or factory that creates a `DXP.IStrings`.
  `DXP.IStrings` is declared as a dispatch interface (DispIds 660 to 663), and the typed `GetAllComponentKeys` helper
  (`EDP.IDatabaseLibDocumentHelper`) passes the list on as a plain `object`. Whether AD can call a managed object
  through its COM-callable wrapper is not documented. Reading keys is therefore off unless you pass `Keys=true`. If AD
  rejects the managed list, expect a `COMException` (reported on the table's line) or an AD "Member not found"
  dialog.
- `GetAvailableDBLibDocAtPath` returns **null** for a `.DbLib` that exists on disk but
  is not installed in Available Libraries. It looks the path up among the available libraries; it does not load
  the file, does not try to reach the database and raises no error. The sample reports the null result on that
  library's line rather than failing.
- Pass `DbLib` as an absolute path. A relative path resolves against AD's working directory.
- The command only reads. It does not call `EDP.IDatabaseLibDocument.DisposeIfNotShowing`, because what it
  does to a library the manager holds is not documented.
