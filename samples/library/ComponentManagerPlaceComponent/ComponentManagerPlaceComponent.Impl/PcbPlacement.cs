using PCB;

namespace AdSamples.ComponentManagerPlaceComponent
{
    /// <summary>Small PCB helpers.</summary>
    internal static class PcbPlacement
    {
        /// <summary>The footprint (Pattern) of the first component on the board, or null.</summary>
        public static string FirstFootprint(IPCB_Board board, out string designator)
        {
            designator = null;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var component = iterator.FirstPCBObject() as IPCB_Component; component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    string pattern = component.GetState_Pattern();
                    if (!string.IsNullOrEmpty(pattern))
                    {
                        designator = component.GetState_Name()?.GetState_Text();
                        return pattern;
                    }
                }
                return null;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }

        public static int CountComponents(IPCB_Board board)
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
    }
}
