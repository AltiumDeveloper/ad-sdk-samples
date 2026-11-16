# Toggle a supplier import parameter's visibility

Toggles the Visible flag of a supplier import parameter (default `Category`) in the options of the
IntegratedLibrary server, then shows what the supplier manager reports for the same parameter. It teaches the
options store (`DXP.IOptionsReader` / `DXP.IOptionsWriter`) and the typed supplier import preferences of
`SCH.ISupplierManager`.

## What it shows
- `DXP.IClient.GetOptionsManager`: the entry point to the stored options of every server.
- `DXP.IOptionsManager.GetOptionsReader` / `DXP.IOptionsManager.GetOptionsWriter`: a reader and a writer for the
  options of one server, here `IntegratedLibrary`.
- `DXP.IOptionsReader.ReadString` / `DXP.IOptionsReader.ReadBoolean` / `DXP.IOptionsReader.ValueExists`: read the
  indexed key pairs `ParameterName<i>` / `Visible<i>` of the `Import Parameters` section.
- `DXP.IOptionsWriter.WriteBoolean` / `DXP.IOptionsWriter.WriteString` / `DXP.IOptionsWriter.Flush`: write the
  new value and commit it.
- `EDP.Utils.GetSupplierManager` with `SCH.ISupplierManager.FindImportParameterPreferences`,
  `SCH.ISupplierManager.GetImportParameterPreferencesCount` and `SCH.ISupplierManager.GetImportParameterPreferences`:
  the same preferences as typed objects.
- `SCH.ISupplierImportParameterPref`: `ParameterName`, `ImportName`, `Visible`, `Exclude`, `Suffix` (read with the
  `Get_` accessors).

## Run it
1. No document is needed. Any editor, or none, may be active.
2. **SupplierPreference › List Supplier Import Parameters**, or
   `tools/ad.ps1 -Sample SupplierPreference run ListSupplierImportParameters`, to see the current state.
3. **SupplierPreference › Supplier Preference**, or `tools/ad.ps1 -Sample SupplierPreference run SupplierPreference "Name=Mount"` (pick a name the list shows).
   The report gives the old and new Visible value and the supplier manager's value. Run it a second time to restore
   the original value.

| Command | Parameters | Result |
|---|---|---|
| `SupplierPreference` | `Name=<parameter>` (default `Category`) | `'<name>' found at index <i>: Visible<i> was <old>, written <new>.` (or `not found: added as ParameterName<i> ...`), then the value read back before and after `Flush`, and `FindImportParameterPreferences('<name>'): Visible = <v>` or `null` |
| `ListSupplierImportParameters` | `Raw=true` (optional) | The `ParameterName<i>` / `Visible<i>` entries of the options, then one line per `SCH.ISupplierImportParameterPref` from the supplier manager. `Raw=true` adds the section text returned by `ReadSection` |

## How it works
`SupplierPreferenceCommands.cs` gets the options manager from `DXP.GlobalVars.Client`, and a reader and a writer for
the `IntegratedLibrary` server. `Find` walks `ParameterName0`, `ParameterName1`, ... until it finds the name (exact,
case-sensitive match) or an empty value. That empty slot is where a missing parameter is added. A found
parameter gets its `Visible<i>` value inverted. A missing one is added with `Visible = true`. The command reads the
value back with the same reader, calls `Flush`, reads it again with a new reader, and asks
`FindImportParameterPreferences` for the supplier manager's view. `ListSupplierImportParameters` prints both views
side by side. Use it to check the toggle.

## Notes
- The command changes the user's persistent preferences. Run it twice to restore the original value. When the
  parameter was missing, the first run adds an entry, and the sample has no command to remove it again.
- The default list (33 entries) has **no `Category` entry**, so running the command with the default name appends
  one. Run `ListSupplierImportParameters` first and pass an existing name (for example `Name=Mount`).
- The options are stored for the server `IntegratedLibrary` in the section `Import Parameters`. Each entry has the
  keys `ParameterName<i>`, `ParameterAlias<i>`, `Visible<i>`, `Exclude<i>`, `Suffix<i>`, and the section ends with
  `DefaultsSaved`. `ReadSection` returns these key names one per line, without values.
- The same reader sees a written value before `Flush`. After `Flush`, `SCH.ISupplierManager` still reports the old
  `Visible` value in the same session. The API has no refresh method.
- A missing `Visible<i>` key is read as false. The report says when the key was absent.
- `GetImportParameterPreferences` is 0-based (0 .. Count - 1), in the same order as the options entries. It is
  marked `[Obsolete]` in the SDK with no replacement. The warning is suppressed for that one call.
- The `Find` loop stops after 1000 entries.
