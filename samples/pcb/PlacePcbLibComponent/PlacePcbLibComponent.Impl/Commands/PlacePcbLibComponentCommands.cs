using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;
// The library manager lives in the SCH namespace, which also defines TObjectId, CoordRect etc.: alias the two
// types instead of importing the namespace, so the PCB names stay unambiguous.
using IIntegratedLibraryManager = SCH.IIntegratedLibraryManager;
using IModelType = SCH.IModelType;

namespace AdSamples.PlacePcbLibComponent
{
    /// <summary>
    /// Finds a component in an integrated library with IIntegratedLibraryManager, picks its PCB footprint model, then
    /// creates an IPCB_Component, loads the footprint into it with LoadFromLibrary and adds it to the current board.
    /// </summary>
    internal static class PlacePcbLibComponentCommands
    {
        // The defaults. The library is looked up by file name among the libraries AD knows about
        // (Available / Installed lists) instead of a fixed folder.
        private const string DefaultLibraryFile = "Miscellaneous Devices.IntLib";
        private const string DefaultComponent = "Res Pack1";
        private const string DefaultFootprint = "SOIC16_M";

        // .NET exposes no constant for the footprint model type.
        // IModelType.Name returns "PCBLIB" for a footprint model (ServerName "PCB").
        private const string PcbModelType = "PCBLIB";

        /// <summary>
        /// PlaceComponent "Lib=&lt;.IntLib&gt;|Component=&lt;name&gt;|Footprint=&lt;model&gt;|X=&lt;mil&gt;|Y=&lt;mil&gt;|LoadKey=&lt;key&gt;".
        /// X/Y are relative to the board origin; without them the footprint goes to the centre of the board outline.
        /// </summary>
        [HotCommand("PlaceComponent", "Place a footprint from an integrated library on the board")]
        public static string PlaceComponent(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            IIntegratedLibraryManager manager = LibraryManager();
            string libPath = ResolveLibrary(ctx, manager);
            string componentName = ctx.Get("Component", DefaultComponent);
            string footprintName = ctx.Get("Footprint", DefaultFootprint);

            int componentIndex = FindComponent(manager, libPath, componentName);
            string modelName = FindPcbModel(manager, libPath, componentIndex, componentName, footprintName);

            (int x, int y, string where) = Location(ctx, board);

            // For an .IntLib the key is IntegratedLibraryName=<path>. The
            // reference's FileName=<path> is the key for a .PcbLib; with an .IntLib path LoadFromLibrary throws a
            // COMException (E_UNEXPECTED) instead of returning false. LoadKey= forces another key for experiments.
            string forcedKey = ctx.Get("LoadKey");
            string[] keys = string.IsNullOrWhiteSpace(forcedKey)
                ? new[] { "IntegratedLibraryName" }
                : new[] { forcedKey.Trim() };

            int before = CountComponents(board);
            IPCB_Component component = null;
            string usedKey = null;
            var failedKeys = new List<string>();

            pcbServer.PreProcess();
            try
            {
                foreach (string key in keys)
                {
                    // A fresh component per attempt, so a failed load cannot leave half-loaded primitives behind.
                    IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentObject,
                        TDimensionKind.eNoDimension, TObjectCreationMode.eCreate_Default);
                    var candidate = primitive as IPCB_Component;
                    if (candidate == null)
                    {
                        if (primitive != null)
                            pcbServer.DestroyPCBObject(ref primitive);
                        throw new UserException("PCBObjectFactory did not create a component.");
                    }

                    // Order: Board first, then load, then layer and location, then add.
                    candidate.SetState_Board(board);
                    bool loaded;
                    string failure = "returned false";
                    try
                    {
                        loaded = candidate.LoadFromLibrary(key + "=" + libPath + "|Footprint=" + modelName);
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        // A key the loader does not accept for this library type can fail with an exception.
                        loaded = false;
                        failure = $"threw COMException 0x{ex.HResult:X8}";
                    }
                    if (loaded)
                    {
                        component = candidate;
                        usedKey = key;
                        break;
                    }

                    failedKeys.Add($"{key}=<path> ({failure})");
                    primitive = candidate;
                    pcbServer.DestroyPCBObject(ref primitive);
                }

                if (component != null)
                {
                    // The .NET Layer property is a TV6_Layer.
                    component.SetState_Layer(TV6_Layer.eV6_TopLayer);
                    component.SetState_XLocation(x);
                    component.SetState_YLocation(y);
                    board.AddPCBObject(component);
                }
            }
            finally
            {
                pcbServer.PostProcess();
            }

