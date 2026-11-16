using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // IClient.LastActiveDocumentOfType / ShowDocument are extension methods in namespace DXP
using PCB;
using SCH;
using TObjectId = PCB.TObjectId; // SCH has its own TObjectId and TObjectCreationMode: this file names the SCH ones in full
using TObjectCreationMode = PCB.TObjectCreationMode;

namespace AdSamples.ComponentManagerPlaceComponent
{
    /// <summary>
    /// Places a component that lives on a component management server: download its library file(s) over HTTP,
    /// read the component's metadata from the response's Content-Disposition header, place the symbol
    /// (IIntegratedLibraryManager.PlaceLibraryComponent) or footprint (IPCB_Component.LoadFromLibrary), and on the
    /// schematic stamp the placed component with the metadata as parameters plus a footprint model link.
    /// </summary>
    internal static class ComponentManagerPlaceComponentCommands
    {
        /// <summary>
        /// PlaceSchComponent "Url=..|SessionId=..|ComponentId=..|Out=&lt;cache folder&gt;|X=&lt;mil&gt;|Y=&lt;mil&gt;|Interactive|Part=&lt;n&gt;|Timeout=&lt;s&gt;".
        /// </summary>
        [HotCommand("PlaceSchComponent", "Download a component's libraries and place its symbol")]
        public static string PlaceSchComponent(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client;
            IServerDocument document = client.LastActiveDocumentOfType("SCH")
                ?? throw new UserException("No schematic document is open: open a .SchDoc to place the component on.");
            client.ShowDocument(document);

            Request request = ReadRequest(ctx);
            Placement placement = ReadPlacement(ctx, defaultX: 1000, defaultY: 1000);
            string part = ctx.Get("Part");
            if (!string.IsNullOrEmpty(part) && (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int partId) || partId < 1))
                throw new UserException($"Part='{part}' is not a part number (1 = part A).");

            // The PcbLib first (only its file is used, for the model link), then the SchLib,
            // whose response provides the metadata and the parameter list that are stamped.
            DownloadedLibrary pcbLib = ComponentServer.Download(ctx, request.Url, request.ComponentId, request.SessionId, true, request.Cache, request.Timeout);
            DownloadedLibrary schLib = ComponentServer.Download(ctx, request.Url, request.ComponentId, request.SessionId, false, request.Cache, request.Timeout);

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("The schematic document is not the current document after ShowDocument.");
            IIntegratedLibraryManager manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new UserException("The integrated library manager is not available.");

            HashSet<string> before = SchPlacement.ComponentsOf(sheet).Select(c => c.Handle).ToHashSet(StringComparer.Ordinal);

            // The third argument is an AD parameter string. Location.X/Location.Y (internal units, as in the
            // reference's example) place at a point; without them AD places interactively.
            var placeParameters = new List<KeyValuePair<string, string>>();
            if (!placement.Interactive)
            {
                placeParameters.Add(new("Location.X", EDP.Utils.MilsToCoord(placement.XMil).ToString(CultureInfo.InvariantCulture)));
                placeParameters.Add(new("Location.Y", EDP.Utils.MilsToCoord(placement.YMil).ToString(CultureInfo.InvariantCulture)));
            }
            if (!string.IsNullOrEmpty(part))
                placeParameters.Add(new("PartID", part));
            string parameterText = CommandContext.FormatParameters(placeParameters);

            ctx.Log($"PlaceLibraryComponent('{schLib.LibraryRef}', '{schLib.FilePath}', '{parameterText}')");
            bool placed = manager.PlaceLibraryComponent(schLib.LibraryRef, schLib.FilePath, parameterText);

