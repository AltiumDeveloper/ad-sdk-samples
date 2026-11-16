# Read the workspace's variant display options

Reports the workspace-level variant display preferences: how schematic sheets and PCB documents show components
that a variant does not fit or alters. It shows how to get from the client to the workspace preferences and the
`EDP.IVariantOption` object.

## What it shows
- `DXP.IClient.GetDXPWorkspace`: the workspace, cast to `EDP.IWorkspace` for the `DM_*` methods.
- `EDP.IWorkspace.DM_Preferences`: the workspace preferences object (`EDP.IWorkspacePreferences`).
- `EDP.IWorkspacePreferences.GetVariantOption`: the variant display options (`EDP.IVariantOption`).
- `EDP.IVariantOption.DM_SCHUseText`: the schematic "Use Text" flag.
- `EDP.IVariantOption.DM_SCHUseGraphics` / `EDP.IVariantOption.DM_SCHGraphics`: schematic graphics flag and kind
  (`EDP.TVariantGraphics`: `eVariantGraphics_Cross`, `eVariantGraphics_MeshBox`).
- `EDP.IVariantOption.DM_PCBUseText` / `EDP.IVariantOption.DM_PCBUseGraphics` / `EDP.IVariantOption.DM_PCBGraphics`:
  the same for PCB documents.
- The other `EDP.IVariantOption` getters (text, font, colour and parameter options), all read-only in the .NET API.
- `using EDP;`: `DM_Preferences`, `GetVariantOption` and the typed getters (`DM_SCHGraphics`, the colour getters) are
  extension methods of that namespace.

## Run it
1. No document is needed. Altium Designer must be running with its workspace.
2. **VariantDisplayOptions › Get Variant Options** (in the no-document, schematic and PCB menus), or
   `tools/ad.ps1 -Sample VariantDisplayOptions run GetVariantOptions`
3. The first line is `Schematic Drawing Options : Use Text = True` (or `False`), followed by a `Schematic:` and a
   `PCB:` block with one `    <Name> = <value>` line per getter, for example `    Graphics = eVariantGraphics_Cross`.

| Command | Parameters | Result |
|---|---|---|
| `GetVariantOptions` | none | The schematic "Use Text" line, then all schematic and PCB variant display options as text |

## How it works
`VariantDisplayOptionsCommands.cs`: `CurrentVariantOption` gets `DXP.GlobalVars.Client`, calls `GetDXPWorkspace` and
casts the result to `EDP.IWorkspace`, then calls `DM_Preferences` and `GetVariantOption`. Each step is checked for
null and raises a `UserException`. `GetVariantOptions` reads every `DM_*` getter of `EDP.IVariantOption` and adds one
line per value. Each getter is read separately: one that throws is reported in its own line as `<error: ...>`, and
the other values are still listed. Strings are quoted, enums are printed by name, colours as decimal and hex.

## Notes
- Read-only. `EDP.IVariantOption` has no setters in the .NET API, so these values cannot be set through this
  interface.
- Values are printed raw. The reference doesn't give the units of `DM_SCHTextSize`, `DM_SCHParamSize`,
  `DM_PCBTextHeight`, `DM_PCBTextStrokeWidth` and `DM_PCBTextTTInvertedBorder`, or the byte order of the colour
  values.
- Every getter returns a value. The text getters `DM_SCHText`, `DM_SCHParamSuffix` and
  `DM_PCBText` return null when unset (shown as `(null)`).