            if (component == null)
                throw new UserException($"LoadFromLibrary failed for footprint '{modelName}' of '{libPath}': " +
                                        $"{string.Join(", ", failedKeys)}. Nothing was placed.");

            board.GraphicalView_ZoomRedraw();

            int after = CountComponents(board);
            string designator = component.GetState_Name()?.GetState_Text() ?? "";
            int pads = component.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.ePadObject });
            return $"Placed footprint '{component.GetState_Pattern()}' (component '{componentName}' of " +
                   $"{Path.GetFileName(libPath)}) on {Path.GetFileName(board.GetState_FileName())}: " +
                   $"designator '{designator}', {pads} pads, layer {component.GetState_Layer()}, " +
                   $"at X={Mil(component.GetState_XLocation() - board.GetState_XOrigin())} " +
                   $"Y={Mil(component.GetState_YLocation() - board.GetState_YOrigin())} mil from the origin ({where}). " +
                   $"Loaded with {usedKey}=<path>" +
                   (failedKeys.Count > 0 ? $" after {string.Join(", ", failedKeys)}" : "") +
                   $". Components on the board: {before} -> {after}. Close without saving to discard it.";
        }

        /// <summary>
        /// ListModels "Lib=&lt;.IntLib&gt;|Component=&lt;name&gt;": read-only. Lists the models of
        /// the component with the type name IModelType.Name returns.
        /// </summary>
        [HotCommand("ListModels", "List the models of an integrated library component (read-only)")]
        public static string ListModels(CommandContext ctx)
        {
            IIntegratedLibraryManager manager = LibraryManager();
            string libPath = ResolveLibrary(ctx, manager);
            string componentName = ctx.Get("Component", DefaultComponent);
            int componentIndex = FindComponent(manager, libPath, componentName);

            int modelCount = manager.GetModelCount(libPath, componentIndex);
            var lines = new List<string>
            {
                $"{Path.GetFileName(libPath)} ({libPath}): component '{componentName}' (index {componentIndex}) has {modelCount} models."
            };
            for (int j = 0; j < modelCount; j++)
            {
                IModelType type = ModelType(manager, libPath, componentIndex, j);
                lines.Add($"    [{j}] {manager.GetModelName(libPath, componentIndex, j)} : type " +
                          (type == null ? "<null>" : $"'{type.Name()}' (server '{type.ServerName()}')"));
            }
            return string.Join(Environment.NewLine, lines);
        }

        // GetModelType is a typed extension method of the helper class next to the interface (docs: API conventions,
        // "Typed helper methods"). Called statically, so this PCB-centred file does not need `using SCH;`.
        private static IModelType ModelType(IIntegratedLibraryManager manager, string libPath, int component, int model) =>
            SCH.IIntegratedLibraryManagerHelper.GetModelType(manager, libPath, component, model);

        private static IIntegratedLibraryManager LibraryManager() =>
            EDP.Utils.LoadIntegratedLibraryManager()
            ?? throw new UserException("The integrated library manager is not available.");

        /// <summary>
        /// Lib= is a full path, or a file name looked up among the available and installed libraries. The default is
        /// Miscellaneous Devices.IntLib.
        /// </summary>
        private static string ResolveLibrary(CommandContext ctx, IIntegratedLibraryManager manager)
        {
            string lib = ctx.Get("Lib", DefaultLibraryFile);
            if (Path.IsPathRooted(lib))
                return File.Exists(lib) ? lib : throw new UserException($"Library '{lib}' does not exist.");

            var known = new List<string>();
            for (int i = 0; i < manager.AvailableLibraryCount(); i++)
                known.Add(manager.AvailableLibraryPath(i));
            for (int i = 0; i < manager.InstalledLibraryCount(); i++)
                known.Add(manager.InstalledLibraryPath(i));

            foreach (string path in known)
                if (!string.IsNullOrEmpty(path) && string.Equals(Path.GetFileName(path), lib, StringComparison.OrdinalIgnoreCase))
                    return path;

            throw new UserException($"'{lib}' is not among the {known.Count} available/installed libraries. " +
                                    "Pass the full path: Lib=<path to .IntLib>.");
        }

        private static int FindComponent(IIntegratedLibraryManager manager, string libPath, string componentName)
        {
            int count = manager.GetComponentCount(libPath);
            for (int i = 0; i < count; i++)
                if (string.Equals(manager.GetComponentName(libPath, i), componentName, StringComparison.OrdinalIgnoreCase))
                    return i;
            throw new UserException($"Component '{componentName}' not found in '{libPath}' ({count} components).");
        }

        /// <summary>The model of the component whose type is the PCB footprint type and whose name matches.</summary>
        private static string FindPcbModel(IIntegratedLibraryManager manager, string libPath, int componentIndex,
                                           string componentName, string footprintName)
        {
            var seen = new List<string>();
            int count = manager.GetModelCount(libPath, componentIndex);
            for (int j = 0; j < count; j++)
            {
                string typeName = ModelType(manager, libPath, componentIndex, j)?.Name() ?? "<null>";
                string modelName = manager.GetModelName(libPath, componentIndex, j);
                seen.Add($"{modelName} ({typeName})");
                if (string.Equals(typeName, PcbModelType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(modelName, footprintName, StringComparison.OrdinalIgnoreCase))
                    return modelName;
            }
            throw new UserException($"Component '{componentName}' has no {PcbModelType} model named '{footprintName}'. " +
                                    $"Models: {(seen.Count == 0 ? "none" : string.Join(", ", seen))}.");
        }

        /// <summary>
        /// X=/Y= in mils from the board origin (as the guide's PCBObjectFactory example offsets from XOrigin/YOrigin).
        /// Without them: the centre of the board outline, so the footprint lands on the board whatever its size.
        /// </summary>
        private static (int X, int Y, string Where) Location(CommandContext ctx, IPCB_Board board)
        {
            string xs = ctx.Get("X"), ys = ctx.Get("Y");
            if (!string.IsNullOrWhiteSpace(xs) || !string.IsNullOrWhiteSpace(ys))
            {
                if (!double.TryParse(xs, NumberStyles.Float, CultureInfo.InvariantCulture, out double xm) ||
                    !double.TryParse(ys, NumberStyles.Float, CultureInfo.InvariantCulture, out double ym))
                    throw new UserException("Give both X and Y in mils, for example X=1850|Y=1310.");
                return (board.GetState_XOrigin() + EDP.Utils.MilsToCoord(xm),
                        board.GetState_YOrigin() + EDP.Utils.MilsToCoord(ym), "X/Y parameters");
            }

            IPCB_BoardOutline outline = board.GetState_BoardOutline()
                ?? throw new UserException("The board has no outline. Pass X=<mil>|Y=<mil>.");
            CoordRect r = outline.BoundingRectangle();
            return ((r.Left + r.Right) / 2, (r.Bottom + r.Top) / 2, "centre of the board outline");
        }

        private static int CountComponents(IPCB_Board board)
        {
            int count = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive p = iterator.FirstPCBObject(); p != null; p = iterator.NextPCBObject())
                    count++;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return count;
        }

        private static string Mil(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
