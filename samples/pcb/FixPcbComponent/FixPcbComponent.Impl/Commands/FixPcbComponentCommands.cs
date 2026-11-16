using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DXP; // the IClient.OpenDocument/ShowDocument/GetDocumentByPath extension methods live in namespace DXP
using EDP;
using PCB; // also brings the IPCB_Component helper extensions GetState_Name / GetState_Comment into scope
using SCH;

namespace AdSamples.FixPcbComponent
{
    /// <summary>
    /// Cross-document correlation between the schematic and the PCB of the focused project, matched by designator:
    /// FixPcbComponent copies a schematic parameter into each PCB component's Comment text, and
    /// ImportSchComponentParams writes parameters from a table file onto the schematic components.
    /// </summary>
    internal static class FixPcbComponentCommands
    {
        private const int MaxListed = 20;

        /// <summary>
        /// FixPcbComponent "Param=&lt;schematic parameter&gt;|DryRun=true": for every PCB component whose designator has a
        /// schematic component with that parameter, sets IPCB_Component.Comment's text to the parameter's text.
        /// The board changes in memory only.
        /// </summary>
        [HotCommand("FixPcbComponent", "Copy a schematic parameter into the PCB component comments")]
        public static string FixPcbComponent(CommandContext ctx)
        {
            // The schematic parameter to copy is a command parameter.
            string paramName = ctx.Get("Param", "Comment").Trim();
            if (paramName.Length == 0)
                throw new UserException("Pass the schematic parameter to copy: Param=<name>.");
            bool dryRun = ctx.Flag("DryRun");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus the project's .PcbDoc first.");
            string boardPath = board.GetState_FileName();
            IProject project = ProjectSchematics.FocusedProject();

            // 1. Schematic side: designator -> components with it, over all sheets. The first match is
            //    used (case-insensitive), and designators whose components disagree on the value are
            //    reported.
            //    The sheets are opened but not shown, so the PCB stays
            //    the current document (PreProcess/PostProcess act on the current view's document).
            List<ProjectSheet> sheets = ProjectSchematics.OpenSheets(ctx, project, show: false);
            if (sheets.Count == 0)
                throw new UserException($"The focused project {Path.GetFileName(project.DM_ProjectFileName())} has no schematic sheets.");
            var schByDesignator = new Dictionary<string, List<ISch_Component>>(StringComparer.OrdinalIgnoreCase);
            int schCount = 0;
            foreach (ProjectSheet sheet in sheets)
            {
                foreach (var (designator, component) in ProjectSchematics.Components(sheet.Sheet))
                {
                    schCount++;
                    if (!schByDesignator.TryGetValue(designator, out var list))
                        schByDesignator[designator] = list = new List<ISch_Component>();
                    list.Add(component);
                }
            }

            // Opening a sheet that was closed could make it the current document; then PreProcess would act on the
            // wrong document. Re-show the board in that case, as a precaution.
            IClient client = DXP.GlobalVars.Client;
            IServerDocument boardDocument = client.GetDocumentByPath(boardPath);
            IPCB_Board current = pcbServer.GetCurrentPCBBoard();
            if (boardDocument != null && (current == null || !string.Equals(current.GetState_FileName(), boardPath, StringComparison.OrdinalIgnoreCase)))
                client.ShowDocument(boardDocument);

            // 2. PCB side: walk the components and copy the text.
            int pcbCount = 0, matched = 0, updated = 0, unchanged = 0, noMatch = 0, noParam = 0, noComment = 0, ambiguous = 0;
            var changes = new List<string>();
            var ambiguousList = new List<string>();

            if (!dryRun)
                pcbServer.PreProcess();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    pcbCount++;
                    // The designator as shown on the board (IPCB_Component.Name).
                    string designator = component.GetState_Name()?.GetState_Text()?.Trim() ?? "";
                    if (!schByDesignator.TryGetValue(designator, out var schComponents))
                    {
                        noMatch++;
                        continue;
                    }
                    matched++;

                    ISch_Parameter parameter = ProjectSchematics.FindParameter(schComponents[0], paramName);
                    if (parameter == null)
                    {
                        noParam++;
                        continue;
                    }
                    string value = parameter.GetState_Text() ?? "";
                    if (schComponents.Skip(1).Any(other => ProjectSchematics.FindParameter(other, paramName)?.GetState_Text() is string v && v != value))
                    {
                        ambiguous++;
                        if (ambiguousList.Count < MaxListed)
                            ambiguousList.Add(designator);
                    }

                    IPCB_Text comment = component.GetState_Comment();
                    if (comment == null)
                    {
                        noComment++;
                        continue;
                    }
                    string before = comment.GetState_Text() ?? "";
                    if (before == value)
                    {
                        unchanged++;
                        continue;
                    }

                    if (!dryRun)
                    {
                        // The text is the object that
                        // changes, so it gets the BeginModify/EndModify bracket (docs/guides/pcb-basics.mdx).
                        comment.BeginModify();
                        comment.SetState_Text(value);
                        comment.EndModify();
                        component.GraphicallyInvalidate();
                    }
                    updated++;
                    if (changes.Count < MaxListed)
                        changes.Add($"  {designator}: '{before}' -> '{value}'");
                    if (pcbCount % 200 == 0)
                        ctx.Log($"{pcbCount} PCB components processed");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
                if (!dryRun)
                    pcbServer.PostProcess();
            }

