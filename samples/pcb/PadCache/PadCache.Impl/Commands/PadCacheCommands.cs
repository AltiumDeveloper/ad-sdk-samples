using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.PadCache
{
    /// <summary>
    /// Marks the solder mask expansion in a pad's cache (V7_PadCache) as invalid, by reading the cache, changing the
    /// SolderMaskExpansionValid field and assigning the cache back to the pad.
    /// </summary>
    internal static class PadCacheCommands
    {
        /// <summary>
        /// InvalidatePadSolderMaskCache "All=True|Method=Assign|Invalidate": sets SolderMaskExpansionValid to
        /// eCacheInvalid on the first pad of the board (All=True: every pad) and reports the state before and after.
        /// Method=Assign (default) is the read-modify-assign of the cache; Method=Invalidate calls
        /// IPCB_Pad.InvalidateCache instead, which never assigns a cache record.
        /// </summary>
        [HotCommand("InvalidatePadSolderMaskCache", "Mark the solder mask cache of the first pad invalid (All=True: every pad)")]
        public static string InvalidatePadSolderMaskCache(CommandContext ctx)
        {
            bool all = ctx.Flag("All");
            string method = ctx.Get("Method", "Assign");
            bool assign;
            if (string.Equals(method, "Assign", StringComparison.OrdinalIgnoreCase))
                assign = true;
            else if (string.Equals(method, "Invalidate", StringComparison.OrdinalIgnoreCase))
                assign = false;
            else
                throw new UserException("Method must be Assign or Invalidate.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = CurrentBoard(pcbServer);

            // Collect first, change afterwards: the pads are not modified while the iterator walks the board.
            List<IPCB_Pad> pads = FindPads(board, all ? int.MaxValue : 1);
            if (pads.Count == 0)
                return $"{BoardName(board)}: the board has no pads, nothing changed.";

            string before = Describe(pads[0], pads[0].GetState_Cache());

            // PreProcess/BeginModify/EndModify/PostProcess is the documented way to change PCB objects (docs: PCB basics).
            pcbServer.PreProcess();
            try
            {
                foreach (IPCB_Pad pad in pads)
                {
                    pad.BeginModify();
                    if (assign)
                    {
                        // GetState_Cache returns a V7_PadCache holding a copy of the pad's cache: a change takes
                        // effect only when the cache is assigned back with SetState_Cache.
                        V7_PadCache cache = pad.GetState_Cache();
                        cache.SolderMaskExpansionValid = TCacheState.eCacheInvalid;
                        pad.SetState_Cache(cache);
                    }
                    else
                    {
                        // The docs say this marks the pad's cached design rule values out of date; its exact effect
                        // on the *Valid fields of Cache is not documented (ReadPadCache shows it).
                        pad.InvalidateCache();
                    }
                    pad.EndModify();
                    pad.GraphicallyInvalidate();
                }
            }
            finally
            {
                pcbServer.PostProcess();
            }

            // Read the cache back so the result shows what the pad holds now, instead of claiming an effect.
            string after = Describe(pads[0], pads[0].GetState_Cache());
            string scope = all ? $"{pads.Count} pads" : "the first pad";
            // Word the result per method: Method=Invalidate does not necessarily touch the solder mask state
            // (it changes only the relief air gap and power plane clearance states), so only
            // the Assign path may claim what it set. Both paths let the reader compare the lines below.
            string what = assign
                ? $"set SolderMaskExpansionValid to eCacheInvalid in the cache of {scope} (Method=Assign)"
                : $"called InvalidateCache on {scope} (Method=Invalidate); which cache states that changes is up " +
                  "to Altium Designer, compare the lines below";
            return $"{BoardName(board)}: {what}." +
                   Environment.NewLine + "    before: " + before +
                   Environment.NewLine + "    after:  " + after +
                   Environment.NewLine + "The change is in memory only.";
        }

        /// <summary>
        /// ReadPadCache: reports the cache fields of the first pad, the one
        /// InvalidatePadSolderMaskCache changes, so the effect of that command can be checked.
        /// </summary>
        [HotCommand("ReadPadCache", "Show the cache of the first pad")]
        public static string ReadPadCache(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = CurrentBoard(pcbServer);

            List<IPCB_Pad> pads = FindPads(board, 1);
            if (pads.Count == 0)
                return $"{BoardName(board)}: the board has no pads.";
            return $"{BoardName(board)}: first pad " + Describe(pads[0], pads[0].GetState_Cache());
        }

        private static IPCB_Board CurrentBoard(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetCurrentPCBBoard()
            ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

        /// <summary>
        /// Up to <paramref name="max"/> pads of the board in iteration order. eProcessAll also visits pads inside
        /// components, per the iterator docs.
        /// </summary>
        private static List<IPCB_Pad> FindPads(IPCB_Board board, int max)
        {
            var pads = new List<IPCB_Pad>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // Filters: ePadObject, cAllLayers, eProcessAll.
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_LayerSet_2(PCBConstant.AllLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var pad = iterator.FirstPCBObject() as IPCB_Pad;
                     pad != null && pads.Count < max;
                     pad = iterator.NextPCBObject() as IPCB_Pad)
                {
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
            $"{pad.GetState_DescriptorString()}: " +
            $"SolderMaskExpansion={Coord(cache.SolderMaskExpansion)} ({cache.SolderMaskExpansionValid}), " +
            $"PasteMaskExpansion={Coord(cache.PasteMaskExpansion)} ({cache.PasteMaskExpansionValid}), " +
            // Fields this sample never changes, listed so a run can confirm that assigning the cache keeps them.
            $"ReliefAirGap={Coord(cache.ReliefAirGap)} ({cache.ReliefAirGapValid}), " +
            $"PowerPlaneClearance={Coord(cache.PowerPlaneClearance)} ({cache.PowerPlaneClearanceValid}); " +
            $"pad SolderMaskExpansion={Coord(pad.GetState_SolderMaskExpansion())}";

        // Expansions are internal coordinates; EDP.Utils converts them to mils.
        private static string Coord(int value) =>
            $"{EDP.Utils.CoordToMils(value).ToString("0.###", CultureInfo.InvariantCulture)} mil ({value})";

        private static string BoardName(IPCB_Board board) => Path.GetFileName(board.GetState_FileName());
    }
}
