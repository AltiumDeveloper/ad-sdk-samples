using System;
using System.Collections.Generic;
using System.IO;
using DXP; // IClient.GetDXPWorkspace is a typed helper in the DXP namespace
using EDP; // the DM_* objects of the workspace, project, variants and variations

namespace AdSamples.ProjectVariantAdd
{
    /// <summary>
    /// Creates (or reuses) a project variant and, for every component of the focused project, a component variation
    /// of kind eVariation_None whose parameter variations override each parameter value with the value plus a suffix.
    /// A second command reads the variant back so the result can be checked without opening Project Options.
    /// </summary>
    internal static class ProjectVariantAddCommands
    {
        private const string DefaultName = "Testing";
        private const string DefaultSuffix = "_$";

        /// <summary>
        /// AddVariant "Name=&lt;variant name&gt;|Suffix=&lt;text&gt;". Changes the project in memory only; nothing is saved.
        /// </summary>
        [HotCommand("AddVariant", "Create or update a project variant with parameter variations")]
        public static string AddVariant(CommandContext ctx)
        {
            string name = ctx.Get("Name", DefaultName);
            string suffix = ctx.Get("Suffix", DefaultSuffix);
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("Name must not be empty.");

            IProject project = FocusedProject();
            var lines = new List<string>();

            // The flattened document is built by compiling. DM_Compile returns a bool;
            // what false means exactly is not documented, so it is reported rather than treated as fatal.
            if (project.DM_NeedsCompile())
            {
                bool compiled = project.DM_Compile();
                lines.Add(compiled ? "Project compiled." : "Warning: DM_Compile returned false; the component list may be stale.");
            }

            IDocument flattened = project.DM_DocumentFlattened();
            int count = flattened?.DM_ComponentCount() ?? 0;
            if (count == 0)
                throw new UserException("The compiled project has no components. Add schematic sheets with components and compile it.");

            int componentsAdded = 0, componentsUpdated = 0, parametersAdded = 0, parametersUpdated = 0;
            int inferred = 0, noUniqueId = 0;
            bool created = false;
            IProjectVariant variant;

            // DM_BeginUpdate / DM_EndUpdate bracket a bulk change of the project's variant data; the finally block
            // makes sure the bracket is closed even when a call in between throws.
            project.DM_BeginUpdate();
            try
            {
                variant = FindVariant(project, name);
                if (variant == null)
                {
                    variant = project.DM_AddProjectVariant();
                    variant.DM_SetName(name);
                    // The name is used as the description too.
                    variant.DM_SetDescription(name);
                    created = true;
                }

                for (int i = 0; i < count; i++)
                {
                    IComponent component = flattened.DM_Components(i);
                    // Inferred objects are created by the compiler, not placed by the user: they have no variation.
                    if (component.DM_IsInferredObject())
                    {
                        inferred++;
                        continue;
                    }

                    string uniqueId = component.DM_UniqueId();
                    if (string.IsNullOrEmpty(uniqueId))
                    {
                        noUniqueId++;
                        continue;
                    }

                    // Find-or-add keeps a second run from duplicating the component variation. The variation is
                    // linked to the schematic component by its unique ID, not by designator.
                    IComponentVariation variation = variant.DM_FindComponentVariationByUniqueId(uniqueId);
                    if (variation == null)
                    {
                        variation = variant.DM_AddComponentVariation();
                        componentsAdded++;
                    }
                    else
                    {
                        componentsUpdated++;
                    }

                    // eVariation_None: the component stays fitted and is not replaced; only parameters vary.
                    // The enum overload is used on purpose: DM_SetVariationKind also has an int overload.
                    variation.DM_SetVariationKind(TVariationKind.eVariation_None);
                    variation.DM_SetUniqueId(uniqueId);

                    for (int j = 0; j < component.DM_ParameterCount(); j++)
                    {
                        IParameter parameter = component.DM_Parameters(j);
                        string parameterName = parameter.DM_Name();

                        IParameterVariation parameterVariation = variation.DM_FindParameterVariation(parameterName);
                        if (parameterVariation == null)
                        {
                            parameterVariation = variation.DM_AddParameterVariation();
                            parametersAdded++;
                        }
                        else
                        {
                            parametersUpdated++;
                        }

                        parameterVariation.DM_SetParameterName(parameterName);
                        // The varied value is the component's value plus the suffix. The value is
                        // taken as DM_Value returns it; no special handling for empty values or formulas.
                        parameterVariation.DM_SetVariedValue(parameter.DM_Value() + suffix);
                    }

                    if ((componentsAdded + componentsUpdated) % 50 == 0)
                        ctx.Log($"{componentsAdded + componentsUpdated} components processed");
                }
            }
            finally
            {
                project.DM_EndUpdate();
            }

            lines.Insert(0, $"{ProjectName(project)}: {(created ? "created" : "reused")} variant '{name}'.");
            lines.Add($"Component variations: {componentsAdded} added, {componentsUpdated} updated " +
                      $"({count} components in the flattened document, {inferred} inferred skipped" +
                      (noUniqueId > 0 ? $", {noUniqueId} without a unique id skipped" : "") + ").");
            lines.Add($"Parameter variations (value + '{suffix}'): {parametersAdded} added, {parametersUpdated} updated.");
            lines.Add($"Variant now has {variant.DM_VariationCount()} component variation(s). " +
                      "The project is changed in memory: save it to keep the variant.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// ListVariant "Name=&lt;variant name&gt;|Max=&lt;n&gt;": lists the variant's component variations with their parameter
        /// variations, or the project's variants when the named one does not exist. Read-only.
        /// </summary>
        [HotCommand("ListVariant", "List the component and parameter variations of a project variant")]
        public static string ListVariant(CommandContext ctx)
        {
            string name = ctx.Get("Name", DefaultName);
            int max = int.TryParse(ctx.Get("Max", "10"), out int m) && m >= 0 ? m : 10;

            IProject project = FocusedProject();
            IProjectVariant variant = FindVariant(project, name);
            if (variant == null)
            {
                var names = new List<string>();
                for (int i = 0; i < project.DM_ProjectVariantCount(); i++)
                {
                    IProjectVariant v = project.DM_ProjectVariants(i);
                    names.Add($"'{v.DM_Name()}' ('{v.DM_Description()}')");
                }
                return $"{ProjectName(project)}: no variant named '{name}'. " +
                       $"{names.Count} variant(s): {(names.Count == 0 ? "none" : string.Join(", ", names))}.";
            }

            int count = variant.DM_VariationCount();
            int parameterTotal = 0;
            var rows = new List<string>();
            for (int i = 0; i < count; i++)
            {
                IComponentVariation variation = variant.DM_Variations(i);
                int parameterCount = variation.DM_VariationCount();
                parameterTotal += parameterCount;
                if (rows.Count >= max)
                    continue;

                var parameters = new List<string>();
                for (int j = 0; j < parameterCount; j++)
                {
                    IParameterVariation p = variation.DM_Variations(j);
                    parameters.Add($"{p.DM_ParameterName()}='{p.DM_VariedValue()}'");
                }
                rows.Add($"  #{i} '{variation.DM_PhysicalDesignator()}' uid={variation.DM_UniqueId()} " +
                         $"{variation.DM_VariationKind()}: {(parameters.Count == 0 ? "no parameter variations" : string.Join(", ", parameters))}");
            }

            var lines = new List<string>
            {
                $"{ProjectName(project)}: variant '{variant.DM_Name()}' ('{variant.DM_Description()}'), " +
                $"{count} component variation(s), {parameterTotal} parameter variation(s)"
            };
            lines.AddRange(rows);
            if (count > rows.Count)
                lines.Add($"  ... {count - rows.Count} more (Max={max})");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Finds a variant by name, case-insensitively. Matching
        /// on DM_Name (not DM_Description) finds the variant the command created even after the user edits its description.
        /// </summary>
        private static IProjectVariant FindVariant(IProject project, string name)
        {
            for (int i = 0; i < project.DM_ProjectVariantCount(); i++)
            {
                IProjectVariant variant = project.DM_ProjectVariants(i);
                if (string.Equals(variant.DM_Name(), name, StringComparison.OrdinalIgnoreCase))
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
