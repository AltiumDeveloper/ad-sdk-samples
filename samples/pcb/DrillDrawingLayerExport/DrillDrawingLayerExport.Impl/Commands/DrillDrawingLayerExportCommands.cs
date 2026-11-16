using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCB;

namespace AdSamples.DrillDrawingLayerExport
{
    /// <summary>
    /// Dumps every primitive on the Drill Drawing layer of the current board as its parameter string
    /// (IPCB_Primitive.Export_ToParameters), one line per primitive, to a text file.
    /// </summary>
    internal static class DrillDrawingLayerExportCommands
    {
        /// <summary>
        /// ExportDrillDrawing "Out=&lt;folder or file&gt;|TextOnly=true": writes the file and returns the primitive
        /// count, a count per object kind and the file path.
        /// </summary>
        [HotCommand("ExportDrillDrawing", "Export the primitives of the drill drawing layer to a text file")]
        public static string ExportDrillDrawing(CommandContext ctx)
        {
            bool textOnly = ctx.Flag("TextOnly");
            IPCB_Board board = CurrentBoard();
            string boardName = Path.GetFileName(board.GetState_FileName());

            var lines = new List<string>();
            var countByKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int emptyCount = 0;

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // PCBConstant has no all-objects set, so every TObjectId except eNoObject is listed.
                iterator.AddFilter_ObjectSet(AllObjects());

                // The .NET API has no V7 layer
                // enumeration, and AddFilter_LayerSet_2 takes a TLayerPartitionSet of unclear behaviour
                // (undocumented). The V6 layer set with eV6_DrillDrawing is the documented iterator filter.
                iterator.AddFilter_LayerSet(new TV6_LayerSet { TV6_Layer.eV6_DrillDrawing });
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (IPCB_Primitive primitive = iterator.FirstPCBObject(); primitive != null; primitive = iterator.NextPCBObject())
                {
                    TViewableObjectID kind = primitive.GetState_ViewableObjectID();

                    // Optional filter; TextOnly switches it on.
                    if (textOnly && kind != TViewableObjectID.eViewableObject_Text)
                        continue;

                    // A fresh string per primitive.
                    string parameters = string.Empty;
                    primitive.Export_ToParameters(ref parameters);
                    if (string.IsNullOrEmpty(parameters))
                        emptyCount++;
                    lines.Add(parameters ?? string.Empty);

                    string kindName = kind.ToString();
                    countByKind[kindName] = countByKind.TryGetValue(kindName, out int n) ? n + 1 : 1;

                    if (lines.Count % 500 == 0)
                        ctx.Log($"{lines.Count} primitives exported...");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            string path = OutputPath(ctx, boardName);
            File.WriteAllLines(path, lines);

            string filter = textOnly ? " (text primitives only)" : "";
            string kinds = countByKind.Count == 0
                ? "none"
                : string.Join(", ", countByKind.Select(p => $"{p.Key} {p.Value}"));
            string result = $"{boardName}: {lines.Count} primitives on the Drill Drawing layer{filter} written to {path}" +
                            Environment.NewLine + "By kind: " + kinds;
            if (emptyCount > 0)
                result += Environment.NewLine + $"{emptyCount} primitives returned an empty parameter string.";
            return result;
        }

        /// <summary>
        /// AddTestText "Text=&lt;string&gt;": helper. Places one text object on the Drill
        /// Drawing layer at the board origin + 500 mil, so ExportDrillDrawing has a known primitive to find on boards
        /// without a drill table.
        /// </summary>
        [HotCommand("AddTestText", "Place a text on the drill drawing layer (test setup)")]
        public static string AddTestText(CommandContext ctx)
        {
            string text = ctx.Get("Text", DefaultTestText);
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = CurrentBoard();

            pcbServer.PreProcess();
            try
            {
                var textObj = pcbServer.PCBObjectFactory(TObjectId.eTextObject, TDimensionKind.eNoDimension,
                                                         TObjectCreationMode.eCreate_Default) as IPCB_Text
                              ?? throw new UserException("Could not create a text object.");
                textObj.SetState_XLocation(board.GetState_XOrigin() + EDP.Utils.MilsToCoord(500));
                textObj.SetState_YLocation(board.GetState_YOrigin() + EDP.Utils.MilsToCoord(500));
                textObj.SetState_Layer(TV6_Layer.eV6_DrillDrawing);
                textObj.SetState_Text(text);
                textObj.SetState_Size(EDP.Utils.MilsToCoord(60));
                // AddPCBObject notifies the board (and its undo history) itself (pcb-basics guide).
                board.AddPCBObject(textObj);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            return $"Placed text '{text}' on the Drill Drawing layer of {Path.GetFileName(board.GetState_FileName())}. " +
                   "Close the document without saving to discard it.";
        }

        private const string DefaultTestText = "DrillDrawingLayerExport_Test";

        /// <summary>Every object kind of TObjectId except eNoObject: an all-objects set.</summary>
        private static PCB.TObjectSet AllObjects()
        {
            var set = new PCB.TObjectSet();
            foreach (TObjectId id in Enum.GetValues(typeof(TObjectId)))
            {
                if (id != TObjectId.eNoObject)
                    set.Add(id);
            }
            return set;
        }

        /// <summary>Out may be a folder (the file name is added) or a file path; default: the temp folder.</summary>
        private static string OutputPath(CommandContext ctx, string boardName)
        {
            string fileName = "DrillDrawing_" + Path.GetFileNameWithoutExtension(boardName) + ".txt";
            string path = ctx.Get("Out", Path.GetTempPath());
            if (Directory.Exists(path))
                path = Path.Combine(path, fileName);
            path = Path.GetFullPath(path);

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                throw new UserException($"The folder '{folder}' does not exist.");
            return path;
        }

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
