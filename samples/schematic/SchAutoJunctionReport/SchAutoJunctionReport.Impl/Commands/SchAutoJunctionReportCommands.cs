using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DXP;
using EDP;
using SCH;

namespace AdSamples.SchAutoJunctionReport
{
    /// <summary>
    /// Lists the junctions Altium Designer places automatically on the focused schematic sheet, with their net names.
    /// Two techniques meet here: the sheet's wire connection points (ISch_Sheet.WireConnections, the points where
    /// AD draws junction dots), and the compiled netlist, cross-referenced with the live wire by its handle
    /// (ILine.DM_SchHandle = ISch_BasicContainer.Handle).
    /// </summary>
    internal static class SchAutoJunctionReportCommands
    {
        /// <summary>IDocument.DM_DocumentKind of a schematic sheet.</summary>
        private const string SchematicKind = "SCH";

        /// <summary>A connection point needs this many objects to need a junction.</summary>
        private const int DefaultThreshold = 3;

        /// <summary>
        /// GetAllAutoJunctions "Threshold=3|Compile=true": every wire connection of the focused sheet with at least
        /// Threshold objects and no manual junction, with its location and, when found, its net name.
        /// </summary>
        [HotCommand("GetAllAutoJunctions", "List auto-placed junctions of the current sheet with their net names")]
        public static string GetAllAutoJunctions(CommandContext ctx)
        {
            int threshold = Threshold(ctx);
            // Compile is on unless explicitly turned off (ctx.Flag would treat a missing parameter as false).
            bool compileAllowed = !string.Equals(ctx.Get("Compile", "true"), "false", StringComparison.OrdinalIgnoreCase);

            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its sheets.");

            // Net names exist only in the compiled model. Compile only when the project says it is out of date.
            // Compiling changes the in-memory model only; nothing is saved.
            string compileNote = "";
            if (project.DM_NeedsCompile())
            {
                if (compileAllowed)
                {
                    ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                    compileNote = project.DM_Compile() ? " Project compiled." : " DM_Compile returned false; net names may be out of date.";
                }
                else
                {
                    compileNote = " The project needs a compile (Compile=false): net names may be out of date.";
                }
            }

            IDocument focused = workspace.DM_FocusedDocument()
                ?? throw new UserException("No document is focused. Focus a schematic sheet of the project.");
            if (!string.Equals(focused.DM_DocumentKind(), SchematicKind, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The focused document '{focused.DM_FileName()}' is not a schematic sheet. Focus a .SchDoc.");

            // From the data-model document to the live schematic document, as the docs' IConnection example does.
            string path = focused.DM_FullPath();
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document schDoc = schServer.GetSchDocumentByPath(path)
                ?? throw new UserException($"'{Path.GetFileName(path)}' is not open in the schematic editor. Open it first.");
            // Cast only after testing ObjectId.
            if (schDoc.GetState_ObjectId() != TObjectId.eSheet)
                throw new UserException($"'{Path.GetFileName(path)}' is not a schematic sheet.");
            var sheet = (ISch_Sheet)schDoc;

            List<WireInfo> wires = CollectWires(schDoc);
            Dictionary<string, string> netByHandle = NetNamesByLineHandle(project, path, focused);

            IConnectionsArray connections = sheet.GetState_WireConnections();
            int connectionCount = connections.GetState_ConnectionsCount();
            int manual = 0, belowThreshold = 0, withNet = 0, viaSegment = 0, noWire = 0;
            var lines = new List<string>();

            for (int i = 0; i < connectionCount; i++)
            {
                IConnection connection = connections.GetState_Connection(i);
                if (connection == null)
                    continue;
                if (connection.GetState_ObjectsCount() < threshold)
                {
                    belowThreshold++;
                    continue;
                }
                if (connection.GetState_IsManualJunction())
                {
                    manual++;
                    continue;
                }

                DXP.Point location = connection.GetState_Location();
                string line = $"X:{location.X}, Y:{location.Y} ({Mils(location.X)}, {Mils(location.Y)})";

                WireInfo wire = FindWire(wires, location.X, location.Y, out bool onSegment);
                if (wire == null)
                {
                    noWire++;
                    line += ", no wire found at this point";
                }
                else if (netByHandle.TryGetValue(wire.Handle, out string netName))
                {
                    withNet++;
                    if (onSegment)
                        viaSegment++;
                    line += ", NetName: " + netName;
                }
                else
                {
                    line += ", wire not in a compiled net of this sheet";
                }
                lines.Add(line);
            }

            int found = lines.Count;
            string summary = found == 0
                ? $"{Path.GetFileName(path)}: no auto-placed junctions"
                : $"{Path.GetFileName(path)}: {found} auto-placed junction(s), {withNet} with a net name";
            summary += $" ({connectionCount} wire connections: {belowThreshold} with fewer than {threshold} objects, " +
                       $"{manual} with a manual junction; {wires.Count} wires, {netByHandle.Count} wire handles in the compiled nets).";
            if (viaSegment > 0)
                summary += $" {viaSegment} net name(s) found through a wire segment, not a vertex.";
            if (noWire > 0)
                summary += $" {noWire} junction(s) without a wire at the point.";
            summary += compileNote;
            summary += " Coordinates: schematic internal units (mils in brackets).";

            lines.Insert(0, summary);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>A wire's handle and vertices, read once instead of once per junction.</summary>
        private sealed class WireInfo
        {
            public string Handle;
            public readonly List<(int X, int Y)> Vertices = new List<(int X, int Y)>();
        }

        private static List<WireInfo> CollectWires(ISch_Document schDoc)
        {
            var wires = new List<WireInfo>();
            ISch_Iterator iterator = schDoc.SchIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eWire });
                for (var wire = iterator.FirstSchObject() as ISch_Wire; wire != null; wire = iterator.NextSchObject() as ISch_Wire)
                {
                    var info = new WireInfo { Handle = wire.GetState_Handle() ?? "" };
                    // Vertex is 1-based: the docs' ISch_Wire example also reads 1..VerticesCount.
                    int count = wire.GetState_VerticesCount();
                    for (int k = 1; k <= count; k++)
                    {
                        DXP.Point vertex = wire.GetState_Vertex(k);
                        info.Vertices.Add((vertex.X, vertex.Y));
                    }
                    wires.Add(info);
                }
            }
            finally
            {
                schDoc.SchIterator_Destroy(ref iterator);
            }
            return wires;
        }

