# Detect a full AD license via a technology set

Asks the licensing layer whether the `Schematic_Editor` technology set is supported and reports "AD License used."
or "No AD License used.". It shows how an extension reaches `DXP.IServerSecurity` through its own server module, and
how to read the list of technology sets the license reports.

## What it shows
- `DXP.IServerSecurity.IsTechnologySetSupported`: asks whether a named technology set is licensed in this session.
- `DXP.ServerModule`: the base class of every extension's server module. It implements `DXP.IServerSecurity`,
  so the extension's own module is the object to ask.
- `DXP.IClient.GetServerModuleByName`: finds the extension's own module from code that doesn't hold it. The name is
  `SampleInfo.Name`, which is the name the shim gives its `PluginServerModule` and the `EditorName` in the `.ins`.
- `DXP.IClient.GetTechnologySets`: the technology sets of the active license, as one string, plus a timestamp
  returned through a `ref` parameter.
- `DXP.GlobalVars.Client`: the entry point to the client.

## Run it
1. No document is needed. Altium Designer runs with whatever license is active.
2. **TechnologySetLicenseCheck › Check AD License**, or `tools/ad.ps1 -Sample TechnologySetLicenseCheck run CheckADLicense`
3. The result is `AD License used.` when the license supports `Schematic_Editor`, otherwise `No AD License used.`.
   **List Technology Sets** shows the set names the license reports.

| Command | Parameters | Result |
|---|---|---|
| `CheckADLicense` | `TechnologySet=<name>` (optional, default `Schematic_Editor`); `Server=<server module name>` (optional, default this extension; pass another module's name when the Impl is loaded by a different extension, or for example `Server=PCB`) | `AD License used.` / `No AD License used.`; for another set: `Technology set '<name>' is supported.` / `... is not supported.` |
| `ListTechnologySets` | none | `ValidAtTimestamp=<n>` and `TechnologySets=<raw string>` |

## How it works
`TechnologySetLicenseCheckCommands.cs` gets the client from `DXP.GlobalVars.Client`. `CheckADLicense` looks up the
extension's own module (or the one named by `Server`) with `GetServerModuleByName`. It casts the module to
`DXP.IServerSecurity` and calls `IsTechnologySetSupported` with the requested set. If the module is missing, or
doesn't implement the interface, the command fails with a `UserException` that names the module's type.
`ListTechnologySets` calls `GetTechnologySets` and returns the string and the timestamp unchanged.

## Notes
- The check is a proxy. `Schematic_Editor` being supported means the license includes schematic editing. It does
  not prove a particular edition.
- Valid technology set names are not listed in the SDK reference. `ListTechnologySets` shows what the running
  license reports. It is one comma-separated string of names (no spaces), for example
  `CAM_Editor`, `Schematic_Editor`, `Schematic_Viewer`. Names are case-sensitive (`schematic_editor` is not
  supported). The unit of `ValidAtTimestamp` is not documented, so the sample returns it unchanged.
- The module `GetServerModuleByName` returns casts to `DXP.IServerSecurity` with `as`, both
  for a .NET extension module and for the built-in `PCB` server. The compiler alone only shows that
  `DXP.ServerModule` implements the interface and `DXP.IServerModule` does not.
- The extension's own module (the default `Server`) exists only when the extension is installed and started.
  Otherwise `CheckADLicense` fails with `Server module '<name>' was not found`.
- Read-only: nothing in the workspace or the license changes.
