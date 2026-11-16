using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DXP; // IClient.OpenDocument/ShowDocument extension methods
using EDP;
using SCH;

namespace AdSamples.NetWireVertices
{
    /// <summary>
    /// Compiles the focused project and lists the vertices of every schematic wire of a named net. The lesson is the
    /// bridge from the compiled netlist back to the live schematic: a net's ILine carries a schematic handle
    /// (DM_SchHandle) that IUnifiedDataModel.SupportsInterface resolves to the ISch_Wire on the sheet.
    /// </summary>
    internal static class NetWireVerticesCommands
    {
        /// <summary>How many net names the listing mode (no Net parameter) returns.</summary>
        private const int NetsInListing = 40;

        /// <summary>
        /// GetWireVertices "Net=&lt;name&gt;|Out=&lt;file or folder&gt;|Open=true|Base=1": the vertices of the net's wires.
        /// Without Net, lists the nets of the flattened document that have lines.
        /// </summary>
        [HotCommand("GetWireVertices", "List the vertices of the wires of a net")]
        public static string GetWireVertices(CommandContext ctx)
        {
            string netName = ctx.Get("Net");
            int vertexBase = VertexBase(ctx);

            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its documents.");

            // Nets and lines exist only in the compiled model, so compile first.
            ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
            bool compiled = project.DM_Compile();

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            var report = new List<string>();
            var counts = new Counts();
            var netsWithLines = new List<string>();

            // One read bracket around all access to the compiled model, including the handle lookups (the same
            // single bracket as NetItemReport).
            dataModel.BeginRead();
            try
            {
                IDocument flattened = project.DM_DocumentFlattened()
                    ?? throw new UserException("The project has no flattened document. Does it contain a schematic sheet?");

                int netCount = flattened.DM_NetCount();
                for (int i = 0; i < netCount; i++)
                {
                    INet net = flattened.DM_Nets(i);
                    if (net == null)
                        continue;
                    string name = net.DM_NetName();
                    int lineCount = net.DM_LineCount();

                    if (string.IsNullOrEmpty(netName))
                    {
                        if (lineCount > 0)
                            netsWithLines.Add($"{name} ({lineCount} line(s))");
                        continue;
                    }

                    // The net name is matched case-insensitively.
                    // A net can appear more than once in the flattened document; every match is listed.
                    if (!string.Equals(name, netName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    counts.Nets++;
                    report.Add($"Net {name} ({net.DM_FullNetName()}): {lineCount} line(s)");
                    for (int j = 0; j < lineCount; j++)
                    {
                        ILine line = net.DM_Lines(j);
                        if (line == null)
                            continue;
                        counts.Lines++;
                        ReportLine(report, dataModel, line, j, vertexBase, counts);
                    }
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            string warning = compiled ? "" : "Warning: DM_Compile returned false; the nets may be out of date." + Environment.NewLine;

            if (string.IsNullOrEmpty(netName))
            {
                string list = string.Join(", ", netsWithLines.Take(NetsInListing));
                string more = netsWithLines.Count > NetsInListing ? $", ... {netsWithLines.Count - NetsInListing} more" : "";
                return warning + $"{project.DM_ProjectFileName()}: {netsWithLines.Count} nets with lines. " +
                       $"Pass Net=<name> to list a net's wire vertices. Nets: {list}{more}";
            }

            if (counts.Nets == 0)
                throw new UserException($"No net named '{netName}' in {project.DM_ProjectFileName()}. " +
                                        "Run GetWireVertices without Net to list the nets.");

            report.Insert(0, $"{project.DM_ProjectFileName()}: net '{netName}' matched {counts.Nets} time(s), {counts.Lines} line(s), " +
                             $"{counts.Wires} wire(s) with {counts.Vertices} vertices, {counts.Unresolved} line(s) not resolved, " +
                             $"{counts.Other} not a wire; vertex index {vertexBase}..VerticesCount{(vertexBase == 0 ? "-1" : "")}.");
            if (counts.Unresolved > 0)
                report.Insert(1, "  Handles resolve only for sheets open in an editor: open the project's sheets to resolve all lines.");

            string text = warning + string.Join(Environment.NewLine, report);
            string written = WriteReport(ctx, netName, report);
            return written == null ? text : text + Environment.NewLine + "Report written to " + written;
        }

        /// <summary>Resolves one netlist line to its schematic wire and appends the wire's vertices.</summary>
        private static void ReportLine(List<string> report, IUnifiedDataModel dataModel, ILine line, int index,
                                       int vertexBase, Counts counts)
        {
            // The netlist line's own coordinates (the reference: lower left and upper right), for comparison with the
            // wire's vertices, in the same internal units as the vertices.
            string ends = $"line box ({Mm(line.DM_LX())}, {Mm(line.DM_LY())}) - ({Mm(line.DM_HX())}, {Mm(line.DM_HY())})";

            string handle = line.DM_SchHandle();
            if (string.IsNullOrEmpty(handle) || !dataModel.SupportsInterface(handle, out object obj) || obj == null)
            {
                counts.Unresolved++;
                report.Add($"  Line{index + 1}: no schematic object ({line.DM_OwnerDocumentName()}), {ends}");
                return;
            }

            // A cast to an SDK interface always succeeds (they are dispatch interfaces), so the kind is checked with
            // ObjectId. A non-schematic object fails on the first call: treat it as not a wire.
            var container = (ISch_BasicContainer)obj;
            TObjectId kind;
            try
            {
                kind = container.GetState_ObjectId();
            }
            catch (COMException)
            {
                counts.Other++;
                report.Add($"  Line{index + 1}: not a schematic object ({line.DM_OwnerDocumentName()})");
                return;
            }
            if (kind != TObjectId.eWire)
            {
                counts.Other++;
                report.Add($"  Line{index + 1}: {kind}, not a wire ({line.DM_OwnerDocumentName()})");
                return;
            }

            var wire = (ISch_Wire)container;
            int vertexCount = wire.GetState_VerticesCount();
            counts.Wires++;
            // Numbered by the line index in the net, so skipped lines leave gaps.
            report.Add($"  Wire{index + 1} ({line.DM_OwnerDocumentName()}): {vertexCount} vertices, {ends}");

            // The reference does not give the index base of Vertex. The docs' ISch_Wire example reads
            // 1..VerticesCount, which is the valid range. Base selects the range (default 1; 0 reads 0..VerticesCount-1).
            int last = vertexBase == 0 ? vertexCount - 1 : vertexCount;
            for (int k = vertexBase; k <= last; k++)
            {
                DXP.Point vertex = wire.GetState_Vertex(k);
                counts.Vertices++;
                report.Add($"    [{k}] X = {Mm(vertex.X)} Y = {Mm(vertex.Y)}");
            }
        }

        private static int VertexBase(CommandContext ctx)
        {
            string value = ctx.Get("Base", "1");
            return value switch
            {
                "1" => 1,
                "0" => 0,
                _ => throw new UserException($"Base must be 0 or 1, not '{value}'."),
            };
        }

        /// <summary>
        /// Coordinate in millimetres with the raw internal value. The scripting helper CoordUnitToString(coord, eMetric)
        /// has no .NET counterpart, so the text is formatted here.
        /// </summary>
        private static string Mm(int coord) =>
            EDP.Utils.CoordToMMs(coord).ToString("0.####", CultureInfo.InvariantCulture) + "mm (" + coord + ")";

        /// <summary>Writes the report when Out or Open is given; returns the path, or null.</summary>
        private static string WriteReport(CommandContext ctx, string netName, List<string> report)
        {
            string path = ctx.Get("Out");
            bool open = ctx.Flag("Open");
            if (string.IsNullOrEmpty(path) && !open)
                return null;

            string fileName = "NetWireVertices_" + string.Concat(netName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".txt";
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), fileName);
            else if (Directory.Exists(path))
                path = Path.Combine(path, fileName);
            path = Path.GetFullPath(path);
            File.WriteAllLines(path, report);

            if (open)
            {
                // Show the list in AD's text editor.
                IClient client = DXP.GlobalVars.Client;
                IServerDocument document = client.OpenDocument("Text", path);
                if (document != null)
                    client.ShowDocument(document);
            }
            return path;
        }

        private sealed class Counts
        {
            public int Nets, Lines, Wires, Vertices, Unresolved, Other;
        }
    }
}
