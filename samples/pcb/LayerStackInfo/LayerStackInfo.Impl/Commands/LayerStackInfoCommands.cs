using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DXP; // IClient.OpenDocument / ShowDocument are typed extension methods
using PCB; // Iterator_1, LayerObject, GetState_MasterStack and GetCurrentPCBBoard are typed extension methods

namespace AdSamples.LayerStackInfo
{
    /// <summary>
    /// Walks the board's master layer stack with an iterator filtered by layer class, casts each layer object to the
    /// typed interface of that class, and reports its properties in millimetres.
    /// </summary>
    internal static class LayerStackInfoCommands
    {
        /// <summary>InternalPlaneInfo ["Out=&lt;file&gt;"]: name, copper thickness and pull-back of each internal plane.</summary>
        [HotCommand("InternalPlaneInfo", "List the internal plane layers of the current board")]
        public static string InternalPlaneInfo(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            var lines = new List<string>();

            // The iterator only visits layers of the class it was created for, so the cast below is safe. A C# type
            // test would not prove it: SDK interfaces are dispatch interfaces and "as" succeeds for any layer object.
            IPCB_LayerObjectIterator iterator = board.GetState_MasterStack().Iterator_1(TLayerClassID.eLayerClass_InternalPlane);
            iterator.SetBeforeFirst();
            while (iterator.Next())
            {
                var plane = (IPCB_InternalPlane)iterator.LayerObject();
                // CopperThickness and PullBackDistance are coordinates in internal units (10,000 per mil).
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "LayerName : {0}; Type : Internal Plane; Material : Copper; Thickness : {1:0.0000} mm; Pullback : {2:0.0000} mm; Net : {3}",
                    plane.GetState_LayerName(),
                    EDP.Utils.CoordToMMs(plane.GetState_CopperThickness()),
                    EDP.Utils.CoordToMMs(plane.GetState_PullBackDistance()),
                    plane.GetState_NetName()));
            }

            return Report(ctx, board, "internal plane", lines);
        }

        /// <summary>DielectricInfo ["Out=&lt;file&gt;"]: type, thickness, material and constant of each dielectric layer.</summary>
        [HotCommand("DielectricInfo", "List the dielectric layers of the current board")]
        public static string DielectricInfo(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            var lines = new List<string>();

            IPCB_LayerObjectIterator iterator = board.GetState_MasterStack().Iterator_1(TLayerClassID.eLayerClass_Dielectric);
            iterator.SetBeforeFirst();
            while (iterator.Next())
            {
                var dielectric = (IPCB_DielectricLayer)iterator.LayerObject();
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "LayerName : {0}; Type : Dielectric; Material : {1}; Thickness : {2:0.0000} mm; Dielectric Material : {3}; Dielectric Constant : {4:0.00}",
                    dielectric.GetState_LayerName(),
                    DielectricTypeName(dielectric.GetState_DielectricType()),
                    EDP.Utils.CoordToMMs(dielectric.GetState_DielectricHeight()), // internal units, like any coordinate
                    dielectric.GetState_DielectricMaterial(),
                    dielectric.GetState_DielectricConstant()));
            }

            return Report(ctx, board, "dielectric", lines);
        }

        // The .NET SDK has no table of dielectric type names, so the labels are mapped here.
        private static string DielectricTypeName(TDielectricType type)
        {
            switch (type)
            {
                case TDielectricType.eNoDielectric: return "None";
                case TDielectricType.eCore: return "Core";
                case TDielectricType.ePrePreg: return "Prepreg";
                case TDielectricType.eSurfaceMaterial: return "Surface Material";
                case TDielectricType.eFilm: return "Film";
                default: return type.ToString();
            }
        }

        /// <summary>
        /// Returns the lines as text. With "Out=&lt;file&gt;" also writes them to that file and, when run from the
        /// menu, opens it in Altium Designer's text editor.
        /// </summary>
        private static string Report(CommandContext ctx, IPCB_Board board, string kind, List<string> lines)
        {
            string header = $"{Path.GetFileName(board.GetState_FileName())}: {lines.Count} {kind} layer(s)";
            string text = lines.Count == 0 ? header + "." : header + ":\n" + string.Join("\n", lines);

            string outFile = ctx.Get("Out");
            if (string.IsNullOrEmpty(outFile))
                return text;

            outFile = Path.GetFullPath(outFile);
            File.WriteAllLines(outFile, lines);
            if (ctx.IsInteractive)
            {
                DXP.IClient client = DXP.GlobalVars.Client;
                client.ShowDocument(client.OpenDocument("Text", outFile));
            }
            return text + $"\nWritten to {outFile}";
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