            // Find what to stamp: the components that are new on the sheet and carry the library
            // reference. If there are none (interactive placement still in progress or cancelled, or the handles
            // changed), fall back to the metadata rule: every component with DesignItemId = libraryref and
            // SourceLibraryName = librarypath that has no ComponentManager_ID yet.
            ISch_Document current = schServer.GetCurrentSchDocument() ?? sheet;
            List<SchPlacement.ComponentInfo> after = SchPlacement.ComponentsOf(current);
            string rule = "new on the sheet";
            List<SchPlacement.ComponentInfo> targets = after
                .Where(c => !before.Contains(c.Handle) &&
                            (Same(c.DesignItemId, schLib.LibraryRef) || Same(c.LibReference, schLib.LibraryRef)))
                .ToList();
            if (targets.Count == 0)
            {
                rule = "the fallback rule (DesignItemId = libraryref, SourceLibraryName = librarypath, no ComponentManager_ID)";
                targets = after
                    .Where(c => Same(c.DesignItemId, schLib.LibraryRef) && Same(c.SourceLibraryName, schLib.LibraryPath) && !c.HasIdParameter)
                    .ToList();
            }

            ISch_RobotManager robot = schServer.GetState_RobotManager();
            int created = 0, updated = 0;
            foreach (SchPlacement.ComponentInfo target in targets)
            {
                ISch_Component component = target.Component;
                // Change notifications through the robot manager (schematic guide).
                robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                try
                {
                    foreach ((string name, string value) in new[]
                             {
                                 (SchPlacement.IdParameter, schLib.Id),
                                 ("ComponentManager_ComponentType", schLib.ComponentType),
                                 ("ComponentManager_AssembleTech", schLib.AssembleTech),
                                 ("ComponentManager_LibraryPath", schLib.LibraryPath),
                                 ("ComponentManager_LibraryRef", schLib.LibraryRef),
                                 ("ComponentManager_FootprintPath", schLib.FootprintPath),
                                 ("ComponentManager_FootprintRef", schLib.FootprintRef),
                             })
                    {
                        if (SchPlacement.AddOrSetParameter(schServer, robot, component, name, "STRING", value)) created++; else updated++;
                    }
                    foreach (ComponentParameter p in schLib.Parameters)
                    {
                        if (SchPlacement.AddOrSetParameter(schServer, robot, component, p.Name, p.Type, p.Value)) created++; else updated++;
                    }
                    SchPlacement.AddFootprintModel(component, schLib.FootprintRef, pcbLib.FilePath);
                }
                finally
                {
                    robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                }
                component.GraphicallyInvalidate();
            }
            current.GraphicallyInvalidate();

            var text = new StringBuilder();
            text.Append($"Downloaded {Path.GetFileName(pcbLib.FilePath)} ({pcbLib.Bytes} bytes) and {Path.GetFileName(schLib.FilePath)} " +
                        $"({schLib.Bytes} bytes) to {request.Cache}. ");
            text.Append($"PlaceLibraryComponent('{schLib.LibraryRef}', '{parameterText}') returned {placed} " +
                        $"({(placement.Interactive ? "interactive" : $"at {placement.XMil.ToString(CultureInfo.InvariantCulture)}, {placement.YMil.ToString(CultureInfo.InvariantCulture)} mil")}); " +
                        $"components on {Path.GetFileName(current.GetState_DocumentName())}: {before.Count} -> {after.Count}. ");
            if (targets.Count == 0)
            {
                text.Append($"No component to stamp: none is new with lib reference '{schLib.LibraryRef}', and none matches {rule}.");
                if (placement.Interactive)
                    text.Append(" If the placement was cancelled, nothing was placed.");
            }
            else
            {
                text.Append($"Stamped {targets.Count} component(s) ({rule}): {created} parameter(s) created, {updated} updated, " +
                            $"footprint model '{schLib.FootprintRef}' linked to {Path.GetFileName(pcbLib.FilePath)}. ");
                text.Append(string.Join(" | ", targets.Select(t => SchPlacement.Describe(t.Component))));
            }
            text.Append(" Close without saving to discard.");
            return text.ToString();
        }

