# List variant components next to PCB components

Reports the component variations of the focused project's current variant, with their alternate library links,
and lists the components of the current PCB board with the IDs that tie them to the schematic. It shows how to
read project variant data and how to match a PCB component to its variation.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject` / `EDP.IProject.DM_CurrentProjectVariant`: the variant selected for the
  focused project, or none.
- `EDP.IProjectVariant.DM_VariationCount` / `EDP.IProjectVariant.DM_Variations`: the component variations stored
  in the variant (components the variant leaves unchanged have no entry).
- `EDP.IComponentVariation.DM_PhysicalDesignator`, `EDP.IComponentVariation.DM_UniqueId`,
  `EDP.IComponentVariation.DM_VariationKind`: which component a variation applies to and what it does
  (`EDP.TVariationKind`: None, NotFitted, Alternate).
- `EDP.IComponentVariation.DM_AlternateLibraryLink` and `EDP.IComponentLibraryLink.DM_LibraryIdentifier`,
  `DM_SymbolReference`, `DM_DesignItemID`, `DM_SourceLibraryName`, `DM_Footprint`: the alternate part.
- `PCB.IPCB_BoardIterator` with `AddFilter_ObjectSet`, `AddFilter_AllLayers` and `AddFilter_Method`
  (`PCB.TIterationMethod.eProcessFree`): walk the board's components.
- `PCB.IPCB_Component.SourceDesignator`, `PCB.IPCB_Component.SourceUniqueId`, `PCB.IPCB_Primitive.UniqueId`: the
  PCB component's link to its schematic component, and its own ID.
- `EDP.IProjectVariant.DM_FindComponentVariationByUniqueId` / `DM_FindComponentVariationByDesignator`: look a
  component up in the variant.

## Run it
1. Open a project that has a current variant (for example a copy of the SpiritLevel-SL1 example project that ships
   with Altium Designer, after **GetPcbComponentForVariant › Add Test Variant**), focus it, and open and focus its
   `.PcbDoc`.
2. **GetPcbComponentForVariant › Get Variant Component**, or
   `tools/ad.ps1 -Sample GetPcbComponentForVariant run GetVariantComponent "Max=20"`
3. The report has a variant section (one row per variation) and a PCB section (one row per component, with the
   variation found for it, if any).

| Command | Parameters | Result |
|---|---|---|
| `GetVariantComponent` | `Max=<n>` (rows per section, default 50, 0 = all) | `Project <file>, current variant '<name>' ...: n component variation(s)`, then rows `<designator> UniqueId=<id> Kind=<kind> <link>`; `PCB <file>: n component(s), m with a variation in the current variant`, then rows `<designator> SourceUniqueId=<id> UniqueId=<id> Variant: <kind> (found by ...)` |
| `AddTestVariant` | `Variant=<description>` (optional; a default name is used) | `Created variant '<name>' ...: <designator> (UniqueId=<id>) set to NotFitted ... Current variant is now '<name>'.` |

## How it works
`GetPcbComponentForVariantCommands.cs`, `GetVariantComponent`: `ReportVariant` gets the workspace with
`DXP.IClient.GetDXPWorkspace`, the focused project and its current variant, and lists each variation with its kind
and, when there is one, its alternate library link. `ReportPcbComponents` takes the board from
`PCB.GlobalVars.PCBServer.GetCurrentPCBBoard`, iterates the free components on all layers, and for each one reads
the source designator, source unique ID and unique ID. If a variant was found, it looks the component up by
`SourceUniqueId`, then by `SourceDesignator`, and reports the kind and which lookup matched. The iterator is
destroyed in a `finally`. When the project, variant or board is missing, that section is replaced by a
"skipped" line. The other section still runs.

`AddTestVariant` (a setup helper) compiles the project if it needs it, takes the first placed
component of `EDP.IProject.DM_DocumentFlattened`, and finds or creates a variant by description. Between
`DM_BeginUpdate` and `DM_EndUpdate` it adds or updates a `NotFitted` variation for that component, then makes the
variant current with `EDP.IProject.DM_SetCurrentProjectVariant`.

## Notes
- `GetVariantComponent` changes nothing. `AddTestVariant` changes the project in memory and marks the `.PrjPcb`
  modified: close it without saving to discard the variant.
- `DM_AlternateLibraryLink` returns a **non-null** `IComponentLibraryLink` even for a variation that is not an
  alternate: on a `NotFitted` variation all five fields are empty. So test `DM_VariationKind`, not the reference, to
  decide whether there is an alternate part.
- `PCB.IPCB_Component.SourceUniqueId` and `EDP.IComponentVariation.DM_UniqueId` use the **same hierarchical path
  form** (`\<sheet symbol id>\<component id>`, or `\<component id>` on the top sheet), so
  `DM_FindComponentVariationByUniqueId` can take `SourceUniqueId` as is. It returns null for a component with no
  variation.
- Reading `DM_CurrentProjectVariant` needs no compile; `AddTestVariant` compiles only because the flattened
  document is what it reads components from. `DM_CurrentProjectVariant` returns null on a just-opened project with
  no current variant, which is why the command handles that case.
- The command doesn't check that the board belongs to the focused project: the two sections use whatever project
  is focused and whatever PCB document is active.
