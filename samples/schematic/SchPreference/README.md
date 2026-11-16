# Read and toggle a schematic preference

Reads a boolean option of the schematic editor, writes the negated value back, and asks the SCH server module to
reload its options. It shows how an extension reads and writes the options of another server, here
`ResetPartsDesignatorsOnPaste` in the `Schematic Preferences` section of the `SCH` server.

## What it shows
- `DXP.Utils.ServerOptionsReader` / `DXP.Utils.ServerOptionsWriter`: an options reader and writer for a server,
  by server name (`"SCH"`).
- `DXP.IOptionsReader.ReadBoolean`: reads an option of a section, with a default for an absent value.
  `DXP.IOptionsReader.ValueExists` and `DXP.IOptionsReader.SectionExists` tell an absent value from a stored one.
- `DXP.IOptionsWriter.WriteBoolean`: stores the new value.
- `DXP.IClient.GetServerModuleByName`: the running SCH server module, or null when it is not loaded.
- `DXP.IServerOptions.ReloadOptions`: the server module, cast to `IServerOptions`, reloads its options.

## Run it
1. Open any `.SchDoc` (for example `SL_Power.SchDoc` of the SpiritLevel-SL1 example project that ships with Altium
   Designer), so the SCH server is loaded.
2. **SchPreference › Read Sch Preference**, or `tools/ad.ps1 -Sample SchPreference run ReadSchPreference`
3. **SchPreference › Change Sch Preference**, or `tools/ad.ps1 -Sample SchPreference run ChangeSchPreference`.
   It returns the old and the new value. Run it a second time to restore your value.

| Command | Parameters | Result |
|---|---|---|
| `ReadSchPreference` | `Section=<section>`, `Key=<option>` (optional) | `SCH [Schematic Preferences] ResetPartsDesignatorsOnPaste = true` |
| `ChangeSchPreference` | `Section=<section>`, `Key=<option>` (optional) | `SCH [Schematic Preferences] ResetPartsDesignatorsOnPaste: true -> false (server 'SCH' reloaded its options).` |

Without parameters both commands use `Section=Schematic Preferences|Key=ResetPartsDesignatorsOnPaste`. An absent
value is reported as `true (absent, default)`.

## How it works
`SchPreferenceCommands.cs` gets a reader and a writer for the `SCH` server from `DXP.Utils`. `ChangeSchPreference`
reads the option with `ReadBoolean` (default `true`), writes the negated value with `WriteBoolean`, then looks up
the SCH server module with `GetServerModuleByName`. If the module is loaded and implements `IServerOptions`, it
calls `ReloadOptions`; otherwise the result says that nothing was reloaded. Finally it reads the value back through
a new reader and returns old value, new value and the reload outcome. `ReadSchPreference` only reads.

## Notes
- `ChangeSchPreference` changes a persistent user preference of Altium Designer. Run it twice to get your value back.
- Do not toggle a key that does not exist: the writer presumably creates it. `ReadSchPreference` reports an unknown
  key as "absent, default".
- The SDK reference does not list the names of the schematic options.
- The SCH server module implements `IServerOptions`, and a new reader sees the written value at once, without
  `DXP.IOptionsWriter.Flush`. The sample calls `ReloadOptions` so that the running editor reloads its copy of the
  options. It does not call `Flush`.
- The SDK docs' example for `DXP.IOptionsReader.ReadBoolean` gets the reader through
  `DXP.IClient.GetOptionsManager` instead of `DXP.Utils.ServerOptionsReader`. This sample uses
  `DXP.Utils.ServerOptionsReader`.
