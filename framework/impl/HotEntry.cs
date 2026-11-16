using System.Collections.Generic;
using System.Linq;

namespace AdSamples
{
    /// <summary>
    /// Entry point the shim binds to: by name through the hot-reload host in Debug, as a normal call in Release.
    /// BCL-only signatures. Do not rename or change them. (Shared framework file: framework\impl\HotEntry.cs.)
    /// </summary>
    public static class HotEntry
    {
        public static string Invoke(string command, string parameters, IDictionary<string, object> env) =>
            CommandRegistry.Execute(new CommandContext(command, parameters, env));

        public static string[] GetCommands() =>
            CommandRegistry.All.Select(c => c.Name + "\t" + c.Description).ToArray();

        /// <summary>
        /// After this generation loads (env "Origin" = "Host"): in Debug on its first command and on every hot reload,
        /// in Release once, when the extension starts.
        /// </summary>
        public static void OnLoad(IDictionary<string, object> env)
        {
#if SAMPLE_PANEL
            SamplePanel.Attach();
#endif
            SampleHooks.OnLoad(env);
        }

        /// <summary>
        /// Debug only: called before this generation is unloaded. Release anything that would keep it alive: static
        /// event subscriptions, timers, threads, open forms, controls inside the shim's (such as the panel content).
        /// </summary>
        public static void OnUnload()
        {
            SampleHooks.OnUnload();
#if SAMPLE_PANEL
            SamplePanel.Detach();
#endif
        }
    }

    /// <summary>
    /// Optional per-sample lifecycle hooks: implement either method in a <c>partial class SampleHooks</c> of the
    /// sample (namespace AdSamples). Unimplemented hooks compile away.
    /// </summary>
    internal static partial class SampleHooks
    {
        static partial void OnLoadCore(IDictionary<string, object> env);
        static partial void OnUnloadCore();

        internal static void OnLoad(IDictionary<string, object> env) => OnLoadCore(env);
        internal static void OnUnload() => OnUnloadCore();
    }
}
