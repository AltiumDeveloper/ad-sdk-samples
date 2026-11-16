using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // GetDXPWorkspace is an extension method of DXP.IClient
using EDP; // the IBom* getters and the DM_* members are extension methods in EDP
using PCB;

namespace AdSamples.BomReport
{
    /// <summary>
    /// Builds a grouped bill of materials from one of four sources and writes it as one '|Name=Value' row per BOM line:
    /// Schema and BomManager ask the BOM engine (IBomManagerFactory) for the focused document's BOM, Project walks the
    /// flattened project document, Pcb iterates the components of the current board.
    /// </summary>
    internal static class BomReportCommands
    {
        private static readonly string[] Sources = { "Schema", "BomManager", "Project", "Pcb" };

        /// <summary>
        /// GenerateBom "Source=Schema|BomManager|Project|Pcb|GroupBy=Comment,Footprint|Out=&lt;file or folder&gt;|Max=&lt;n&gt;|Update=true".
        /// </summary>
        [HotCommand("GenerateBom", "Build a grouped BOM and write it to a text file")]
        public static string GenerateBom(CommandContext ctx)
        {
            // The BOM source is a parameter; Schema is the default.
            string source = Sources.FirstOrDefault(s => s.Equals(ctx.Get("Source", "Schema"), StringComparison.OrdinalIgnoreCase))
                ?? throw new UserException($"Source must be one of {string.Join(", ", Sources)}.");
            List<string> groupBy = ctx.Get("GroupBy", "Comment,Footprint")
                .Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (groupBy.Count == 0)
                throw new UserException("GroupBy needs at least one parameter name, for example GroupBy=Comment,Footprint.");
            string maxText = ctx.Get("Max", "5");
            if (!int.TryParse(maxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 0)
                throw new UserException("Max must be a whole number >= 0 (0 = return all rows).");

            var report = new List<string>();
            BomTable table;
            string name;
            switch (source)
            {
                case "Schema":
                case "BomManager":
                    table = FromBomEngine(ctx, source == "Schema", groupBy, report, out name);
                    break;
                case "Project":
                    table = FromProject(ctx, groupBy, report, out name);
                    break;
                default:
                    table = FromPcb(ctx, groupBy, report, out name);
                    break;
            }

            List<string> rows = table.Rows().ToList();
            string path = OutputPath(ctx, source, name);
            File.WriteAllLines(path, rows);

            report.Insert(0, $"Source={source}: {rows.Count} BOM lines, {table.Lines.Sum(l => l.DesignatorCount)} designators, " +
                             $"{table.Columns.Count} columns from {name}.");
            report.Add("BOM written to " + path);
            int shown = max == 0 ? rows.Count : Math.Min(max, rows.Count);
            report.AddRange(rows.Take(shown));
            if (shown < rows.Count)
                report.Add($"... {rows.Count - shown} more rows in the file.");
            return string.Join(Environment.NewLine, report);
        }

        // ---------------------------------------------------------------- BOM engine (Source=Schema / BomManager)

        /// <summary>
        /// The BOM engine's BOM of the focused document. Schema (the default) also sets GroupOrder from
        /// GroupBy and splits each row's Designator list; BomManager uses the manager's grouping and columns as they are.
        /// The options are changed only for this call and restored in finally.
        /// </summary>
        private static BomTable FromBomEngine(CommandContext ctx, bool schemaMode, IReadOnlyList<string> groupBy,
                                              List<string> report, out string name)
        {
            IWorkspace workspace = Workspace();
            IDocument document = workspace.DM_FocusedDocument()
                ?? throw new UserException("No document is focused. Focus a schematic or PCB document of a project.");
            // The BOM engine builds a BOM from design documents; this sample accepts schematic and PCB documents only.
            string kind = document.DM_DocumentKind();
            if (!string.Equals(kind, "SCH", StringComparison.OrdinalIgnoreCase) && !string.Equals(kind, "PCB", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The focused document {document.DM_FileName()} is a '{kind}' document. Focus a schematic or PCB document.");
            name = document.DM_FileName();

            // The documented helper for the BOM engine (the "BomManagerFactory" server).
            IBomManagerFactory factory = EDP.Utils.GetBomManagerFactory()
                ?? throw new UserException("The BOM manager factory (BomManagerFactory server) is not available.");
            // The project's manager (GetManagerForProject) fails in GetOptions, so the
            // document's manager is used.
            IBomManager manager = factory.GetManagerForDocument(document)
                ?? throw new UserException($"The BOM engine returned no manager for {name}.");

            IBomManagerOptions options = manager.GetOptions();
            object savedState = options.GetState();
            try
            {
                options.SetAutoGroup(true);
                if (schemaMode)
                {
                    // Format "Comment=True|Footprint=True"; not documented.
                    string groupOrder = string.Join("|", groupBy.Select(g => g + "=True"));
                    options.SetGroupOrder(groupOrder);
                    report.Add($"GroupOrder set to '{groupOrder}', read back '{options.GetGroupOrder()}'.");
                }
                options.SetUseCurrentVariant(true);
                options.SetIncludeNotFitted(false);
                options.SetIncludeAlternatives(true);
                options.SetIncludeDbParametersInVariations(false);
                options.SetIncludeDbParameters(false);
                options.SetIncludePcbParameters(false);

                // Whether GetSchema reflects the new options without a rebuild is not documented.
                // Update=true rebuilds the schema first, so both can be compared.
                if (ctx.Flag("Update"))
                {
                    manager.UpdateSchema(false);
                    report.Add("UpdateSchema(false) called before GetSchema.");
                }

                IBomSchema schema = manager.GetSchema();
                IBomParameterDefinitionList definitions = schema.GetParameterDefinitions();
                IBomComponentList rows = schema.GetComponents();
                int rowCount = rows.GetCount(), groupRows = 0, skipped = 0;
                ctx.Log($"{name}: {rowCount} BOM rows, {definitions.GetCount()} columns");

                var table = new BomTable();
                for (int i = 0; i < definitions.GetCount(); i++)
                    table.AddColumn(definitions.GetItem(i).GetName());

                for (int i = 0; i < rowCount; i++)
                {
                    IBomComponent row = rows.GetItem(i);
                    if (row.IsGroup())
                        groupRows++;
                    IBomParameterDictionary parameters = row.GetParameters();
                    var line = new BomLine();

                    if (schemaMode)
                    {
                        // Schema: the row's Designator value is a comma separated list; rows without
                        // one are skipped. All of the row's parameters are kept.
                        if (!parameters.TryGetParameter(BomTable.DesignatorColumn, out IBomParameter designators))
                        {
                            skipped++;
                            continue;
                        }
                        line.Designators.AddRange(designators.GetStrValue().Split(',').Select(d => d.Trim()).Where(d => d.Length > 0));
                        IBomParameterEnumerator e = parameters.GetEnumerator();
                        while (e.MoveNext())
                        {
                            IBomParameter p = e.GetCurrent();
                            // A name can repeat; the first value wins.
                            line.Values.TryAdd(p.GetName(), p.GetStrValue());
                        }
                        table.Lines.Add(line);
                    }
                    else
                    {
                        // BomManager: every column value read as the engine gives it, including Designator and Quantity
                        // (not split or counted in this mode).
                        line.ComputeDesignators = false;
                        foreach (string column in table.Columns)
                            line.Values[column] = parameters.TryGetParameter(column, out IBomParameter p) ? p.GetStrValue() : string.Empty;
                        table.Lines.Add(line);
                    }
                }

                report.Add($"BOM engine: {rowCount} rows ({groupRows} groups), {definitions.GetCount()} columns" +
                           (skipped > 0 ? $", {skipped} rows without a Designator skipped." : "."));

                if (!schemaMode)
                    return table;
                // Schema: Designator and Quantity are computed from the split list; make sure the columns exist.
                if (!table.Columns.Contains(BomTable.QuantityColumn)) table.Columns.Insert(0, BomTable.QuantityColumn);
                if (!table.Columns.Contains(BomTable.DesignatorColumn)) table.Columns.Insert(0, BomTable.DesignatorColumn);
                return table;
            }
            finally
            {
                // GetState/RestoreState: leave the document's BOM options as they were.
                options.RestoreState(savedState);
            }
        }

        // ---------------------------------------------------------------- Project (Source=Project)

        /// <summary>
        /// The flattened project document's components, without the kinds that are not in a BOM and without the
        /// components not fitted in the current variant, grouped by GroupBy.
        /// </summary>
        private static BomTable FromProject(CommandContext ctx, IReadOnlyList<string> groupBy, List<string> report, out string name)
        {
            IProject project = Workspace().DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and focus one of its documents.");
            name = Path.GetFileName(project.DM_ProjectFullPath());

            if (project.DM_NeedsCompile())
            {
                ctx.Log($"Compiling {name}...");
                report.Add(project.DM_Compile() ? "Project compiled." : "DM_Compile returned false; the BOM may be incomplete.");
            }

            IDocument flattened = project.DM_DocumentFlattened()
                ?? throw new UserException($"{name} has no flattened document. Compile the project.");
            IProjectVariant variant = project.DM_CurrentProjectVariant();
            report.Add(variant == null ? "Current variant: none." : $"Current variant: '{variant.DM_Description()}'.");

            var components = new List<(string, Dictionary<string, string>)>();
            int count = flattened.DM_ComponentCount(), skippedKind = 0, notFitted = 0;
            for (int i = 0; i < count; i++)
            {
                IComponent component = flattened.DM_Components(i);
                if (!InBom(component.DM_ComponentKind()))
                {
                    skippedKind++;
                    continue;
                }

                IComponentVariation variation = variant?.DM_FindComponentVariationByUniqueId(component.DM_UniqueId());
                if (variation != null && variation.DM_VariationKind() == TVariationKind.eVariation_NotFitted)
                {
                    notFitted++;
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (variation != null)
                {
                    // Varied values first, so they win over the component's own values.
                    for (int v = 0; v < variation.DM_VariationCount(); v++)
                    {
                        IParameterVariation pv = variation.DM_Variations(v);
                        values.TryAdd(pv.DM_ParameterName(), pv.DM_VariedValue());
                    }
                }
                // An alternate part (eVariation_Alternate) gets only its varied parameters.
                if (variation == null || variation.DM_VariationKind() == TVariationKind.eVariation_None)
                {
                    for (int p = 0; p < component.DM_ParameterCount(); p++)
                    {
                        IParameter parameter = component.DM_Parameters(p);
                        values.TryAdd(parameter.DM_Name(), parameter.DM_Value());
                    }
                }
                components.Add((component.DM_PhysicalDesignator(), values));
            }

            report.Add($"Flattened document: {count} components, {skippedKind} skipped by kind, {notFitted} not fitted.");
            return BomTable.Group(components, groupBy);
        }

        // ---------------------------------------------------------------- PCB (Source=Pcb)

        /// <summary>
        /// The components on the electrical layers of the current board, without the kinds that are not in a BOM,
        /// with a few component properties plus their PCB parameters, grouped by GroupBy.
        /// </summary>
        private static BomTable FromPcb(CommandContext ctx, IReadOnlyList<string> groupBy, List<string> report, out string name)
        {
            IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard()
                ?? throw new UserException("Open a .PcbDoc and make it the active document.");
            name = Path.GetFileName(board.GetState_FileName());

            var components = new List<(string, Dictionary<string, string>)>();
            int total = 0, skippedKind = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.eComponentObject });
                // Components on non-electrical layers are not iterated.
                iterator.AddFilter_LayerSet_2(PCBConstant.ElectricalLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var component = iterator.FirstPCBObject() as IPCB_Component; component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    total++;
                    if (!InBom(component.GetState_ComponentKind()))
                    {
                        skippedKind++;
                        continue;
                    }

                    var values = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Comment"] = component.GetState_Comment()?.GetState_Text() ?? string.Empty,
                        ["Description"] = component.GetState_SourceDescription(),
                        ["Design Item ID"] = component.GetState_SourceCompDesignItemID(),
                        ["Source"] = component.GetState_SourceComponentLibrary(),
                        // Height is in internal units; written in mils here.
                        ["Height"] = EDP.Utils.CoordToMils(component.GetState_Height()).ToString("0.###", CultureInfo.InvariantCulture) + "mil",
                        // Not FootprintConfiguratorName, which was empty for every component of
                        // the SpiritLevel board, so grouping by Footprint merged different
                        // footprints. Pattern is the footprint name the board shows.
                        ["Footprint"] = component.GetState_Pattern(),
                        ["Library"] = component.GetState_SourceFootprintLibrary(),
                        ["Footprint Description"] = component.GetState_FootprintDescription(),
                    };

                    // A component casts to IPCB_PrimitiveParameters (docs). A PCB parameter can repeat a name above (for example 'Comment'); the component
                    // property wins here.
                    var parameters = (IPCB_PrimitiveParameters)component;
                    for (int i = 0; i < parameters.Count(); i++)
                    {
                        IPCB_Parameter parameter = parameters.GetParameterByIndex(i);
                        if (parameter != null)
                            values.TryAdd(parameter.GetName(), parameter.GetValue());
                    }
                    components.Add((component.GetState_SourceDesignator(), values));
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            ctx.Log($"{name}: {total} components");
            report.Add($"Board: {total} components on electrical layers, {skippedKind} skipped by kind (no variant filter).");
            return BomTable.Group(components, groupBy);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Graphical, mechanical and the two No-BOM kinds are left out of a BOM (Project and Pcb sources).</summary>
        private static bool InBom(TComponentKind kind) =>
            kind != TComponentKind.eComponentKind_Graphical && kind != TComponentKind.eComponentKind_Mechanical &&
            kind != TComponentKind.eComponentKind_NetTie_NoBOM && kind != TComponentKind.eComponentKind_Standard_NoBOM;

        private static IWorkspace Workspace() =>
            DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
            ?? throw new UserException("The workspace is not available.");

        /// <summary>Out=&lt;file&gt; or Out=&lt;folder&gt;; default: BomReport_&lt;Source&gt;_&lt;design&gt;.txt in the temp folder.</summary>
        private static string OutputPath(CommandContext ctx, string source, string design)
        {
            string fileName = $"BomReport_{source}_{Path.GetFileNameWithoutExtension(design)}.txt";
            string output = ctx.Get("Out");
            if (string.IsNullOrWhiteSpace(output))
                return Path.Combine(Path.GetTempPath(), fileName);
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) || output.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                Directory.CreateDirectory(output);
                return Path.Combine(output, fileName);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            return output;
        }
    }
}
