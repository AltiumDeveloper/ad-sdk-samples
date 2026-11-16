using System.Collections.Generic;
using DXP;

namespace AdSamples.SchGridOptionsRead
{
    /// <summary>
    /// Reads a server's stored preferences through the client's options manager. By default it reads the grid values
    /// of the schematic editor (server "SCH", section "Schematic Preferences").
    /// </summary>
    internal static class SchGridOptionsReadCommands
    {
        // The grid value names. They are not documented in the SDK reference.
        private static readonly string[] GridValueNames =
        {
            "Grid Color", "Grid Style", "ImperialGridPresets", "MetricGridPresets",
        };

        /// <summary>
        /// ReadOptions ["Server=SCH|Section=Schematic Preferences"]: reads the grid values as integers and
        /// reports, for each one, whether it is stored at all.
        /// </summary>
        [HotCommand("ReadOptions", "Read the schematic grid preferences")]
        public static string ReadOptions(CommandContext ctx)
        {
            string server = ctx.Get("Server", "SCH");
            string section = ctx.Get("Section", "Schematic Preferences");

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // GetOptionsManager is an extension method from `using DXP;`, not a Client.OptionsManager property.
            // The second argument, argOldSettingsPath, is passed empty as in the docs' example.
            IOptionsManager manager = client.GetOptionsManager()
                ?? throw new UserException("The client returned no options manager.");
            IOptionsReader reader = manager.GetOptionsReader(server, "")
                ?? throw new UserException($"No options reader for server '{server}'.");

            if (!reader.SectionExists(section))
                throw new UserException($"Server '{server}' has no options section '{section}'.");

            // ReadInteger returns the default (0) for a missing value, so a missing value and a stored 0 look the
            // same. ValueExists tells them apart.
            var parts = new List<string>();
            foreach (string valueName in GridValueNames)
            {
                bool exists = reader.ValueExists(section, valueName);
                int value = reader.ReadInteger(section, valueName, 0);
                parts.Add(exists ? $"{valueName} = {value}" : $"{valueName} = {value} (not stored, default)");
            }

            return $"{server} / {section}: " + string.Join("; ", parts);
        }
    }
}
