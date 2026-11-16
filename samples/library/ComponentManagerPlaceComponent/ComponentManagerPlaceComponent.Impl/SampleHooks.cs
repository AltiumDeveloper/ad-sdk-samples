using AdSamples.ComponentManagerPlaceComponent;

namespace AdSamples
{
    internal static partial class SampleHooks
    {
        /// <summary>
        /// Before this implementation is unloaded (hot reload or unload): stop the test server, so that its
        /// background thread does not keep running code of an unloaded assembly.
        /// </summary>
        static partial void OnUnloadCore()
        {
            try
            {
                TestServer.Stop();
            }
            catch
            {
                // Unloading must not fail.
            }
        }
    }
}
