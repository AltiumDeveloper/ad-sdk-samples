using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.PlaceAndMovePcbComponent
{
    /// <summary>
    /// Creates a component from a PcbLib footprint, adds it to the current board, selects it and hands it to the PCB
    /// editor's own move tool (process PCB:MoveObject), so the user carries it on the cursor and clicks to drop it.
    /// The SDK lesson: an extension cannot drag an object itself, but it can prepare the selection and then start a
    /// built-in interactive process with DXP.Utils.RunCommand.
    /// </summary>
    internal static class PlaceAndMovePcbComponentCommands
    {
        // The move process and its parameter string. Neither is described in the SDK reference.
        private const string MoveProcess = "PCB:MoveObject";
        private const string MoveParameters = "Object=Component|Drag=True|ContextObject=Component";

        /// <summary>
        /// PlaceAndMoveComponent "Lib=&lt;.PcbLib&gt;|Footprint=&lt;name&gt;|X=&lt;mil&gt;|Y=&lt;mil&gt;|Move=true|false".
        /// Lib is absolute or relative to the board's folder (default: the first .PcbLib next to the board). Footprint
        /// defaults to the footprint of the first component on the board. X/Y are mils from the board origin (default: the origin). Move defaults to true
        /// from the menu and false from the pipe, because the move tool waits for a mouse click.
        /// </summary>
        [HotCommand("PlaceAndMoveComponent", "Place a PcbLib footprint and start the interactive move")]
        public static string PlaceAndMoveComponent(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            (string libPath, string libSource) = ResolveLibrary(ctx, board);
            string footprint = ctx.Get("Footprint");
            string footprintSource = "Footprint parameter";
            if (string.IsNullOrWhiteSpace(footprint))
            {
                footprint = FirstComponentPattern(board)
                    ?? throw new UserException("No Footprint given and the board has no component to take one from.");
                footprintSource = "footprint of the first component on the board";
            }

            int x = board.GetState_XOrigin() + MilParameter(ctx, "X");
            int y = board.GetState_YOrigin() + MilParameter(ctx, "Y");

            string moveValue = ctx.Get("Move");
            bool move = moveValue == null ? ctx.IsInteractive : ctx.Flag("Move");

            int before = CountComponents(board);

            // Clear the selection first, so the move tool only picks up the new component.
            DXP.Utils.RunCommand("PCB:DeSelect", "Scope=All");

            IPCB_Component component;
            pcbServer.PreProcess();
            try
            {
                IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentObject,
                    TDimensionKind.eNoDimension, TObjectCreationMode.eCreate_Default);
                component = primitive as IPCB_Component;
                if (component == null)
                {
                    if (primitive != null)
                        pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException("PCBObjectFactory did not create a component.");
                }

                // Order: Board, load, layer, location, add.
                component.SetState_Board(board);
                if (!component.LoadFromLibrary("FileName=" + libPath + "|Footprint=" + footprint))
                {
                    // A component that failed to load is not added to the board.
                    primitive = component;
                    pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException($"LoadFromLibrary returned false for footprint '{footprint}' of " +
                                            $"'{libPath}'. Nothing was placed.");
                }

                // In .NET IPCB_Primitive.Layer is a TV6_Layer.
                component.SetState_Layer(TV6_Layer.eV6_TopLayer);
                component.SetState_XLocation(x);
                component.SetState_YLocation(y);
                board.AddPCBObject(component);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            // The move tool acts on the selection: mark the new component selected and redraw it.
            component.SetState_Selected(true);
            board.GraphicallyInvalidate();

            int after = CountComponents(board);
            string placed = $"Placed '{component.GetState_Pattern()}' ({footprintSource}) from {Path.GetFileName(libPath)} ({libSource}) " +
                            $"on {Path.GetFileName(board.GetState_FileName())}: designator " +
                            $"'{component.GetState_Name()?.GetState_Text() ?? ""}', " +
                            $"{component.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.ePadObject })} pads, " +
                            $"layer {component.GetState_Layer()}, at {Location(board, component)}, " +
                            $"selected {component.GetState_Selected()}. Components on the board: {before} -> {after}.";

            if (!move)
                return placed + " Move tool not started (Move=false" + (moveValue == null ? ", the default from the pipe" : "") +
                       "). Close without saving to discard the component.";

            // Hands the selected component to the editor's interactive move. Whether RunCommand returns as soon as
            // the tool starts or only after the user drops the component is not documented; the elapsed time and
            // the location afterwards show which.
            ctx.Log(placed);
            ctx.Log($"Starting {MoveProcess} ({MoveParameters}): click in the PCB view to drop the component.");
            var clock = Stopwatch.StartNew();
            DXP.Utils.RunCommand(MoveProcess, MoveParameters);
            clock.Stop();

            return placed + Environment.NewLine +
                   $"{MoveProcess} returned after {clock.ElapsedMilliseconds} ms; the component is now at " +
                   $"{Location(board, component)}. Close without saving to discard it.";
        }

        /// <summary>
        /// Lib: an absolute path, or a path relative to the folder of the board. Without Lib, the first .PcbLib (by
        /// name) in the board's folder or one of its direct subfolders, so the menu item works without a hard-coded
        /// path.
        /// </summary>
        private static (string Path, string Source) ResolveLibrary(CommandContext ctx, IPCB_Board board)
        {
            string boardFolder = Path.GetDirectoryName(board.GetState_FileName()) ?? "";
            string lib = ctx.Get("Lib");
            if (string.IsNullOrWhiteSpace(lib))
            {
                string found = FindLibraryNear(boardFolder)
                    ?? throw new UserException("No Lib given and no .PcbLib next to the board. Pass Lib=<path of a " +
                                               ".PcbLib> (absolute, or relative to the board's folder).");
                return (found, "first .PcbLib next to the board");
            }

            string path = Path.IsPathRooted(lib) ? lib : Path.Combine(boardFolder, lib);
            if (!File.Exists(path))
                throw new UserException($"Library not found: '{Path.GetFullPath(path)}'.");
            return (Path.GetFullPath(path), "Lib parameter");
        }

        private static string FindLibraryNear(string folder)
        {
            if (!Directory.Exists(folder))
                return null;
            var folders = new List<string> { folder };
            var subfolders = Directory.GetDirectories(folder);
            Array.Sort(subfolders, StringComparer.OrdinalIgnoreCase);
            folders.AddRange(subfolders);
            foreach (string f in folders)
            {
                string[] libs = Directory.GetFiles(f, "*.PcbLib");
                if (libs.Length == 0)
                    continue;
                Array.Sort(libs, StringComparer.OrdinalIgnoreCase);
                return libs[0];
            }
            return null;
        }

        /// <summary>Optional X/Y parameter in mils, converted to internal units (10,000 per mil).</summary>
        private static int MilParameter(CommandContext ctx, string name)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double mil))
                throw new UserException($"{name}='{text}' is not a number of mils.");
            return checked((int)Math.Round(mil * 10000));
        }

        private static string Location(IPCB_Board board, IPCB_Component component) =>
            $"X={Mil(component.GetState_XLocation() - board.GetState_XOrigin())} " +
            $"Y={Mil(component.GetState_YLocation() - board.GetState_YOrigin())} mil from the origin";

        private static string Mil(int internalUnits) =>
            (internalUnits / 10000.0).ToString("0.###", CultureInfo.InvariantCulture);

        private static string FirstComponentPattern(IPCB_Board board)
        {
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    string pattern = component.GetState_Pattern();
                    if (!string.IsNullOrWhiteSpace(pattern))
                        return pattern;
                }
                return null;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }

        private static int CountComponents(IPCB_Board board)
        {
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                int count = 0;
                for (var o = iterator.FirstPCBObject(); o != null; o = iterator.NextPCBObject())
                    count++;
                return count;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }
    }
}
