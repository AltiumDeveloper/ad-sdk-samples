using System;
using System.Collections.Generic;
using System.IO;
using PCB;

namespace AdSamples.PcbLibComponentToStep
{
    /// <summary>
    /// Exports the 3D model of every footprint of a PCB library to its own STEP file, using the current PCB document
    /// as a temporary host. The SDK lesson: a library footprint becomes a real board component (PCBObjectFactory +
    /// LoadFromLibrary + AddPCBObject), IPCB_Component.SaveModelToFile writes its model, and RemovePCBObject takes
    /// it off the board again.
    /// </summary>
    internal static class PcbLibComponentToStepCommands
    {
        /// <summary>
        /// PcbLibCompToStep "Lib=&lt;.PcbLib path&gt;|Out=&lt;folder&gt;": writes &lt;Out&gt;/&lt;footprint&gt;.stp for every
        /// footprint of the library. The focused document must be a .PcbDoc; the components placed on it are removed
        /// again, but the add/remove pairs stay in its undo history and the document is left modified.
        /// </summary>
        [HotCommand("PcbLibCompToStep", "Export each footprint of a PcbLib to STEP via the current board")]
        public static string PcbLibCompToStep(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("Open a .PcbDoc first and make it the active document.");
            string boardFile = board.GetState_FileName() ?? "";
            // GetCurrentPCBBoard also returns the board of a focused PCB library; the sample needs a PCB document.
            if (!boardFile.EndsWith(".PcbDoc", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The active PCB document is '{Path.GetFileName(boardFile)}'; focus a .PcbDoc.");

            // The library and the output folder are parameters.
            string libValue = ctx.Get("Lib");
            if (string.IsNullOrWhiteSpace(libValue))
                throw new UserException("Pass Lib=<full path of the .PcbLib>.");
            string libPath = Path.GetFullPath(libValue.Trim().Trim('"'));
            if (!File.Exists(libPath))
                throw new UserException($"PCB library not found: {libPath}");
            string folder = Path.GetFullPath(ctx.Get("Out", Path.GetTempPath()).Trim().Trim('"'));
            Directory.CreateDirectory(folder);

            // LoadPCBLibraryByPath returns the library of an open document, or loads the file without opening it.
            IPCB_Library library = pcbServer.LoadPCBLibraryByPath(libPath)
                ?? throw new UserException($"Altium Designer could not load {libPath} as a PCB library.");

            int componentsBefore = CountComponents(board);
            int footprints = 0, saveTrue = 0;
            var written = new List<string>();
            var noFile = new List<string>();
            var loadFailed = new List<string>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // One PreProcess/PostProcess bracket around all add/remove pairs; AddPCBObject and RemovePCBObject notify
            // the board (and its undo history) themselves.
            pcbServer.PreProcess();
            IPCB_LibraryIterator iterator = library.LibraryIterator_Create();
            try
            {
                for (IPCB_Primitive item = iterator.FirstPCBObject(); item != null; item = iterator.NextPCBObject())
                {
                    if (item is not IPCB_LibComponent footprint)
                        continue;
                    footprints++;

                    string name = footprint.GetState_Pattern(); // the footprint name; IPCB_LibComponent has no Name
                    int bodies = footprint.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
                    string file = Path.Combine(folder, UniqueFileName(name, usedNames) + ".stp");
                    ctx.Log($"{footprints}: {name} ({bodies} 3D bodies)");

                    // SaveModelToFile's result is not documented as "file written": the file on disk decides.
                    DateTime? before = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : (DateTime?)null;

                    bool? saved = ExportOne(pcbServer, board, libPath, name, file);
                    if (saved == null)
                    {
                        loadFailed.Add(name);
                        continue;
                    }
                    if (saved.Value)
                        saveTrue++;

                    bool fileWritten = File.Exists(file) &&
                                       (before == null || File.GetLastWriteTimeUtc(file) > before.Value);
                    if (fileWritten)
                        written.Add($"{name} ({bodies} bodies, {new FileInfo(file).Length} bytes)");
                    else
                        noFile.Add($"{name} ({bodies} bodies)");
                }
            }
            finally
            {
                library.LibraryIterator_Destroy(ref iterator);
                pcbServer.PostProcess();
            }

            int componentsAfter = CountComponents(board);
            var lines = new List<string>
            {
                $"{Path.GetFileName(libPath)}: {written.Count} of {footprints} footprints written as STEP to {folder}.",
                $"SaveModelToFile returned true {saveTrue} times.",
                $"Components on {Path.GetFileName(boardFile)}: {componentsBefore} before, {componentsAfter} after."
            };
            if (written.Count > 0)
                lines.Add("Written: " + string.Join(", ", written));
            if (noFile.Count > 0)
                lines.Add("No file: " + string.Join(", ", noFile));
            if (loadFailed.Count > 0)
                lines.Add("LoadFromLibrary returned false: " + string.Join(", ", loadFailed));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// For one footprint: create a component for the board, load the footprint into it,
        /// place it on the top layer, add it to the board, save its model, and remove it again. Returns null when
        /// LoadFromLibrary fails (nothing was added), otherwise the result of SaveModelToFile.
        /// </summary>
        private static bool? ExportOne(IPCB_ServerInterface pcbServer, IPCB_Board board, string libPath,
                                       string footprintName, string file)
        {
            IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentObject, TDimensionKind.eNoDimension,
                                                                  TObjectCreationMode.eCreate_Default);
            if (primitive is not IPCB_Component component)
            {
                if (primitive != null)
                    pcbServer.DestroyPCBObject(ref primitive);
                throw new UserException("PCBObjectFactory did not create a component.");
            }

            component.SetState_Board(board);
            // '|' separates the Name=Value pairs; FileName and Footprint are the keys.
            if (!component.LoadFromLibrary("FileName=" + libPath + "|Footprint=" + footprintName))
            {
                // Never added to the board, so it can be freed.
                component.SetState_Board(null);
                pcbServer.DestroyPCBObject(ref primitive);
                return null;
            }

            component.SetState_Layer(TV6_Layer.eV6_TopLayer);
            // Coordinates are absolute, so the board origin is added: the component goes 100 mil from the board
            // origin.
            component.SetState_XLocation(board.GetState_XOrigin() + EDP.Utils.MilsToCoord(100));
            component.SetState_YLocation(board.GetState_YOrigin() + EDP.Utils.MilsToCoord(100));
            board.AddPCBObject(component);
            try
            {
                return component.SaveModelToFile(file);
            }
            finally
            {
                // Removed, but not destroyed: the board's undo history holds the add/remove pair.
                board.RemovePCBObject(component);
                component.SetState_Board(null);
            }
        }

        private static int CountComponents(IPCB_Board board)
        {
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            int count = 0;
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive item = iterator.FirstPCBObject(); item != null; item = iterator.NextPCBObject())
                    count++;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return count;
        }

        /// <summary>The footprint name without characters a file name can't have, made unique within the folder.</summary>
        private static string UniqueFileName(string name, HashSet<string> used)
        {
            string clean = name ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c.ToString(), "");
            if (clean.Length == 0)
                clean = "_";

            string candidate = clean;
            for (int i = 2; !used.Add(candidate); i++)
                candidate = $"{clean}_{i}";
            return candidate;
        }
    }
}
