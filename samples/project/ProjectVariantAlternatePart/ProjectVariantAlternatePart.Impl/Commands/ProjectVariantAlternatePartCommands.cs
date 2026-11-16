using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXP; // IClient.GetDXPWorkspace is a typed helper in the DXP namespace
using EDP; // the DM_* helpers of the workspace, project and variant objects

namespace AdSamples.ProjectVariantAlternatePart
{
    /// <summary>
    /// Creates (or reuses) a project variant and gives every placed component of the focused project an
    /// alternate part: an EDP.IComponentVariation of kind eVariation_Alternate whose alternate library link names
    /// a library and a design item. A second command lists the variant's component variations to check the result.
    /// </summary>
    internal static class ProjectVariantAlternatePartCommands
    {
        private const string DefaultVariant = "Test";
        private const string DefaultLibrary = "Miscellaneous Devices.IntLib";
        private const string DefaultItem = "Bridge2";

        /// <summary>
        /// CreateVariantComponent "Variant=&lt;description&gt;|Library=&lt;lib&gt;|Item=&lt;design item&gt;|DryRun=true".
        /// Changes the project in memory only; nothing is saved.
        /// </summary>
        [HotCommand("CreateVariantComponent", "Give every component an alternate part in a project variant")]
        public static string CreateVariantComponent(CommandContext ctx)
        {
            string variantName = ctx.Get("Variant", DefaultVariant);
            string library = ctx.Get("Library", DefaultLibrary);
            string item = ctx.Get("Item", DefaultItem);
            bool dryRun = ctx.Flag("DryRun");
            if (string.IsNullOrWhiteSpace(variantName) || string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(item))
                throw new UserException("Variant, Library and Item must not be empty.");

            IProject project = FocusedProject();
            var lines = new List<string>();

            // The flattened document is built by compiling: compile first when the project says it is out of date.
            // DM_Compile returns a bool; what false means exactly is not documented, so it is reported, not hidden.
            if (project.DM_NeedsCompile())
            {
                bool compiled = project.DM_Compile();
                lines.Add(compiled ? "Project compiled." : "Warning: DM_Compile returned false; the component list may be stale.");
            }

            IDocument flattened = project.DM_DocumentFlattened()
                ?? throw new UserException("The project has no flattened document. Add schematic sheets and compile it.");

            // Variants are found by description, case-insensitively.
            IProjectVariant variant = FindVariant(project, variantName);
            bool created = false;
            if (variant == null && !dryRun)
            {
                variant = project.DM_AddProjectVariant();
                variant.DM_SetName(variantName);
                variant.DM_SetDescription(variantName);
                created = true;
            }

            int added = 0, updated = 0, inferred = 0, noUniqueId = 0, idMismatch = 0;
            int count = flattened.DM_ComponentCount();

            // DM_BeginUpdate / DM_EndUpdate bracket the bulk change of the project's variant data.
            if (!dryRun)
                project.DM_BeginUpdate();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IComponent component = flattened.DM_Components(i);
                    // Inferred objects are created by the compiler, not placed by the user: skip them.
                    if (component.DM_IsInferredObject())
                    {
                        inferred++;
                        continue;
                    }

                    // DM_UniqueId is inherited from EDP.IPart. EDP.IComponent also declares DM_UniqueId_1; whether
                    // the two ever differ is not documented, so the count of differences is reported.
                    string uniqueId = component.DM_UniqueId();
                    if (!string.Equals(uniqueId, component.DM_UniqueId_1(), StringComparison.Ordinal))
                        idMismatch++;
                    if (string.IsNullOrEmpty(uniqueId))
                    {
                        noUniqueId++;
                        continue;
                    }

                    // An existing variation of this component is updated, so running the command twice does not
                    // duplicate it.
                    IComponentVariation variation = variant?.DM_FindComponentVariationByUniqueId(uniqueId);
                    if (variation == null)
                        added++;
                    else
                        updated++;
                    if (dryRun)
                        continue;

                    if (variation == null)
                        variation = variant.DM_AddComponentVariation();
                    variation.DM_SetUniqueId(uniqueId);

                    // The alternate part is a library link: library name plus design item id. The setters return
                    // nothing and validate nothing, so a library that is not installed is accepted here.
                    IComponentLibraryLink link = variation.DM_AlternateLibraryLink();
                    link.DM_SetUseLibraryName(true);
                    link.DM_SetLibraryIdentifier(library);
                    link.DM_SetSourceLibraryName(library);
                    link.DM_SetDesignItemID(item);
                    variation.DM_SetVariationKind(TVariationKind.eVariation_Alternate);

                    if ((added + updated) % 50 == 0)
                        ctx.Log($"{added + updated} components processed");
                }
            }
            finally
            {
                if (!dryRun)
                    project.DM_EndUpdate();
            }