        /// <summary>
        /// PlacePcbComponent "Url=..|SessionId=..|ComponentId=..|Out=&lt;cache folder&gt;|X=&lt;mil&gt;|Y=&lt;mil&gt;|Interactive|Timeout=&lt;s&gt;".
        /// X/Y are mils from the board origin; default: the centre of the board outline.
        /// </summary>
        [HotCommand("PlacePcbComponent", "Download a component's PcbLib and place its footprint")]
        public static string PlacePcbComponent(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client;
            IServerDocument document = client.LastActiveDocumentOfType("PCB")
                ?? throw new UserException("No PCB document is open: open a .PcbDoc to place the component on.");
            client.ShowDocument(document);

            Request request = ReadRequest(ctx);
            Placement placement = ReadPlacement(ctx, defaultX: double.NaN, defaultY: double.NaN);

            DownloadedLibrary pcbLib = ComponentServer.Download(ctx, request.Url, request.ComponentId, request.SessionId, true, request.Cache, request.Timeout);

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("The PCB document is not the current board after ShowDocument.");
            int before = PcbPlacement.CountComponents(board);

            // Interactive: park the new component at (-1,-1), select it alone and hand it to
            // PCB:PlaceComponent with RepositionSelected=True; if it is still at (-1,-1) afterwards, the user cancelled.
            int x = -1, y = -1;
            string where = "interactive";
            if (!placement.Interactive)
                (x, y, where) = BoardLocation(board, placement);

            DXP.Utils.RunCommand("PCB:DeSelect", "Scope=All");

            IPCB_Component component;
            pcbServer.PreProcess();
            try
            {
                IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentObject, TDimensionKind.eNoDimension,
                                                                      TObjectCreationMode.eCreate_Default);
                component = primitive as IPCB_Component;
                if (component == null)
                {
                    if (primitive != null)
                        pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException("PCBObjectFactory did not create a component.");
                }

                component.SetState_Board(board);
                // Check the result: a footprint that is not in the file leaves an empty component.
                if (!component.LoadFromLibrary("FileName=" + pcbLib.FilePath + "|Footprint=" + pcbLib.FootprintRef))
                {
                    pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException($"LoadFromLibrary found no footprint '{pcbLib.FootprintRef}' in {Path.GetFileName(pcbLib.FilePath)}. Nothing was placed.");
                }
                // The top layer; the .NET Layer property is a TV6_Layer.
                component.SetState_Layer(TV6_Layer.eV6_TopLayer);
                component.SetState_XLocation(x);
                component.SetState_YLocation(y);
                board.AddPCBObject(component);
                if (placement.Interactive)
                    component.SetState_Selected(true);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            string outcome;
            if (placement.Interactive)
            {
                // Not described in the SDK reference: the process name and RepositionSelected=True.
                ctx.Log("PCB:PlaceComponent RepositionSelected=True: click in the PCB view to drop the component.");
                DXP.Utils.RunCommand("PCB:PlaceComponent", "RepositionSelected=True");
                if (component.GetState_XLocation() == -1 || component.GetState_YLocation() == -1)
                {
                    pcbServer.PreProcess();
                    try { board.RemovePCBObject(component); }
                    finally { pcbServer.PostProcess(); }
                    outcome = "still at (-1,-1) after PCB:PlaceComponent: treated as cancelled and removed again";
                }
                else
                {
                    outcome = $"placed interactively at {Mil(component.GetState_XLocation() - board.GetState_XOrigin())}, " +
                              $"{Mil(component.GetState_YLocation() - board.GetState_YOrigin())} mil from the origin";
                }
            }
            else
            {
                outcome = $"placed at {Mil(component.GetState_XLocation() - board.GetState_XOrigin())}, " +
                          $"{Mil(component.GetState_YLocation() - board.GetState_YOrigin())} mil from the origin ({where})";
            }
            board.GraphicalView_ZoomRedraw();

            int afterCount = PcbPlacement.CountComponents(board);
            return $"Downloaded {Path.GetFileName(pcbLib.FilePath)} ({pcbLib.Bytes} bytes) to {request.Cache}. " +
                   $"Footprint '{component.GetState_Pattern()}' of component '{pcbLib.Id}' ({pcbLib.ComponentType}, {pcbLib.AssembleTech}) " +
                   $"{outcome}; designator '{component.GetState_Name()?.GetState_Text()}', " +
                   $"{component.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.ePadObject })} pads. " +
                   $"Components on {Path.GetFileName(board.GetState_FileName())}: {before} -> {afterCount}. " +
                   $"The server's {pcbLib.Parameters.Count} parameter(s) are not applied on the PCB. " +
                   "Close without saving to discard.";
        }