        /// <summary>
        /// The first wire with a vertex at the point. When none has one, as for a T on the middle of a straight
        /// segment, the first wire whose segment passes through the point.
        /// </summary>
        private static WireInfo FindWire(List<WireInfo> wires, int x, int y, out bool onSegment)
        {
            onSegment = false;
            foreach (WireInfo wire in wires)
                foreach (var v in wire.Vertices)
                    if (v.X == x && v.Y == y)
                        return wire;

            foreach (WireInfo wire in wires)
                for (int k = 1; k < wire.Vertices.Count; k++)
                    if (OnSegment(wire.Vertices[k - 1], wire.Vertices[k], x, y))
                    {
                        onSegment = true;
                        return wire;
                    }
            return null;
        }

        /// <summary>True when (x, y) lies exactly on the segment a-b (collinear and inside its bounding box).</summary>
        private static bool OnSegment((int X, int Y) a, (int X, int Y) b, int x, int y)
        {
            long cross = (long)(b.X - a.X) * (y - a.Y) - (long)(b.Y - a.Y) * (x - a.X);
            return cross == 0
                && x >= Math.Min(a.X, b.X) && x <= Math.Max(a.X, b.X)
                && y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y);
        }

        /// <summary>
        /// Maps the schematic handle of every netlist line of the sheet to its net name, built once instead of
        /// searching the nets once per junction. The sheet's logical document in the focused project is used, falling
        /// back to the focused document. The first net wins.
        /// </summary>
        private static Dictionary<string, string> NetNamesByLineHandle(IProject project, string path, IDocument focused)
        {
            // Handles are compared case-insensitively.
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IUnifiedDataModel dataModel = project.DM_DataModel();
            dataModel?.BeginRead();
            try
            {
                IDocument document = FindLogicalDocument(project, path) ?? focused;
                int netCount = document.DM_NetCount();
                for (int i = 0; i < netCount; i++)
                {
                    INet net = document.DM_Nets(i);
                    if (net == null)
                        continue;
                    string netName = net.DM_NetName();
                    int lineCount = net.DM_LineCount();
                    for (int j = 0; j < lineCount; j++)
                    {
                        string handle = net.DM_Lines(j)?.DM_SchHandle();
                        if (!string.IsNullOrEmpty(handle) && !map.ContainsKey(handle))
                            map[handle] = netName;
                    }
                }
            }
            finally
            {
                dataModel?.EndRead();
            }
            return map;
        }

        private static IDocument FindLogicalDocument(IProject project, string path)
        {
            int count = project.DM_LogicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDocument document = project.DM_LogicalDocuments(i);
                if (document != null && string.Equals(document.DM_FullPath(), path, StringComparison.OrdinalIgnoreCase))
                    return document;
            }
            return null;
        }

        private static int Threshold(CommandContext ctx)
        {
            string value = ctx.Get("Threshold", DefaultThreshold.ToString(CultureInfo.InvariantCulture));
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold) || threshold < 1)
                throw new UserException($"Threshold must be a positive integer, not '{value}'.");
            return threshold;
        }

        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture) + " mil";
    }
}
