using System.Text;
using DXP;

namespace AdSamples.GetExtensionVersion
{
    /// <summary>
    /// Looks up installed extensions in the plugin registry of the running Altium Designer. The registry is reached
    /// through the client (DXP.GlobalVars.Client); each entry is an IPluginsRegistryItem.
    /// </summary>
    internal static class GetExtensionVersionCommands
    {
        // The HRID GetVersion looks up when called without parameters.
        private const string DefaultHrid = "Altium Developer";

        /// <summary>
        /// GetVersion "Hrid=&lt;HRID&gt;": finds an extension by its HRID and returns its version, or says that it
        /// is not installed.
        /// </summary>
        [HotCommand("GetVersion", "Show the version of an installed extension")]
        public static string GetVersion(CommandContext ctx)
        {
            string hrid = ctx.Get("Hrid", DefaultHrid);
            if (string.IsNullOrWhiteSpace(hrid))
                throw new UserException("No extension specified. Pass its HRID as a parameter: Hrid=<HRID>.");

            IPluginsRegistry registry = Registry();

            // FindItemByHRID and GetItem are typed helper (extension) methods: `using DXP;` makes them callable
            // like interface members. FindItemByHRID returns null for an unknown HRID and matches the HRID
            // case-insensitively; GetHRID() gives the registered spelling.
            IPluginsRegistryItem item = registry.FindItemByHRID(hrid);
            if (item == null)
                return $"Extension '{hrid}' is not installed.";

            string version = item.GetVersion();
            return string.IsNullOrEmpty(version)
                ? $"Installed extension '{item.GetHRID()}' has no version string."
                : $"Installed extension '{item.GetHRID()}' is version {version}.";
        }

        /// <summary>
        /// ListExtensions: one line per registry entry (HRID, version, status). It shows
        /// which HRIDs GetVersion can find.
        /// </summary>
        [HotCommand("ListExtensions", "List the extensions in the plugin registry")]
        public static string ListExtensions(CommandContext ctx)
        {
            IPluginsRegistry registry = Registry();
            int count = registry.GetCount();

            var text = new StringBuilder();
            text.AppendLine($"{count} extension(s) in the plugin registry:");
            for (int i = 0; i < count; i++)
            {
                IPluginsRegistryItem item = registry.GetItem(i);
                if (item == null)
                {
                    text.AppendLine($"  [{i}] (no item)");
                    continue;
                }

                string version = item.GetVersion();
                text.AppendLine(
                    $"  {item.GetHRID()}  version={(string.IsNullOrEmpty(version) ? "(none)" : version)}  status={item.GetStatus()}");
            }

            return text.ToString().TrimEnd();
        }

        private static IPluginsRegistry Registry()
        {
            // Re-queried on every call: the registry can change while Altium Designer runs (extensions are
            // installed or removed), and COM objects are not cached in statics.
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            return client.GetPluginsRegistry()
                ?? throw new UserException("The client returned no plugin registry.");
        }
    }
}
