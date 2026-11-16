# Read schematic grid preferences via IOptionsReader

Reads four grid settings of the schematic editor (grid colour, grid style, imperial and metric grid presets) from
the stored preferences and returns them as one line of text. It shows how to read a server's preferences through
the client's options manager, and how to tell a stored value from a default.

## What it shows
- `DXP.IClient.GetOptionsManager`: the client's options manager. In .NET it is an extension method
  (`using DXP;`), not an `OptionsManager` property.
- `DXP.IOptionsManager.GetOptionsReader`: a reader for one server's options, here `SCH`. The second argument
  (`argOldSettingsPath`) is passed empty.
- `DXP.IOptionsReader.SectionExists`: checks that the section (`Schematic Preferences`) exists before reading it.
- `DXP.IOptionsReader.ReadInteger`: reads a value as an integer and returns the default you pass when the value
  is missing.
- `DXP.IOptionsReader.ValueExists`: tells a stored value apart from the default `ReadInteger` returned.

## Run it
1. No document is needed. Any running Altium Designer will do.
2. **SchGridOptionsRead › Read Options**, or `tools/ad.ps1 -Sample SchGridOptionsRead run ReadOptions`
3. The result is one line, for example
   `SCH / Schematic Preferences: Grid Color = <n>; Grid Style = <n>; ImperialGridPresets = <n>; MetricGridPresets = <n>`.
   A value that is not stored is marked `(not stored, default)`.

| Command | Parameters | Result |
|---|---|---|
| `ReadOptions` | `Server=<name>` (default `SCH`), `Section=<name>` (default `Schematic Preferences`), both optional | `<server> / <section>: Grid Color = <n>; Grid Style = <n>; ImperialGridPresets = <n>; MetricGridPresets = <n>` |

## How it works
`SchGridOptionsReadCommands.cs` reads the `Server` and `Section` parameters. It gets the options manager from
`DXP.GlobalVars.Client` and asks it for the server's reader with `GetOptionsReader(server, "")`. If there is no
reader, or the section doesn't exist, the command fails with a clear message. For each of the four value names it
calls `ValueExists`, then `ReadInteger` with default 0, and joins the results into the returned line.

## Notes
- The command only reads. It changes no preferences.
- The section and value names (`Schematic Preferences`, `Grid Color`, `Grid Style`, `ImperialGridPresets`,
  `MetricGridPresets`) are not listed in the SDK reference; Altium Designer stores all four under those names for
  server `SCH`.
- `GetOptionsReader` returns a non-null reader for an unknown server name too, and that reader is empty. An
  unknown server is therefore not reported by `GetOptionsReader`: check `SectionExists` (as this sample does).
- What the integers mean (colour encoding, style codes, preset indices) is not documented. The sample returns the
  raw values. `Grid Color` can come back as 15462640 = 0xEBF0F0, which reads as a Windows `COLORREF`
  (0x00BBGGRR, RGB(240, 240, 235)), but that encoding is not confirmed.
- What `argOldSettingsPath` is for is not documented.