            // Mark the board's own server document: Client.CurrentView's document need not be the board.
            if (!dryRun && updated > 0)
                boardDocument?.SetModified(true);

            var report = new List<string>
            {
                $"{Path.GetFileName(boardPath)}: {(dryRun ? "dry run, would update" : "updated")} {updated} comment(s) from schematic parameter '{paramName}'.",
                $"PCB components: {pcbCount}; matched by designator: {matched}; already equal: {unchanged}; " +
                $"no schematic match: {noMatch}; no parameter '{paramName}': {noParam}; designators with differing values: {ambiguous}" +
                (noComment > 0 ? $"; without a Comment text: {noComment}." : "."),
                $"Schematic: {schCount} component part(s) on {sheets.Count} sheet(s) of {Path.GetFileName(project.DM_ProjectFileName())}.",
            };
            if (changes.Count > 0)
            {
                report.Add(dryRun ? "Would change:" : "Changed:");
                report.AddRange(changes);
                if (updated > changes.Count)
                    report.Add($"  ... and {updated - changes.Count} more");
            }
            if (ambiguousList.Count > 0)
                report.Add("Differing values (first used): " + string.Join(", ", ambiguousList));
            if (!dryRun && updated > 0)
                report.Add("The board is modified in memory; save it to keep the change.");
            return string.Join(Environment.NewLine, report);
        }

