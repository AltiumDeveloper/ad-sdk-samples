using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.PadSolderMask
{
    /// <summary>
    /// Switches the solder mask expansion of named pads to manual, with separate top and bottom values, by editing
    /// the pad's cache (V7_PadCache): read the cache, change it, assign it back.
    /// </summary>
    internal static class PadSolderMaskCommands
    {
        // The default of the Pad parameter.
        private const string DefaultPadName = "M3";

        /// <summary>
        /// ChangePadSolderMask "Pad=&lt;name&gt;|Top=&lt;mils&gt;|Bottom=&lt;mils&gt;": for every pad of that name, sets the
        /// cache's SolderMaskExpansionValid to eCacheManual and UseSeparateExpansions to true. Top and Bottom
        /// (optional) also set the two expansion values.
        /// </summary>
        [HotCommand("ChangePadSolderMask", "Set manual per-side solder mask on pads named M3")]
        public static string ChangePadSolderMask(CommandContext ctx)
        {
            string padName = ctx.Get("Pad", DefaultPadName);
            int? top = Mils(ctx, "Top");
            int? bottom = Mils(ctx, "Bottom");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            // Collect first, change afterwards: the pads are not modified while the iterator walks the board.
            List<IPCB_Pad> pads = FindPads(board, padName);
            if (pads.Count == 0)
                return $"{BoardName(board)}: no pads named '{padName}', nothing changed.";

            // PreProcess/BeginModify/EndModify/PostProcess is the documented way to change PCB objects (docs: PCB basics, "Changing objects").
            pcbServer.PreProcess();
            try
            {
                foreach (IPCB_Pad pad in pads)
                {
                    pad.BeginModify();
                    // Per the IPCB_Pad.Cache remarks, read the cache, change a field and assign it back with
                    // SetState_Cache. Assigning back applies the change.
                    V7_PadCache cache = pad.GetState_Cache();
                    cache.SolderMaskExpansionValid = TCacheState.eCacheManual;
                    cache.UseSeparateExpansions = true;
                    if (top.HasValue)
                        cache.SolderMaskExpansion = top.Value;
                    if (bottom.HasValue)
                        cache.SolderMaskBottomExpansion = bottom.Value;
                    pad.SetState_Cache(cache);
                    pad.EndModify();
                    pad.GraphicallyInvalidate();
                }
            }
            finally
            {
                pcbServer.PostProcess();
            }

            // Read the cache back from the first pad so the result shows what the pad really holds now.
            V7_PadCache after = pads[0].GetState_Cache();
            return $"{BoardName(board)}: changed {pads.Count} pads named '{padName}' to a manual solder mask with " +
                   $"separate expansions. First pad now: {Describe(pads[0], after)}. Save the document to keep it.";
        }

        /// <summary>
        /// ShowPadSolderMask "Pad=&lt;name&gt;|Max=&lt;n&gt;": read-back helper. Lists the solder mask cache
        /// fields of the pads of that name (at most Max, default 10).
        /// </summary>
        [HotCommand("ShowPadSolderMask", "Show the solder mask settings of pads named M3")]
        public static string ShowPadSolderMask(CommandContext ctx)
        {
            string padName = ctx.Get("Pad", DefaultPadName);
            if (!int.TryParse(ctx.Get("Max", "10"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException("Max must be a positive whole number.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            List<IPCB_Pad> pads = FindPads(board, padName);
            var lines = new List<string> { $"{BoardName(board)}: {pads.Count} pads named '{padName}'." };
            for (int i = 0; i < pads.Count && i < max; i++)
                lines.Add("    " + Describe(pads[i], pads[i].GetState_Cache()));
            if (pads.Count > max)
                lines.Add($"    ... {pads.Count - max} more (Max={max}).");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>All pads of the board, free and in components, whose Name equals <paramref name="padName"/>.</summary>
        private static List<IPCB_Pad> FindPads(IPCB_Board board, string padName)
        {
            var pads = new List<IPCB_Pad>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_LayerSet_2(PCBConstant.AllLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                // Pad names are not unique on a board: every component's pad "1" matches Pad=1.
                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                {
                    if (pad.GetState_Name() == padName)
                        pads.Add(pad);
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return pads;
        }

        private static string Describe(IPCB_Pad pad, V7_PadCache cache) =>
            $"{pad.GetState_DescriptorString()}: SolderMaskExpansionValid={cache.SolderMaskExpansionValid}, " +
            $"UseSeparateExpansions={cache.UseSeparateExpansions}, " +
            $"Top={Coord(cache.SolderMaskExpansion)}, Bottom={Coord(cache.SolderMaskBottomExpansion)}, " +
            $"pad SolderMaskExpansion={Coord(pad.GetState_SolderMaskExpansion())}; " +
            // Fields this sample never changes, listed so a run can confirm that assigning the cache keeps them.
            $"PasteMask={Coord(cache.PasteMaskExpansion)} ({cache.PasteMaskExpansionValid}), " +
            $"ReliefAirGap={Coord(cache.ReliefAirGap)} ({cache.ReliefAirGapValid})";

        // Expansions are internal coordinates; EDP.Utils converts mils to and from them.
        private static string Coord(int value) =>
            $"{EDP.Utils.CoordToMils(value).ToString("0.###", CultureInfo.InvariantCulture)} mil ({value})";

        private static int? Mils(CommandContext ctx, string name)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double mils))
                throw new UserException($"{name} must be a number of mils, for example {name}=4.");
            return EDP.Utils.MilsToCoord(mils);
        }

        private static string BoardName(IPCB_Board board) => Path.GetFileName(board.GetState_FileName());
    }
}
