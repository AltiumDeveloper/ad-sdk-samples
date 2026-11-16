# Create a project variant with parameter variations

Creates a named project variant (or reuses it) and gives every component of the focused project a component
variation whose parameter variations override each parameter value. It teaches the find-or-add pattern for
`EDP.IProjectVariant`, `EDP.IComponentVariation` and `EDP.IParameterVariation`, bracketed by
`EDP.IProject.DM_BeginUpdate` / `DM_EndUpdate`.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`: the project the commands work on, queried again on every run.
- `EDP.IProject.DM_NeedsCompile` / `DM_Compile` / `DM_DocumentFlattened`: compile when needed, then read the
  components of the flattened design. `DM_Compile` returns a bool, which the sample reports.
- `EDP.IProject.DM_BeginUpdate` / `DM_EndUpdate`: bracket a bulk change of the variant data, closed in a `finally`.
- `EDP.IProject.DM_ProjectVariantCount` / `DM_ProjectVariants` / `DM_AddProjectVariant`, with
  `EDP.IProjectVariant.DM_SetName` / `DM_SetDescription`: find a variant by name, or add one.
- `EDP.IProjectVariant.DM_FindComponentVariationByUniqueId` / `DM_AddComponentVariation`: one variation per
  component, linked by the component's unique ID (`EDP.IPart.DM_UniqueId`) through
  `EDP.IComponentVariation.DM_SetUniqueId`.
- `EDP.IComponentVariation.DM_SetVariationKind` with `EDP.TVariationKind.eVariation_None`: the component stays
  fitted and unchanged; only parameters vary. The enum overload is used because an `int` overload exists too.
- `EDP.IComponentVariation.DM_FindParameterVariation` / `DM_AddParameterVariation`, with
  `EDP.IParameterVariation.DM_SetParameterName` / `DM_SetVariedValue`: one override per component parameter.
- `EDP.IProjectVariant.DM_VariationCount` / `DM_Variations` and `EDP.IComponentVariation.DM_VariationCount` /
  `DM_Variations`: read the variant back (`ListVariant`). On a project variant they count component variations,
  on a component variation parameter variations.

## Run it
1. Open a project with compiled schematic components (for example a copy of the SpiritLevel-SL1 example project
   that ships with Altium Designer) and select it, or one of its documents, in the Projects panel.
2. **ProjectVariantAdd › Add Variant**, or `tools/ad.ps1 -Sample ProjectVariantAdd run AddVariant "Name=Testing"`
3. The result names the variant (created or reused) and counts the component and parameter variations added or
   updated. `ListVariant` lists them; every varied value is the component's value followed by `_$`.

| Command | Parameters | Result |
|---|---|---|
| `AddVariant` | `Name=<variant>` (default `Testing`), `Suffix=<text>` (default `_$`) | `<project>: created variant '<name>'.` plus counts of component and parameter variations added / updated |
| `ListVariant` | `Name=<variant>` (default `Testing`), `Max=<rows>` (default 10) | the variant's component variations with kind and varied parameter values, or the project's variants if the name is unknown |

## How it works
`ProjectVariantAddCommands.cs` gets the focused project from the workspace and throws a `UserException` when
there is none. `AddVariant` compiles the project if `DM_NeedsCompile` says so and takes the flattened document.
Inside `DM_BeginUpdate` / `DM_EndUpdate` it looks the variant up by name and adds it if missing. Then, for each
component that is not inferred, it finds the component variation by unique ID or adds one. It sets the kind to
`eVariation_None` and the unique ID. For each component parameter it finds the parameter variation by name or
adds one, and sets the name and the varied value (value plus suffix). `ListVariant` walks `DM_Variations` on the
variant and on each component variation and prints designator, unique ID, kind and varied values.

## Notes
- The change is made in memory only and marks the `.PrjPcb` modified. Save the project to keep the variant; the
  command never saves.
- The varied value is always rebuilt from the component's own `DM_Value`, so running again with another suffix
  replaces the old override instead of stacking suffixes. Empty values and formulas get the suffix too: the
  suffix is illustrative.
- Variants are found by `DM_Name`, case-insensitively, so the command finds the variant it created even after the
  user edits its description.
- `DM_FindComponentVariationByUniqueId` and `DM_FindParameterVariation` return `null` for "not found" on the .NET
  proxy, and the flattened component's `DM_UniqueId` is the ID the variant matches on, so a second run updates the
  existing variations instead of duplicating them.
- The unique IDs of flattened components are hierarchical (`\<sheet symbol>\<component>`) and are stored
  unchanged by `DM_SetUniqueId`.
- `IComponentVariation.DM_PhysicalDesignator` reads back empty for variations created this way (the sample sets
  only the kind and the unique ID).
- `DM_NeedsCompile` can already be false right after a project is opened, with `DM_DocumentFlattened` holding all
  components, so the compile branch does not always run.
- Multi-channel designs: the flattened document can hold several instances of a sheet. How their unique IDs map
  to component variations is not known.