        // ---- parameters ------------------------------------------------------------------------------------------

        private sealed record Request(string Url, string SessionId, string ComponentId, string Cache, int Timeout);

        private sealed record Placement(bool Interactive, double XMil, double YMil);

        private static Request ReadRequest(CommandContext ctx)
        {
            // Without Url, a running StartTestServer stub is used, so the menu items (which
            // cannot pass parameters) work against it.
            TestServer stub = string.IsNullOrWhiteSpace(ctx.Get("Url")) ? TestServer.Current : null;
            string url = stub?.Url ?? Required(ctx, "Url", "the component server URL");
            string session = stub != null ? ctx.Get("SessionId", "test") : Required(ctx, "SessionId", "the server session");
            string id = stub != null ? ctx.Get("ComponentId", stub.Config.Id) : Required(ctx, "ComponentId", "the component's id on the server");
            // The download cache: Out, or the temp folder.
            string cache = Path.GetFullPath(ctx.Get("Out", Path.Combine(Path.GetTempPath(), "ComponentManager")));
            string timeoutText = ctx.Get("Timeout", "30");
            if (!int.TryParse(timeoutText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout) || timeout < 1)
                throw new UserException($"Timeout='{timeoutText}' is not a number of seconds.");
            return new Request(url, session, id, cache, timeout);
        }

        /// <summary>
        /// Interactive placement waits for a click in AD, so it is the default from the
        /// menu only, and refused on the pipe. X/Y place at a point instead.
        /// </summary>
        private static Placement ReadPlacement(CommandContext ctx, double defaultX, double defaultY)
        {
            bool hasXY = !string.IsNullOrEmpty(ctx.Get("X")) || !string.IsNullOrEmpty(ctx.Get("Y"));
            bool interactive = ctx.Get("Interactive") != null ? ctx.Flag("Interactive") : ctx.IsInteractive && !hasXY;
            if (interactive && !ctx.IsInteractive)
                throw new UserException("Interactive placement waits for a mouse click in AD; it is refused on the pipe. " +
                                        "Pass X=<mil>|Y=<mil> (or nothing) instead.");
            if (interactive)
                return new Placement(true, 0, 0);
            return new Placement(false, Mil(ctx, "X", defaultX), Mil(ctx, "Y", defaultY));
        }

        /// <summary>X/Y from the board origin, or the centre of the board outline (as the PlacePcbLibComponent sample).</summary>
        private static (int X, int Y, string Where) BoardLocation(IPCB_Board board, Placement placement)
        {
            if (!double.IsNaN(placement.XMil) && !double.IsNaN(placement.YMil))
                return (board.GetState_XOrigin() + EDP.Utils.MilsToCoord(placement.XMil),
                        board.GetState_YOrigin() + EDP.Utils.MilsToCoord(placement.YMil), "X/Y parameters");
            if (!double.IsNaN(placement.XMil) || !double.IsNaN(placement.YMil))
                throw new UserException("Give both X and Y in mils, or neither.");
            IPCB_BoardOutline outline = board.GetState_BoardOutline()
                ?? throw new UserException("The board has no outline. Pass X=<mil>|Y=<mil>.");
            PCB.CoordRect r = outline.BoundingRectangle();
            return ((r.Left + r.Right) / 2, (r.Bottom + r.Top) / 2, "centre of the board outline");
        }

        private static string Required(CommandContext ctx, string name, string what)
        {
            string value = ctx.Get(name)?.Trim();
            if (string.IsNullOrEmpty(value))
                throw new UserException($"Pass {name}=<{what}> (Url, SessionId and ComponentId are required, unless StartTestServer is running and Url is omitted).");
            return value;
        }

        private static double Mil(CommandContext ctx, string name, double defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrEmpty(text))
                return defaultValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new UserException($"{name}='{text}' is not a number of mils.");
            return value;
        }

        private static string Mil(int coord) => EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);

        private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
