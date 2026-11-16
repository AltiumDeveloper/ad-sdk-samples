using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXP; // IClient.GetCurrentView / GetDXPWorkspace and IServerDocumentView.GetOwnerDocument are extension methods in DXP
using PCB;

namespace AdSamples.ImportDwgToPcb
{
    /// <summary>
    /// Imports an AutoCAD DWG/DXF file into a new PCB document and saves it. The SDK lesson: an extension can drive
    /// a built-in importer by running its process (PCB:Import) with DXP.Utils.RunCommand on a document it created
    /// with IWorkspace.DM_CreateNewDocument, then name and save that document through IServerDocument.
    /// </summary>
    internal static class ImportDwgToPcbCommands
    {
        // The SDK reference does not list the parameters
        // of PCB:Import. The Format text is the FileExtensions value of the PCB editor's
        // 'DXF/DWG' entry in ImportFilters of AD's System\advpcb.ins, which is where the importer is declared.
        private const string ImportProcess = "PCB:Import";
        private const string AutoCadFormat = "AutoCAD Files (*.DXF; *.DWG)";

        // Silent=True and TestMode=True are passed, but the HKLM
        // Software\Altium\TestMode registry key is not written
        // (a system setting that needs elevation). The
        // importer still shows its import dialog with these parameters, and the import
        // continues only after a user confirms it. So they do not make the import
        // unattended.
        private const string SilentParameters = "Silent=True|TestMode=True";

        // The kind strings DoFileSave accepts are not documented. An empty kind is what the other samples pass for
        // "save in the document's own format"; it saves the renamed new PcbDoc.
        private const string SaveKind = "";

        // The primitive kinds a DWG/DXF import can plausibly create, counted before and after the import so the result
        // is checkable from the returned text.
        private static readonly TObjectId[] CountedKinds =
        {
            TObjectId.eTrackObject, TObjectId.eArcObject, TObjectId.eTextObject, TObjectId.eFillObject,
            TObjectId.eRegionObject, TObjectId.ePolyObject, TObjectId.ePadObject, TObjectId.eViaObject,
            TObjectId.eComponentObject, TObjectId.eDimensionObject, TObjectId.eComponentBodyObject,
        };

