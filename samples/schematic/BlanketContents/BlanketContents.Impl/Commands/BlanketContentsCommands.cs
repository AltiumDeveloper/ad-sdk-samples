using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;
using SCH;

namespace AdSamples.BlanketContents
{
    /// <summary>
    /// Reports what each blanket of the focused schematic sheet holds. Blankets are part of the compiled project
    /// data model (EDP.IBlanket): each one lists the lines, sheet symbols and net items it covers. Every item carries
    /// a schematic handle (DM_SchHandle) that the unified data model resolves to the live SCH object.
    /// </summary>
    internal static class BlanketContentsCommands
    {
        /// <summary>IDocument.DM_DocumentKind of a schematic sheet, as the reference states it.</summary>
        private const string SchematicKind = "SCH";

        /// <summary>
        /// ShowBlanket "Compile=true|false": returns a count per blanket of the resolved objects.
        /// </summary>
        [HotCommand("ShowBlanket", "Report the items inside each blanket of the focused schematic")]
        public static string ShowBlanket(CommandContext ctx)
        {
            string compileMode = ctx.Get("Compile", "true");
            bool compileAllowed = !string.Equals(compileMode, "false", StringComparison.OrdinalIgnoreCase);
            // Compile=force: compile even when DM_NeedsCompile says the project is up to date.
            // DM_NeedsCompile can return false for a project that has never been compiled in the session and whose
            // sheet has just been edited, so the blanket is missing from the data model.
            bool compileForced = string.Equals(compileMode, "force", StringComparison.OrdinalIgnoreCase);

            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its sheets.");

            // Blankets exist only in the compiled data model. Compile only when the project says it is out of date.
            // Compiling changes the in-memory model only; nothing is saved.
            string compileNote = "";
            bool needsCompile = project.DM_NeedsCompile();
            if (needsCompile || compileForced)
            {
                if (compileAllowed)
                {
                    if (!needsCompile)
                        ctx.Log("DM_NeedsCompile is false; Compile=force compiles anyway.");
                    ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                    compileNote = project.DM_Compile() ? " (project compiled)" : " (DM_Compile returned false; the model may be out of date)";
                }
                else
                {
                    compileNote = " (project needs a compile; Compile=false, so the model may be out of date)";
                }
            }

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            var report = new List<string>();
            string sheetName, documentKind;
            int blanketCount;

            // Every access to the compiled model, including resolving handles, stays inside one read bracket.
            dataModel.BeginRead();
            try
            {
                IDocument document = workspace.DM_FocusedDocument()
                    ?? throw new UserException("No document is focused. Focus a schematic sheet of the project.");
                sheetName = document.DM_FileName();
                documentKind = document.DM_DocumentKind();

                // The .NET API has no cDocKind_SCH constant; the reference of IDocument.DM_DocumentKind gives 'SCH'
                // for a schematic document. The comparison is case-insensitive.
                if (!string.Equals(documentKind, SchematicKind, StringComparison.OrdinalIgnoreCase))
                    throw new UserException($"The focused document '{sheetName}' (kind '{documentKind}') is not a schematic sheet (kind 'SCH'). Focus a .SchDoc.");

                blanketCount = document.DM_BlanketCount();
                for (int i = 0; i < blanketCount; i++)
                {
                    IBlanket blanket = document.DM_Blankets(i);
                    if (blanket == null)
                    {
                        report.Add($"  Blanket {i + 1}: (null)");
                        continue;
                    }
                    report.Add($"  Blanket {i + 1}:");

                    // Lines: wires and buses are told apart by their SCH object ID.
                    var lines = new Tally();
                    for (int j = 0; j < blanket.DM_LineCount(); j++)
                        lines.Add(Resolve(dataModel, blanket.DM_Lines(j)));
                    report.Add($"    Lines: {lines.Total} ({lines.Count(TObjectId.eWire)} wires, {lines.Count(TObjectId.eBus)} buses, " +
                               $"{lines.Other(TObjectId.eWire, TObjectId.eBus)} other, {lines.Unresolved} not resolved){lines.Kinds()}");

                    var sheetSymbols = new Tally();
                    for (int j = 0; j < blanket.DM_SheetSymbolCount(); j++)
                        sheetSymbols.Add(Resolve(dataModel, blanket.DM_SheetSymbols(j)));
                    report.Add($"    Sheet symbols: {sheetSymbols.Total} ({sheetSymbols.Resolved} resolved, {sheetSymbols.Unresolved} not resolved){sheetSymbols.Kinds()}");

                    var netItems = new Tally();
                    for (int j = 0; j < blanket.DM_NetItemCount(); j++)
                        netItems.Add(Resolve(dataModel, blanket.DM_NetItems(j)));
                    report.Add($"    Net items: {netItems.Total} ({netItems.Resolved} resolved, {netItems.Unresolved} not resolved){netItems.Kinds()}");
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            string summary = $"{sheetName} (kind '{documentKind}'): {blanketCount} blanket(s){compileNote}.";
            if (blanketCount == 0)
                summary += " Place a blanket (or run AddTestBlanket) and compile the project.";
            report.Insert(0, summary);
            return string.Join(Environment.NewLine, report);
        }

        /// <summary>
        /// AddTestBlanket "Rect=x1,y1,x2,y2|Margin=100": test setup. Places a rectangular blanket
        /// on the current sheet, by default around all of its wires plus a margin (mils), so ShowBlanket has something
        /// to report on a sheet without blankets. Close the document without saving to discard it.
        /// </summary>
        [HotCommand("AddTestBlanket", "Place a blanket around the wires of the current sheet (test setup)")]
        public static string AddTestBlanket(CommandContext ctx)
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("The active document is not a schematic. Open a .SchDoc first.");
            if (sheet.GetState_ObjectId() != TObjectId.eSheet)
                throw new UserException("The active document is a schematic library, not a sheet. Open a .SchDoc.");

            (int left, int bottom, int right, int top) = BlanketRectangle(ctx, sheet);

            // PreProcess/PostProcess and SCHMPrimitiveRegistration, as the docs example IProcessControl.PreProcess does
            // for a new port. Which robot message a new object needs is not documented.
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            ISch_Blanket blanket;
            processControl.PreProcess(sheet, "");
            try
            {
                blanket = schServer.SchObjectFactory(TObjectId.eBlanket, TObjectCreationMode.eCreate_GlobalCopy) as ISch_Blanket
                    ?? throw new UserException("SchObjectFactory did not create a blanket.");

                // A blanket is a polygon: four corners. The reference does not say where ISch_Polygon.Vertex starts;
                // ISch_Polygon.Vertex is 1-based; the corners are read back below.
                blanket.SetState_VerticesCount(4);
                blanket.SetState_Vertex(1, Point(left, bottom));
                blanket.SetState_Vertex(2, Point(right, bottom));
                blanket.SetState_Vertex(3, Point(right, top));
                blanket.SetState_Vertex(4, Point(left, top));

                sheet.RegisterSchObjectInContainer(blanket);
                schServer.GetState_RobotManager().SendMessage(sheet, SCHConstant.BroadCast,
                    SCHConstant.SCHMPrimitiveRegistration, blanket);
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }
            sheet.GraphicallyInvalidate();

            var corners = new List<string>();
            for (int i = 1; i <= blanket.GetState_VerticesCount(); i++)
            {
                DXP.Point p = blanket.GetState_Vertex(i);
                corners.Add($"({Mils(p.X)},{Mils(p.Y)})");
            }
            return $"Placed a blanket on {Path.GetFileName(sheet.GetState_DocumentName())}: {blanket.GetState_VerticesCount()} vertices " +
                   $"{string.Join(" ", corners)} mil. Compile the project, then run ShowBlanket (a blanket made through " +
                   "the SCH API may not be reported by the compiled model; one placed in the editor is). Close without saving to discard it.";
        }

        /// <summary>Rect parameter (mils), or the union of all wires' bounding rectangles grown by Margin (mils).</summary>
        private static (int Left, int Bottom, int Right, int Top) BlanketRectangle(CommandContext ctx, ISch_Document sheet)
        {
            string rect = ctx.Get("Rect");
            if (!string.IsNullOrWhiteSpace(rect))
            {
                double[] v = rect.Split(',').Select(s => ParseMil("Rect", s)).ToArray();
                if (v.Length != 4)
                    throw new UserException("Rect must be x1,y1,x2,y2 in mils, for example Rect=1000,1000,3000,2500.");
                return (EDP.Utils.MilsToCoord(Math.Min(v[0], v[2])), EDP.Utils.MilsToCoord(Math.Min(v[1], v[3])),
                        EDP.Utils.MilsToCoord(Math.Max(v[0], v[2])), EDP.Utils.MilsToCoord(Math.Max(v[1], v[3])));
            }

            int margin = EDP.Utils.MilsToCoord(ParseMil("Margin", ctx.Get("Margin", "100")));
            int left = int.MaxValue, bottom = int.MaxValue, right = int.MinValue, top = int.MinValue, wires = 0;
            ISch_Iterator iterator = sheet.SchIterator_Create();
            try
            {
                var objectSet = new SCH.TObjectSet();
                objectSet.Add(TObjectId.eWire);
                iterator.AddFilter_ObjectSet(objectSet);
                for (var wire = iterator.FirstSchObject() as ISch_Wire; wire != null; wire = iterator.NextSchObject() as ISch_Wire)
                {
                    SCH.CoordRect r = wire.BoundingRectangle();
                    left = Math.Min(left, Math.Min(r.X1, r.X2));
                    right = Math.Max(right, Math.Max(r.X1, r.X2));
                    bottom = Math.Min(bottom, Math.Min(r.Y1, r.Y2));
                    top = Math.Max(top, Math.Max(r.Y1, r.Y2));
                    wires++;
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            if (wires == 0)
                throw new UserException("The sheet has no wires. Pass Rect=x1,y1,x2,y2 (mils).");
            return (left - margin, bottom - margin, right + margin, top + margin);
        }

        /// <summary>
        /// Resolves an item's schematic handle to its SCH object ID, or null when the handle is empty, does not
        /// resolve, or the object does not answer as an ISch_BasicContainer. Such items are counted as not resolved.
        /// </summary>
        private static TObjectId? Resolve(IUnifiedDataModel dataModel, IDMObject item)
        {
            string handle = item?.DM_SchHandle();
            if (string.IsNullOrEmpty(handle))
                return null;
            if (!dataModel.SupportsInterface(handle, out object obj) || obj == null)
                return null;

            // A cast to an SDK interface always succeeds (docs: Interfaces at run time); a missing interface shows up
            // as a COMException on the first call, which counts as "not resolved".
            try
            {
                return ((ISch_BasicContainer)obj).GetState_ObjectId();
            }
            catch (COMException)
            {
                return null;
            }
        }

        private static DXP.Point Point(int x, int y) => new DXP.Point(new SPoint { X = x, Y = y });

        private static string Mils(int coord) => EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);

        private static double ParseMil(string name, string text) =>
            double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : throw new UserException($"{name}: '{text}' is not a number of mils.");

        /// <summary>Counts resolved items by SCH object ID.</summary>
        private sealed class Tally
        {
            private readonly SortedDictionary<string, int> _byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<TObjectId, int> _byId = new Dictionary<TObjectId, int>();

            public int Total { get; private set; }
            public int Unresolved { get; private set; }
            public int Resolved => Total - Unresolved;

            public void Add(TObjectId? id)
            {
                Total++;
                if (id == null)
                {
                    Unresolved++;
                    return;
                }
                _byId[id.Value] = Count(id.Value) + 1;
                string kind = id.Value.ToString();
                _byKind[kind] = _byKind.TryGetValue(kind, out int n) ? n + 1 : 1;
            }

            public int Count(TObjectId id) => _byId.TryGetValue(id, out int n) ? n : 0;

            public int Other(params TObjectId[] known) => Resolved - known.Sum(Count);

            /// <summary>" [eWire=3, eBus=1]", or "" when nothing resolved.</summary>
            public string Kinds()
            {
                if (_byKind.Count == 0)
                    return "";
                var text = new StringBuilder(" [");
                text.Append(string.Join(", ", _byKind.Select(p => p.Key + "=" + p.Value)));
                return text.Append(']').ToString();
            }
        }
    }
}