            string variantState = dryRun
                ? (variant == null ? $"variant '{variantName}' does not exist and would be created" : $"variant '{variantName}' exists")
                : (created ? $"created variant '{variantName}'" : $"reused variant '{variantName}'");
            string verb = dryRun ? "would add" : "added";
            string verb2 = dryRun ? "would update" : "updated";
            lines.Insert(0, $"{ProjectName(project)}: {(dryRun ? "dry run, nothing changed; " : "")}{variantState}.");
            lines.Add($"Alternate part '{item}' from '{library}': {verb} {added}, {verb2} {updated} component variation(s); " +
                      $"{count} components in the flattened document, {inferred} inferred skipped" +
                      (noUniqueId > 0 ? $", {noUniqueId} without a unique id skipped" : "") + ".");
            lines.Add($"DM_UniqueId differs from DM_UniqueId_1 on {idMismatch} component(s).");
            if (!dryRun)
            {
                lines.Add($"Variant now has {variant.DM_VariationCount()} component variation(s). " +
                          "The project is changed in memory: save it to keep the variant.");
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// ListVariantAlternates "Variant=&lt;description&gt;|Max=&lt;n&gt;": lists the variant's component variations
        /// (kind and alternate library link), or the project's variants when the named one does not exist.
        /// </summary>
        [HotCommand("ListVariantAlternates", "List the component variations of a project variant")]
        public static string ListVariantAlternates(CommandContext ctx)
        {
            string variantName = ctx.Get("Variant", DefaultVariant);
            int max = int.TryParse(ctx.Get("Max", "20"), out int m) && m >= 0 ? m : 20;

            IProject project = FocusedProject();
            IProjectVariant variant = FindVariant(project, variantName);
            if (variant == null)
            {
                var names = new List<string>();
                for (int i = 0; i < project.DM_ProjectVariantCount(); i++)
                {
                    IProjectVariant v = project.DM_ProjectVariants(i);
                    names.Add($"'{v.DM_Name()}' ('{v.DM_Description()}')");
                }
                return $"{ProjectName(project)}: no variant with description '{variantName}'. " +
                       $"{names.Count} variant(s): {(names.Count == 0 ? "none" : string.Join(", ", names))}.";
            }

            int count = variant.DM_VariationCount();
            var rows = new List<string>();
            var summary = new Dictionary<string, int>();
            for (int i = 0; i < count; i++)
            {
                IComponentVariation variation = variant.DM_Variations(i);
                TVariationKind kind = variation.DM_VariationKind();
                string part = "";
                if (kind == TVariationKind.eVariation_Alternate)
                {
                    IComponentLibraryLink link = variation.DM_AlternateLibraryLink();
                    part = $"{link.DM_DesignItemID()} in {link.DM_LibraryIdentifier()}";
                }
                string key = kind + (part.Length > 0 ? $" -> {part}" : "");
                summary[key] = summary.TryGetValue(key, out int n) ? n + 1 : 1;
                if (rows.Count < max)
                {
                    rows.Add($"  #{i} '{variation.DM_PhysicalDesignator()}' uid={variation.DM_UniqueId()} {key}");
                }
            }

            var lines = new List<string>
            {
                $"{ProjectName(project)}: variant '{variant.DM_Name()}' ('{variant.DM_Description()}'), {count} component variation(s)"
            };
            lines.AddRange(summary.OrderByDescending(p => p.Value).Select(p => $"  {p.Value} x {p.Key}"));
            lines.AddRange(rows);
            if (count > rows.Count)
                lines.Add($"  ... {count - rows.Count} more (Max={max})");
            return string.Join(Environment.NewLine, lines);
        }

        private static IProjectVariant FindVariant(IProject project, string description)
        {
            for (int i = 0; i < project.DM_ProjectVariantCount(); i++)
            {
                IProjectVariant variant = project.DM_ProjectVariants(i);
                if (string.Equals(variant.DM_Description(), description, StringComparison.OrdinalIgnoreCase))
                    return variant;
            }
            return null;
        }

        /// <summary>The focused project, queried again on every command.</summary>
        private static IProject FocusedProject()
        {
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and select it in the Projects panel.");
        }

        private static string ProjectName(IProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
