using System;
using System.Collections.Generic;
using System.IO;
using DXP; // IClient.GetDXPWorkspace is a typed helper in the DXP namespace
using EDP; // the DM_* helpers of the workspace, project and variant objects
using PCB;

namespace AdSamples.GetPcbComponentForVariant
{
    /// <summary>
    /// Reports the focused project's current variant (its component variations and their alternate library links)
    /// and the components of the current PCB board (source designator, source unique ID, unique ID), and looks each
    /// PCB component up in the variant. The two halves are independent: a missing input skips one half with a note.
    /// </summary>
    internal static class GetPcbComponentForVariantCommands
    {
        private const int DefaultMax = 50;

        /// <summary>GetVariantComponent "Max=&lt;n&gt;": at most n rows per section (0 = all, default 50).</summary>
        [HotCommand("GetVariantComponent", "List the current variant's component variations and the board's PCB components")]
        public static string GetVariantComponent(CommandContext ctx)
        {
            string maxText = ctx.Get("Max", DefaultMax.ToString());
            if (!int.TryParse(maxText, out int max) || max < 0)
                throw new UserException($"Max must be a whole number >= 0 (0 = all), not '{maxText}'.");
            if (max == 0)
                max = int.MaxValue;

            var lines = new List<string>();
            IProjectVariant variant = ReportVariant(lines, max);
            lines.Add("");
            ReportPcbComponents(ctx, lines, variant, max);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// AddTestVariant "Variant=&lt;description&gt;": a setup helper. Finds or creates a variant
        /// (matched by description), marks the first placed component of the flattened design Not Fitted and makes the
        /// variant the current one, so GetVariantComponent has a variation to report. Changes the project in memory
        /// only: close it without saving afterwards.
        /// </summary>
        [HotCommand("AddTestVariant", "Create a variant with one Not Fitted component and make it current (test setup)")]
        public static string AddTestVariant(CommandContext ctx)
        {
            string name = ctx.Get("Variant", "SampleVariant");
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("Variant must not be empty.");

            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and select it in the Projects panel.");

            // The components come from the flattened document, which compiling builds.
            if (project.DM_NeedsCompile())
                project.DM_Compile();
            IDocument flattened = project.DM_DocumentFlattened()
                ?? throw new UserException("The project has no flattened document. Add schematic sheets and compile it.");

            IComponent target = null;
            for (int i = 0; i < flattened.DM_ComponentCount() && target == null; i++)
            {
                IComponent component = flattened.DM_Components(i);
                if (!component.DM_IsInferredObject() && !string.IsNullOrEmpty(component.DM_UniqueId()))
                    target = component;
            }
            if (target == null)
                throw new UserException("The flattened design has no placed component with a unique ID.");

            IProjectVariant variant = null;
            for (int i = 0; i < project.DM_ProjectVariantCount() && variant == null; i++)
            {
                IProjectVariant v = project.DM_ProjectVariants(i);
                if (string.Equals(v.DM_Description(), name, StringComparison.OrdinalIgnoreCase))
                    variant = v;
            }

            bool created = variant == null;
            project.DM_BeginUpdate();
            try
            {
                if (created)
                {
                    variant = project.DM_AddProjectVariant();
                    variant.DM_SetName(name);
                    variant.DM_SetDescription(name);
                }
                IComponentVariation variation = variant.DM_FindComponentVariationByUniqueId(target.DM_UniqueId())
                                                ?? variant.DM_AddComponentVariation();
                variation.DM_SetUniqueId(target.DM_UniqueId());
                variation.DM_SetPhysicalDesignator(target.DM_PhysicalDesignator());
                variation.DM_SetVariationKind(TVariationKind.eVariation_NotFitted);
                project.DM_SetCurrentProjectVariant(variant);
            }
            finally
            {
                project.DM_EndUpdate();
            }

            IProjectVariant current = project.DM_CurrentProjectVariant();
            return $"{(created ? "Created" : "Reused")} variant '{name}' in {ProjectName(project)}: " +
                   $"{target.DM_PhysicalDesignator()} (UniqueId={target.DM_UniqueId()}) set to NotFitted, " +
                   $"{variant.DM_VariationCount()} variation(s). Current variant is now " +
                   $"'{(current == null ? "(none)" : current.DM_Description())}'. Close the project without saving to discard it.";
        }

        /// <summary>First half: the component variations of the focused project's current variant.</summary>
        private static IProjectVariant ReportVariant(List<string> lines, int max)
        {
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace;
            // A missing focused project is reported instead of failing.
            IProject project = workspace?.DM_FocusedProject();
            if (project == null)
            {
                lines.Add("Variant: skipped, no project is focused.");
                return null;
            }

            // DM_CurrentProjectVariant is the variant selected for the project; none when the project has no variant
            // or none is selected.
            IProjectVariant variant = project.DM_CurrentProjectVariant();
            if (variant == null)
            {
                lines.Add($"Variant: skipped, project {ProjectName(project)} has no current variant.");
                return null;
            }

            // DM_VariationCount / DM_Variations: the component variations stored in this variant. Components the
            // variant leaves unchanged have no entry.
            int count = variant.DM_VariationCount();
            lines.Add($"Project {ProjectName(project)}, current variant '{variant.DM_Name()}' " +
                      $"('{variant.DM_Description()}'): {count} component variation(s)");

            int shown = 0;
            for (int i = 0; i < count && shown < max; i++)
            {
                IComponentVariation variation = variant.DM_Variations(i);
                if (variation == null)
                    continue;
                shown++;

                string row = $"  {variation.DM_PhysicalDesignator()}  UniqueId={variation.DM_UniqueId()}  " +
                             $"Kind={KindName(variation.DM_VariationKind())}";

                // The API does not promise a link for every variation, so a null link is guarded here. Check
                // DM_VariationKind to decide whether there is an alternate part: a NotFitted variation has an empty link.
                IComponentLibraryLink link = variation.DM_AlternateLibraryLink();
                row += link == null
                    ? "  (no alternate library link)"
                    : $"  LibraryIdentifier='{link.DM_LibraryIdentifier()}' SymbolReference='{link.DM_SymbolReference()}' " +
                      $"DesignItemID='{link.DM_DesignItemID()}' SourceLibraryName='{link.DM_SourceLibraryName()}' " +
                      $"Footprint='{link.DM_Footprint()}'";
                lines.Add(row);
            }
            if (count > shown)
                lines.Add($"  ... {count - shown} more (raise Max to see them)");
            return variant;
        }

        /// <summary>Second half: the components of the current board, each looked up in the variant.</summary>
        private static void ReportPcbComponents(CommandContext ctx, List<string> lines, IProjectVariant variant, int max)
        {
            IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard();
            if (board == null)
            {
                lines.Add("PCB: skipped, no PCB document is active.");
                return;
            }

            var rows = new List<string>();
            int total = 0, matched = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // Filters: components on all layers, free objects only (eProcessFree; components are
                // not part of another group).
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessFree);

                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    total++;
                    string sourceDesignator = component.GetState_SourceDesignator();
                    string sourceUniqueId = component.GetState_SourceUniqueId();

                    // SourceUniqueId is the unique ID of the schematic component the PCB component was placed from,
                    // in the same hierarchical form as IComponentVariation.DM_UniqueId. The designator lookup is a
                    // second try for a component whose unique ID does not match, and the report says which matched.
                    string inVariant = "";
                    if (variant != null)
                    {
                        string how = "unique id";
                        IComponentVariation variation = variant.DM_FindComponentVariationByUniqueId(sourceUniqueId);
                        if (variation == null && !string.IsNullOrEmpty(sourceDesignator))
                        {
                            how = "designator";
                            variation = variant.DM_FindComponentVariationByDesignator(sourceDesignator);
                        }
                        if (variation != null)
                            matched++;
                        inVariant = variation == null
                            ? "  Variant: no variation"
                            : $"  Variant: {KindName(variation.DM_VariationKind())} (found by {how})";
                    }

                    if (rows.Count < max)
                        rows.Add($"  {sourceDesignator}  SourceUniqueId={sourceUniqueId}  UniqueId={component.GetState_UniqueId()}{inVariant}");
                    if (total % 200 == 0)
                        ctx.Log($"{total} PCB components read");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            lines.Add($"PCB {Path.GetFileName(board.GetState_FileName())}: {total} component(s)" +
                      (variant != null ? $", {matched} with a variation in the current variant" : ""));
            lines.AddRange(rows);
            if (total > rows.Count)
                lines.Add($"  ... {total - rows.Count} more (raise Max to see them)");
        }

        /// <summary>The enum name without its prefix: None, NotFitted or Alternate.</summary>
        private static string KindName(TVariationKind kind) => kind.ToString().Replace("eVariation_", "");

        private static string ProjectName(IProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
