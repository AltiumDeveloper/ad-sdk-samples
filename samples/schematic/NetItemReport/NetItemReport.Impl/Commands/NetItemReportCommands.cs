using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;
using SCH;

namespace AdSamples.NetItemReport
{
    /// <summary>
    /// Compiles the focused project and reports every net of its flattened document, with the net items of each net
    /// resolved from their data-model handles to live schematic objects.
    /// </summary>
    internal static class NetItemReportCommands
    {
        /// <summary>How many nets the returned text lists; the report file always has all of them.</summary>
        private const int NetsInSummary = 15;

        /// <summary>
        /// GetNetInfo "Out=&lt;folder&gt;|Open=true|Compile=false": writes NetItemReport_&lt;project&gt;.txt and returns a summary.
        /// </summary>
        [HotCommand("GetNetInfo", "Report all nets and net items of the focused project")]
        public static string GetNetInfo(CommandContext ctx)
        {
            string outFolder = ctx.Get("Out", Path.GetTempPath());
            bool open = ctx.Flag("Open");
            // Compile is on unless explicitly turned off (ctx.Flag would treat a missing parameter as false).
            bool compile = !string.Equals(ctx.Get("Compile", "true"), "false", StringComparison.OrdinalIgnoreCase);

            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its documents.");

            // The nets exist only in the compiled model: compile first (unless Compile=false).
            bool compiled = true;
            if (compile)
            {
                ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                compiled = project.DM_Compile();
            }

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            var report = new List<string>();
            var summary = new StringBuilder();
            var totals = new Totals();
            int netCount;

            // Every access to the compiled model, including resolving handles, happens inside one read bracket.
            dataModel.BeginRead();
            try
            {
                IDocument flattened = project.DM_DocumentFlattened()
                    ?? throw new UserException("The project has no flattened document. Does it contain a schematic sheet?");

                netCount = flattened.DM_NetCount();
                report.Add($"Project: {project.DM_ProjectFullPath()}");
                report.Add($"Number of nets: {netCount}");
                report.Add("");

                for (int i = 0; i < netCount; i++)
                {
                    INet net = flattened.DM_Nets(i);
                    if (net == null)
                        continue;
                    if (i % 50 == 0)
                        ctx.Log($"Net {i + 1}/{netCount}");

                    string netName = net.DM_NetName();

                    // General net properties.
                    report.Add($"Net number = {net.DM_NetNumber()}");
                    report.Add($"  Net name = {netName}");
                    report.Add($"  Full net name = {net.DM_FullNetName()}");
                    report.Add($"  Electrical property = {net.DM_ElectricalString()}");
                    report.Add($"  Signal type = {net.DM_SignalType()}");
                    report.Add($"  Calculated net name = {net.DM_CalculatedNetName()}");
                    report.Add($"  Hidden net name = {net.DM_HiddenNetName()}");

                    // The three item lists of a net, each a count plus an indexed accessor.
                    int all = ReportItems(report, dataModel, "Net item", net.DM_AllNetItemCount(), net.DM_AllNetItems, totals);
                    int removed = ReportItems(report, dataModel, "Removed net item", net.DM_RemovedNetItemCount(), net.DM_RemovedNetItems, totals);
                    int harness = ReportItems(report, dataModel, "Removed harness entry", net.DM_RemovedHarnessEntryCount(), net.DM_RemovedHarnessEntries, totals);
                    report.Add("");

                    totals.All += all;
                    totals.Removed += removed;
                    totals.Harness += harness;
                    if (i < NetsInSummary)
                        summary.AppendLine($"  {netName}: {all} item(s), {removed} removed, {harness} removed harness entries");
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            Directory.CreateDirectory(outFolder);
            string fileName = Path.Combine(outFolder, "NetItemReport_" + Path.GetFileNameWithoutExtension(project.DM_ProjectFileName()) + ".txt");
            File.WriteAllLines(fileName, report);

            if (open)
            {
                IClient client = DXP.GlobalVars.Client;
                IServerDocument reportDocument = client.OpenDocument("Text", fileName);
                if (reportDocument != null)
                    client.ShowDocument(reportDocument);
            }

            var result = new StringBuilder();
            if (!compiled)
                result.AppendLine("Warning: DM_Compile returned false; the nets may be out of date.");
            result.AppendLine($"{project.DM_ProjectFileName()}: {netCount} nets, {totals.All} net items, {totals.Removed} removed net items, " +
                              $"{totals.Harness} removed harness entries; {totals.Resolved} resolved to schematic objects, {totals.Unresolved} not resolved.");
            if (totals.Unresolved > 0)
                result.AppendLine("  Handles resolve only for sheets open in an editor: open the project's sheets to resolve all items.");
            result.Append(summary);
            if (netCount > NetsInSummary)
                result.AppendLine($"  ... {netCount - NetsInSummary} more nets in the report.");
            result.Append("Report: " + fileName);
            return result.ToString();
        }

        /// <summary>Appends one line per item of a net's item list and returns the number of items in it.</summary>
        private static int ReportItems(List<string> report, IUnifiedDataModel dataModel, string label, int count,
                                       Func<int, INetItem> itemAt, Totals totals)
        {
            for (int m = 0; m < count; m++)
            {
                INetItem item = itemAt(m);
                if (item == null)
                    continue;
                string identifier = Identifier(dataModel, item.DM_SchHandle());
                if (identifier == null)
                {
                    totals.Unresolved++;
                    // SupportsInterface returns false for items on sheets that are not open in an
                    // editor, so the line names the sheet to tell the user which documents to open.
                    report.Add($"    {label}: {item.DM_ObjectKindString()} (no schematic object, {item.DM_OwnerDocumentName()})");
                }
                else
                {
                    totals.Resolved++;
                    report.Add($"    {label}: {item.DM_ObjectKindString()} \"{identifier}\"");
                }
            }
            return count;
        }

        /// <summary>
        /// Resolves a schematic handle (INetItem.DM_SchHandle) to the live schematic object and returns its identifier
        /// string, or null when the handle does not resolve.
        /// </summary>
        private static string Identifier(IUnifiedDataModel dataModel, string schHandle)
        {
            if (string.IsNullOrEmpty(schHandle))
                return null;
            if (!dataModel.SupportsInterface(schHandle, out object obj) || obj == null)
                return null;

            // The cast to ISch_BasicContainer always succeeds (SDK interfaces are IDispatch, see "Interfaces at run
            // time"): the call fails only if the object is not a schematic object.
            var container = (ISch_BasicContainer)obj;
            try
            {
                return container.GetState_IdentifierString();
            }
            catch (COMException)
            {
                return null;
            }
        }

        private sealed class Totals
        {
            public int All, Removed, Harness, Resolved, Unresolved;
        }
    }
}