        /// <summary>
        /// ImportSchComponentParams "File=&lt;csv&gt;|Delimiter=,|Show=true|DryRun=true": sets (or creates, hidden) the
        /// table's parameters on every schematic component of the focused project whose designator is listed.
        /// From a menu click without File a file dialog asks for it.
        /// </summary>
        [HotCommand("ImportSchComponentParams", "Import schematic component parameters from a CSV file")]
        public static string ImportSchComponentParams(CommandContext ctx)
        {
            IProject project = ProjectSchematics.FocusedProject();
            string path = ResolveFile(ctx, project);
            ParameterTable table = ParameterTable.Load(path, ctx.Get("Delimiter"));
            bool dryRun = ctx.Flag("DryRun");
            // By default every sheet opened is shown; Show=false only opens them.
            bool show = !string.Equals(ctx.Get("Show", "true"), "false", StringComparison.OrdinalIgnoreCase);

            List<ProjectSheet> sheets = ProjectSchematics.OpenSheets(ctx, project, show);
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running.");
            ISch_RobotManager robot = schServer.GetState_RobotManager();

            int components = 0, created = 0, updatedCount = 0, unchanged = 0, sheetsChanged = 0;
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perSheet = new List<string>();

            foreach (ProjectSheet sheet in sheets)
            {
                int sheetComponents = 0, sheetChanges = 0;
                foreach (var (designator, component) in ProjectSchematics.Components(sheet.Sheet))
                {
                    if (!table.Values.TryGetValue(designator, out List<string> values))
                        continue;
                    found.Add(designator);
                    sheetComponents++;

                    if (dryRun)
                    {
                        for (int k = 0; k < table.ParameterNames.Count; k++)
                        {
                            ISch_Parameter existing = ProjectSchematics.FindParameter(component, table.ParameterNames[k]);
                            if (existing == null) created++;
                            else if (existing.GetState_Text() == values[k]) unchanged++;
                            else updatedCount++;
                        }
                        continue;
                    }

                    // Change notifications through the robot manager (docs/guides/schematic-basics.mdx).
                    robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                    try
                    {
                        for (int k = 0; k < table.ParameterNames.Count; k++)
                        {
                            switch (ProjectSchematics.SetParameter(schServer, robot, component, table.ParameterNames[k], values[k]))
                            {
                                case "created": created++; sheetChanges++; break;
                                case "updated": updatedCount++; sheetChanges++; break;
                                default: unchanged++; break;
                            }
                        }
                    }
                    finally
                    {
                        robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                    }
                    component.GraphicallyInvalidate();
                }

                components += sheetComponents;
                if (sheetChanges > 0)
                {
                    sheetsChanged++;
                    sheet.ServerDocument?.SetModified(true);
                }
                if (sheetComponents > 0)
                    perSheet.Add($"  {Path.GetFileName(sheet.Path)}: {sheetComponents} component part(s), {sheetChanges} parameter change(s)");
            }

            List<string> missing = table.Values.Keys.Where(d => !found.Contains(d)).ToList();
            var report = new List<string>
            {
                $"{Path.GetFileName(path)}: {table.Rows} row(s), {table.Values.Count} designator(s), parameters: {string.Join(", ", table.ParameterNames)}.",
                $"{(dryRun ? "Dry run: would create" : "Created")} {created}, {(dryRun ? "would update" : "updated")} {updatedCount}, unchanged {unchanged} " +
                $"parameter(s) on {components} component part(s); {sheetsChanged} of {sheets.Count} sheet(s) modified.",
            };
            report.AddRange(perSheet);
            if (missing.Count > 0)
                report.Add($"Not found in the project ({missing.Count}): {string.Join(", ", missing.Take(MaxListed))}{(missing.Count > MaxListed ? ", ..." : "")}");
            if (table.DuplicateDesignators.Count > 0)
                report.Add($"Listed more than once (first row used): {string.Join(", ", table.DuplicateDesignators.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxListed))}");
            if (!dryRun && sheetsChanged > 0)
                report.Add("The sheets are modified in memory; save them to keep the change.");
            return string.Join(Environment.NewLine, report);
        }

        /// <summary>File=&lt;path&gt;; from a menu click without it, an Open dialog in the project folder.</summary>
        private static string ResolveFile(CommandContext ctx, IProject project)
        {
            string path = ctx.Get("File");
            if (string.IsNullOrWhiteSpace(path))
            {
                if (!ctx.IsInteractive)
                    throw new UserException("No File specified. Pass File=<path to a .csv file>.");
                using var dialog = new OpenFileDialog
                {
                    Title = "Open component parameter table",
                    Filter = "CSV files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                    InitialDirectory = Path.GetDirectoryName(project.DM_ProjectFullPath()) ?? "",
                    CheckFileExists = true,
                };
                if (dialog.ShowDialog() != DialogResult.OK)
                    throw new UserException("Cancelled.");
                path = dialog.FileName;
            }

            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(path))
                throw new UserException("File not found: " + path);
            return path;
        }
    }
}