        /// <summary>
        /// ImportDwg "File=&lt;.dwg or .dxf&gt;|Out=&lt;folder or .PcbDoc path&gt;|Overwrite=true|Silent=false":
        /// creates a new PCB document, imports File into it with PCB:Import, and saves it as Out
        /// (default: the temp folder, named after File).
        /// </summary>
        [HotCommand("ImportDwg", "Import a DWG/DXF file into a new PCB document and save it")]
        public static string ImportDwg(CommandContext ctx)
        {
            // 1. Validate the parameters before anything is created.
            string file = ctx.Get("File");
            if (string.IsNullOrWhiteSpace(file))
                throw new UserException("No input file specified. Pass it as a parameter: File=<path of a .dwg or .dxf file>.");
            file = Path.GetFullPath(file);
            if (!File.Exists(file))
                throw new UserException($"'{file}' does not exist.");
            string extension = Path.GetExtension(file);
            if (!extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"'{file}' is not a .dwg or .dxf file.");

            string target = TargetPath(ctx.Get("Out", Path.GetTempPath()), file);
            if (File.Exists(target) && !ctx.Flag("Overwrite"))
                throw new UserException($"'{target}' already exists. Pass Overwrite=true to replace it, or choose another Out.");

            // Silent=true (the default) passes the Silent/TestMode parameters; Silent=false omits them.
            // Neither suppresses the import dialog (see SilentParameters).
            bool silent = !string.Equals(ctx.Get("Silent", "true"), "false", StringComparison.OrdinalIgnoreCase);

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            var workspace = client.GetDXPWorkspace() as EDP.IWorkspace
                ?? throw new UserException("The workspace is not available.");

            // Remember the focused document, so the command can tell whether a new one really got the focus.
            string previousFile = client.GetCurrentView()?.GetOwnerDocument()?.GetFileName() ?? "";

            // 2. A new, empty PCB document. The reference does not say what
            // the string is, so the command reports it.
            string created = workspace.DM_CreateNewDocument("PCB");
            ctx.Log($"DM_CreateNewDocument(\"PCB\") returned '{created}'.");

            // Check that the new document is the current view: kind PCB, and not the
            // document focused before, so a failure cannot import into (and rename) a board the user already had open.
            IServerDocument document = client.GetCurrentView()?.GetOwnerDocument()
                ?? throw new UserException("DM_CreateNewDocument(\"PCB\") left no document in focus. Nothing was imported.");
            if (!string.Equals(document.GetKind(), "PCB", StringComparison.OrdinalIgnoreCase) ||
                (previousFile.Length > 0 && string.Equals(document.GetFileName(), previousFile, StringComparison.OrdinalIgnoreCase)))
                throw new UserException(
                    $"DM_CreateNewDocument(\"PCB\") did not focus a new PCB document (focused: kind '{document.GetKind()}', " +
                    $"file '{document.GetFileName()}'). Nothing was imported.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB server is not running after creating the PCB document.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("The new document has no current PCB board. Nothing was imported.");
            string newFile = document.GetFileName();

            // 3. Run the importer on the current (new) document. Parameters are Name=Value pairs joined by '|'.
            Dictionary<TObjectId, int> before = CountPrimitives(board);
            string parameters = "";
            DXP.Utils.SetParamterValue(ref parameters, "Format", AutoCadFormat);
            DXP.Utils.SetParamterValue(ref parameters, "Filename", file);
            if (silent)
                parameters += "|" + SilentParameters;
            ctx.Log($"{ImportProcess} {parameters}");
            // The importer shows its own modal dialog and RunCommand returns only after it is confirmed, so someone
            // has to be at Altium Designer: this sample is interactive-only.
            ctx.Log($"{ImportProcess} shows its import dialog: confirm it in Altium Designer to continue.");
            DXP.Utils.RunCommand(ImportProcess, parameters);
            DXP.Utils.RunCommand("PCB:Zoom", "Action=Redraw");

            // Re-query the board: the importer may have replaced the view's board object.
            board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException($"No current PCB board after {ImportProcess}. The new document is left open, unsaved.");
            if (!string.Equals(board.GetState_FileName(), newFile, StringComparison.OrdinalIgnoreCase))
                throw new UserException(
                    $"After {ImportProcess} the current board is '{board.GetState_FileName()}', not the new '{newFile}'. Nothing was saved.");
            Dictionary<TObjectId, int> after = CountPrimitives(board);
            int added = after.Values.Sum() - before.Values.Sum();

            // Saving an empty board would hide a failed import, so stop here.
            if (added <= 0)
                throw new UserException(
                    $"{ImportProcess} added no primitives to the new PCB document '{newFile}' (Silent={silent}). " +
                    "It is left open and unsaved; check the Messages panel or run again with Silent=false.");

            // 4. Name the new document and save it. DoFileSave is the document's own save;
            // WorkSpaceManager:SaveObject can show a dialog.
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            document.SetFileName(target);
            bool saved = document.DoFileSave(SaveKind);
            ctx.Log($"DoFileSave: {saved}.");
            bool exists = File.Exists(target);

            return $"Imported '{Path.GetFileName(file)}' into a new PCB document (Silent={silent}): {added} primitives added " +
                   $"({Describe(before, after)}).\n" +
                   $"Saved as '{target}': DoFileSave returned {saved}, file {(exists ? "exists" : "NOT found")} on disk. " +
                   $"DM_CreateNewDocument returned '{created}'.\n" +
                   $"Layers used: {LayersUsed(board)}.";
        }

        /// <summary>Out is a .PcbDoc path, or a folder that receives &lt;input name&gt;.PcbDoc.</summary>
        private static string TargetPath(string outParameter, string inputFile)
        {
            if (string.IsNullOrWhiteSpace(outParameter))
                outParameter = Path.GetTempPath();
            string full = Path.GetFullPath(outParameter);
            return string.Equals(Path.GetExtension(full), ".PcbDoc", StringComparison.OrdinalIgnoreCase)
                ? full
                : Path.Combine(full, Path.GetFileNameWithoutExtension(inputFile) + ".PcbDoc");
        }

        /// <summary>Counts the board's primitives of the CountedKinds, on all layers.</summary>
        private static Dictionary<TObjectId, int> CountPrimitives(IPCB_Board board)
        {
            var counts = CountedKinds.ToDictionary(kind => kind, _ => 0);
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                var set = new PCB.TObjectSet();
                foreach (TObjectId kind in CountedKinds)
                    set.Add(kind);
                iterator.AddFilter_ObjectSet(set);
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive primitive = iterator.FirstPCBObject(); primitive != null; primitive = iterator.NextPCBObject())
                {
                    TObjectId kind = primitive.GetState_ObjectID();
                    if (counts.ContainsKey(kind))
                        counts[kind]++;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return counts;
        }

        /// <summary>The layers that hold tracks, arcs, texts, fills or regions, with their counts (TV6_Layer names).</summary>
        private static string LayersUsed(IPCB_Board board)
        {
            var layers = new SortedDictionary<string, int>(StringComparer.Ordinal);
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet
                {
                    TObjectId.eTrackObject, TObjectId.eArcObject, TObjectId.eTextObject, TObjectId.eFillObject, TObjectId.eRegionObject,
                });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive primitive = iterator.FirstPCBObject(); primitive != null; primitive = iterator.NextPCBObject())
                {
                    string layer = primitive.GetState_Layer().ToString();
                    layers[layer] = layers.TryGetValue(layer, out int n) ? n + 1 : 1;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return layers.Count == 0 ? "none" : string.Join(", ", layers.Select(l => $"{l.Key} {l.Value}"));
        }

        /// <summary>"tracks 0 -> 120, arcs 0 -> 14" for the kinds whose count changed.</summary>
        private static string Describe(Dictionary<TObjectId, int> before, Dictionary<TObjectId, int> after)
        {
            var changed = CountedKinds.Where(kind => before[kind] != after[kind])
                                      .Select(kind => $"{kind} {before[kind]} -> {after[kind]}")
                                      .ToList();
            return changed.Count == 0 ? "no count changed" : string.Join(", ", changed);
        }
    }
}
