using AdSamples.PlacePadInteractive;

namespace AdSamples
{
    internal static partial class SampleHooks
    {
        /// <summary>
        /// Before this implementation is unloaded (hot reload or unload): end a placement in progress. Its Windows
        /// hooks point into this assembly; left installed, the next mouse message would crash Altium Designer.
        /// </summary>
        static partial void OnUnloadCore()
        {
            try
            {
                PadPlacement.Active?.Finish(keep: false, "the implementation was unloaded");
            }
            catch
            {
                // Unloading must not fail; Finish has already removed the hooks before touching the board.
            }
        }
    }
}
