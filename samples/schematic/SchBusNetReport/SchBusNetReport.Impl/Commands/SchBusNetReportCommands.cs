using System;
using System.Collections.Generic;
using System.IO;
using DXP; // IClient.GetDXPWorkspace and the IClient.OpenDocument/ShowDocument helpers live in namespace DXP
using EDP; // the DM_* data model: workspace, project, document, bus, net

namespace AdSamples.SchBusNetReport
{
    /// <summary>
    /// Walks the data model of every open project (workspace -> project -> physical document -> bus -> net)
    /// and lists, for each schematic sheet, its buses with the net name of each of the bus's wires.
    /// Altium Designer already populates the buses and nets of a project it has open (Compile=false gives the
    /// same output), so a compile is not a precondition. The sample still compiles each project by default to
    /// refresh the model; pass Compile=false to skip it.
    /// </summary>
    internal static class SchBusNetReportCommands
    {
        /// <summary>
        /// GetBusInfo "Compile=true|false|Out=&lt;file or folder&gt;|Open=true": returns the report as text. Out also
        /// writes it to a file; Open writes it (to a temporary file when Out is not given) and shows it in Altium
        /// Designer.
        /// </summary>
        [HotCommand("GetBusInfo", "List the compiled buses of every schematic and their member nets")]
        public static string GetBusInfo(CommandContext ctx)
        {
            string compileValue = ctx.Get("Compile", "true");
            bool compile = !(compileValue.Equals("false", StringComparison.OrdinalIgnoreCase) || compileValue == "0");

            IWorkspace workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");

            int projectCount = workspace.DM_ProjectCount();
            // An explicit error is clearer than an empty report.
            if (projectCount == 0)
                throw new UserException("No projects are open. Open a project with schematic sheets first.");

            var report = new List<string>();
            int sheetCount = 0, busCount = 0, wireCount = 0;

            for (int i = 0; i < projectCount; i++)
            {
                // Every data-model object below is null-checked.
                IProject project = workspace.DM_Projects(i);
                if (project == null)
                {
                    report.Add($"Project #{i}: <not available>");
                    report.Add("");
                    continue;
                }

                string compileState;
                if (compile)
                {
                    ctx.Log($"Compiling {ProjectName(project)}");
                    // DM_Compile returns a bool. What false means exactly is not
                    // documented, so it is reported, not hidden.
                    compileState = project.DM_Compile() ? "compiled" : "DM_Compile returned false, the data may be stale";
                }
                else
                {
                    compileState = "not compiled (Compile=false)";
                }
                report.Add($"Project #{i}: {ProjectName(project)} ({compileState})");

                int documentCount = project.DM_PhysicalDocumentCount();
                for (int k = 0; k < documentCount; k++)
                {
                    IDocument document = project.DM_PhysicalDocuments(k);
                    if (document == null || !string.Equals(document.DM_DocumentKind(), "SCH", StringComparison.OrdinalIgnoreCase))
                        continue;

                    sheetCount++;
                    report.Add("  Document FileName " + document.DM_FileName());
                    report.Add("  Document Fullpath " + document.DM_FullPath());

                    int buses = document.DM_BusCount();
                    report.Add("     Number of Buses for this document: " + buses);
                    for (int l = 0; l < buses; l++)
                    {
                        IBus bus = document.DM_Buses(l);
                        if (bus == null)
                        {
                            report.Add($"     Bus #{l}: <not available>");
                            continue;
                        }

                        busCount++;
                        report.Add("     Bus Name = " + bus.DM_BusName());

                        // DM_Wires returns EDP.INet: the bus's wires are reported by their net names.
                        int wires = bus.DM_WireCount();
                        report.Add("     Number of Wires for this Bus: " + wires);
                        for (int m = 0; m < wires; m++)
                        {
                            INet net = bus.DM_Wires(m);
                            wireCount++;
                            report.Add("     Wire Net Name = " + (net == null ? "<not available>" : net.DM_NetName()));
                        }
                        report.Add("");
                    }
                    report.Add("");
                }
                report.Add("");
            }

            report.Insert(0, $"{projectCount} projects, {sheetCount} schematic documents, {busCount} buses, {wireCount} bus wires.");

            string text = string.Join(Environment.NewLine, report).TrimEnd();
            string written = WriteReport(ctx, report);
            return written == null ? text : text + Environment.NewLine + "Report written to " + written;
        }

        /// <summary>Writes the report when Out or Open is given; returns the path, or null.</summary>
        private static string WriteReport(CommandContext ctx, List<string> report)
        {
            string path = ctx.Get("Out");
            bool open = ctx.Flag("Open");
            if (string.IsNullOrEmpty(path) && !open)
                return null;

            const string fileName = "SchBusNetReport.txt";
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), fileName);
            else if (Directory.Exists(path))
                path = Path.Combine(path, fileName);
            path = Path.GetFullPath(path);
            File.WriteAllLines(path, report);

            if (open)
            {
                DXP.IClient client = DXP.GlobalVars.Client;
                var document = client.OpenDocument("Text", path);
                if (document != null)
                    client.ShowDocument(document);
            }
            return path;
        }

        private static string ProjectName(IProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
