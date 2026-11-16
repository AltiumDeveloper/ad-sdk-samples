using System;
using PCB;

namespace AdSamples.BoardPadCount
{
    /// <summary>
    /// Counts the pads of the current PCB board with a board iterator: an object filter (pads only), a layer filter
    /// (all layers) and an iteration method (free and component pads).
    /// </summary>
    internal static class BoardPadCountCommands
    {
        /// <summary>
        /// CountPads ["LayerFilter=All|Partition|None"]: walks every pad of the board and returns the count.
        /// LayerFilter chooses how the iterator is told to search all layers (see the README):
        /// All = AddFilter_AllLayers (default), Partition = AddFilter_LayerSet_2(PCBConstant.AllLayersSet),
        /// None = no layer filter (the board iterator's default layers).
        /// </summary>
        [HotCommand("CountPads", "Count the pads on the current board")]
        public static string CountPads(CommandContext ctx)
        {
            string layerFilter = ctx.Get("LayerFilter", "All");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            string fileName = System.IO.Path.GetFileName(board.GetState_FileName());

            int padCount = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // Only pads. The object set is a collection initializer over TObjectId values.
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.ePadObject });

                // Pads live on the multi-layer (through-hole) or on a single copper layer (SMD); both must be searched.
                if (layerFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                    iterator.AddFilter_AllLayers();
                else if (layerFilter.Equals("Partition", StringComparison.OrdinalIgnoreCase))
                    // In .NET the AddFilter_LayerSet_2 overload takes a
                    // TLayerPartitionSet. It filters by the layers of that set.
                    iterator.AddFilter_LayerSet_2(PCBConstant.AllLayersSet);
                else if (!layerFilter.Equals("None", StringComparison.OrdinalIgnoreCase))
                    throw new UserException($"Unknown LayerFilter '{layerFilter}'. Use All, Partition or None.");

                // eProcessAll: free pads and the pads of components (eProcessFree / eProcessComponent select one kind).
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                // FirstPCBObject/NextPCBObject return null after the last object. There is
                // no Release() per primitive: the .NET runtime manages the COM references.
                for (IPCB_Primitive pad = iterator.FirstPCBObject(); pad != null; pad = iterator.NextPCBObject())
                {
                    padCount++;
                    if (padCount % 1000 == 0)
                        ctx.Log($"{padCount} pads so far...");
                }
            }
            finally
            {
                // Destroy takes the iterator by ref and sets the variable to null.
                board.BoardIterator_Destroy(ref iterator);
            }

            return $"{fileName}: {padCount} pad(s) (LayerFilter={layerFilter})";
        }
    }
}
