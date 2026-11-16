# Assign an alternate part to every component in a variant

Finds or creates a project variant and gives every placed component of the focused project an alternate part from
a library, then lists the variant's component variations to check the result. It shows how project variants,
component variations and their alternate library links fit together in the `EDP` project model.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`: the focused project, from `DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace`.
- `EDP.IProject.DM_NeedsCompile` / `EDP.IProject.DM_Compile`: compile before reading the flattened document.
- `EDP.IProject.DM_DocumentFlattened`: the compiled, flattened design; its `EDP.IDocument.DM_Components` are the
  components the variant refers to.
- `EDP.IProject.DM_ProjectVariantCount` / `EDP.IProject.DM_ProjectVariants` /
  `EDP.IProject.DM_AddProjectVariant`, `EDP.IProjectVariant.DM_SetName` / `DM_SetDescription`: walk the project's
  variants to find one (here by description), or create one.
- `EDP.IProject.DM_BeginUpdate` / `EDP.IProject.DM_EndUpdate`: bracket the bulk change.
- `EDP.IProjectVariant.DM_FindComponentVariationByUniqueId` / `EDP.IProjectVariant.DM_AddComponentVariation`:
  a component variation is keyed by the component's unique id (`EDP.IPart.DM_UniqueId`), so an existing one is
  updated instead of duplicated.
- `EDP.IComponentVariation.DM_AlternateLibraryLink` and `EDP.IComponentLibraryLink.DM_SetUseLibraryName`,
  `DM_SetLibraryIdentifier`, `DM_SetSourceLibraryName`, `DM_SetDesignItemID`: the alternate part is a library
  link (library name plus design item id).
- `EDP.IComponentVariation.DM_SetVariationKind` with `EDP.TVariationKind.eVariation_Alternate`: makes the
  variation use the alternate part.
- `EDP.IProjectVariant.DM_VariationCount` / `EDP.IProjectVariant.DM_Variations`: read the variant's component
  variations back (despite the names, they count and return component variations of this one variant), with
  `EDP.IComponentVariation.DM_VariationKind` and `EDP.IComponentVariation.DM_PhysicalDesignator`.
- `EDP.IDMObject.DM_IsInferredObject`: skips objects the compiler inferred rather than the user placed.

## Run it
1. Open a project with schematic sheets (for example a copy of SpiritLevel-SL1, an example project that ships with
   Altium Designer) and focus it in the Projects panel.
2. Optional: `tools/ad.ps1 -Sample ProjectVariantAlternatePart run CreateVariantComponent "DryRun=true"` to see
   what would change.
3. **ProjectVariantAlternatePart › Create Variant Component**, or
   `tools/ad.ps1 -Sample ProjectVariantAlternatePart run CreateVariantComponent "Variant=Test|Library=Miscellaneous Devices.IntLib|Item=Bridge2"`
4. **ProjectVariantAlternatePart › List Variant Alternates**: the variant now has one `eVariation_Alternate`
   variation per placed component, each pointing at `Bridge2 in Miscellaneous Devices.IntLib`.

| Command | Parameters | Result |
|---|---|---|
| `CreateVariantComponent` | `Variant=<description>` (default `Test`), `Library=<library>` (default `Miscellaneous Devices.IntLib`), `Item=<design item id>` (default `Bridge2`), `DryRun=true` (optional) | Variant created or reused, number of variations added and updated, components in the flattened document, inferred ones skipped |
| `ListVariantAlternates` | `Variant=<description>` (default `Test`), `Max=<n>` (default 20) | Variation count, a count per kind and alternate part, then up to `Max` rows `#i '<designator>' uid=<id> <kind> -> <item> in <library>`. Lists the project's variants when the named one does not exist |

## How it works
`ProjectVariantAlternatePartCommands.cs`: `FocusedProject` gets the workspace as `EDP.IWorkspace` and its focused
project, and reports a clear error when there is none. `CreateVariantComponent` compiles the project if
`DM_NeedsCompile` says so (a `false` result from `DM_Compile` is reported as a warning) and gets
`DM_DocumentFlattened`. `FindVariant` walks `DM_ProjectVariants` and matches `DM_Description` case-insensitively;
if none matches, `DM_AddProjectVariant` creates one and gets the name and description. Inside
`DM_BeginUpdate`/`DM_EndUpdate`, each non-inferred component's `DM_UniqueId` is looked up with
`DM_FindComponentVariationByUniqueId`, a variation is added if missing, and its unique id, alternate library link
and kind are set. With `DryRun=true` nothing is created or changed, only counted. `ListVariantAlternates` reads
`DM_VariationCount`/`DM_Variations` and, for alternate variations, the link's `DM_DesignItemID` and
`DM_LibraryIdentifier`.

## Notes
- The project is changed in memory only; nothing is saved. The change marks the `.PrjPcb` modified, and an
  in-memory document named `<project file name>Variants` appears next to it; nothing is written to disk until the
  project is saved.
- The library link setters return nothing and do no check: a library that is not installed, or an item that does
  not exist, is accepted.
- New variations get only the unique id, not a physical designator (`DM_SetPhysicalDesignator`); their
  `DM_PhysicalDesignator` reads back empty.
- `EDP.IComponent` declares `DM_UniqueId_1` besides the `DM_UniqueId` it inherits from `EDP.IPart`. The sample
  uses `DM_UniqueId`, and `CreateVariantComponent` reports how many components return a different value from the
  two; what `DM_UniqueId_1` stands for is not documented. On SpiritLevel-SL1 the two are equal on every component.
- What a `false` result of `DM_Compile` means is not documented; the sample reports it as a warning.
- Every placed component gets the same alternate part. That is a demonstration, not a useful design change: run it
  on a copy of a project.
