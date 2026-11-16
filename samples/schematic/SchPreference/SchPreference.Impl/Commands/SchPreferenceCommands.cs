using DXP;

namespace AdSamples.SchPreference
{
    /// <summary>
    /// Reads and toggles a boolean option of the schematic editor (server "SCH") through the server options
    /// reader/writer, then asks the SCH server module to reload its options.
    /// </summary>
    internal static class SchPreferenceCommands
    {
        // The server whose options are read and written. Section and key are parameters with these defaults.
        private const string ServerName = "SCH";
        private const string DefaultSection = "Schematic Preferences";
        private const string DefaultKey = "ResetPartsDesignatorsOnPaste";

        // The option is read with this default, so an absent value counts as true.
        private const bool DefaultValue = true;

        /// <summary>
        /// ChangeSchPreference "Section=&lt;section&gt;|Key=&lt;option&gt;": writes the negated value of a boolean
        /// option and reloads the SCH options. Running it twice restores the previous value.
        /// </summary>
        [HotCommand("ChangeSchPreference", "Toggle a boolean schematic preference")]
        public static string ChangeSchPreference(CommandContext ctx)
        {
            string section = ctx.Get("Section", DefaultSection);
            string key = ctx.Get("Key", DefaultKey);

            IOptionsReader reader = DXP.Utils.ServerOptionsReader(ServerName)
                ?? throw new UserException($"No options reader for server '{ServerName}'.");
            IOptionsWriter writer = DXP.Utils.ServerOptionsWriter(ServerName)
                ?? throw new UserException($"No options writer for server '{ServerName}'.");

            bool existed = reader.ValueExists(section, key);
            bool previous = reader.ReadBoolean(section, key, DefaultValue);
            writer.WriteBoolean(section, key, !previous);

            // The writer stores the value; the SCH server keeps its own copy of the options, so it is asked to
            // reload them. GetServerModuleByName returns null when the SCH server is not loaded (no schematic
            // opened yet in this session); the result then says that nothing was reloaded.
            string reload;
            IServerModule schModule = DXP.GlobalVars.Client.GetServerModuleByName(ServerName);
            if (schModule == null)
                reload = $"server '{ServerName}' is not loaded, options not reloaded";
            else if (schModule is IServerOptions serverOptions)
            {
                serverOptions.ReloadOptions();
                reload = $"server '{ServerName}' reloaded its options";
            }
            else
                reload = $"server '{ServerName}' does not implement IServerOptions, options not reloaded";

            // Read back through a fresh reader, so the result shows what is stored now, not what was intended.
            bool now = DXP.Utils.ServerOptionsReader(ServerName).ReadBoolean(section, key, DefaultValue);

            return $"{ServerName} [{section}] {key}: {Format(previous, existed)} -> {now.ToString().ToLowerInvariant()} ({reload}).";
        }

        /// <summary>ReadSchPreference "Section=&lt;section&gt;|Key=&lt;option&gt;": shows the current value.</summary>
        [HotCommand("ReadSchPreference", "Show a boolean schematic preference")]
        public static string ReadSchPreference(CommandContext ctx)
        {
            string section = ctx.Get("Section", DefaultSection);
            string key = ctx.Get("Key", DefaultKey);

            IOptionsReader reader = DXP.Utils.ServerOptionsReader(ServerName)
                ?? throw new UserException($"No options reader for server '{ServerName}'.");

            bool sectionExists = reader.SectionExists(section);
            bool existed = reader.ValueExists(section, key);
            bool value = reader.ReadBoolean(section, key, DefaultValue);

            return $"{ServerName} [{section}] {key} = {Format(value, existed)}" +
                   (sectionExists ? "." : $" (section '{section}' not found).");
        }

        private static string Format(bool value, bool existed) =>
            existed ? value.ToString().ToLowerInvariant() : $"{value.ToString().ToLowerInvariant()} (absent, default)";
    }
}
