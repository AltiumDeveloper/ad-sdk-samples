using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB; // Iterator_1, LayerObject, GetState_MasterStack and GetCurrentPCBBoard are typed extension methods

namespace AdSamples.DielectricLossTangent
{
    /// <summary>
    /// Reads the loss tangent (Df) of each dielectric layer of the board's master layer stack through the typed
    /// IPCB_DielectricLayer.DielectricLossTangent property. The .NET API exposes it directly, so no raw
    /// COM dispatch ID is needed.
    /// </summary>
    internal static class DielectricLossTangentCommands
    {
        /// <summary>GetDfValue ["Out=&lt;file&gt;"]: name, type, material, Dk and Df of each dielectric layer.</summary>
        [HotCommand("GetDfValue", "List the dielectric layers with their loss tangent (Df)")]
        public static string GetDfValue(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_MasterLayerStack stack = board.GetState_MasterStack()
                ?? throw new UserException("The board has no master layer stack.");

            var lines = new List<string>();

            // The iterator only visits layers of the class it was created for, so the cast below is safe.
            // The .NET API has no IsDielectricLayer() helper, and a C# type test would not
            // help either: SDK interfaces are dispatch interfaces, so "as" succeeds for any layer object.
            IPCB_LayerObjectIterator iterator = stack.Iterator_1(TLayerClassID.eLayerClass_Dielectric);
            iterator.SetBeforeFirst();
            while (iterator.Next())
            {
                var dielectric = (IPCB_DielectricLayer)iterator.LayerObject();

                // The SDK lesson: a typed getter, no raw COM dispatch.
                double df = dielectric.GetState_DielectricLossTangent();

                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "LayerName : {0}; Type : {1}; Dielectric Material : {2}; Dk : {3}; Df : {4}",
                    dielectric.GetState_LayerName(),
                    DielectricTypeName(dielectric.GetState_DielectricType()),
                    dielectric.GetState_DielectricMaterial(),
                    dielectric.GetState_DielectricConstant().ToString("R", CultureInfo.InvariantCulture),
                    df.ToString("R", CultureInfo.InvariantCulture)));
            }

            string header = $"{Path.GetFileName(board.GetState_FileName())}: {lines.Count} dielectric layer(s)";
            string text = lines.Count == 0 ? header + "." : header + ":\n" + string.Join("\n", lines);

            string outFile = ctx.Get("Out");
            if (string.IsNullOrEmpty(outFile))
                return text;

            outFile = Path.GetFullPath(outFile);
            File.WriteAllLines(outFile, lines);
            return text + $"\nWritten to {outFile}";
        }

        // TDielectricType has no display-name table in .NET.
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

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
