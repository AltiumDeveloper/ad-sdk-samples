using System;
using System.IO;
using System.Text;
using DXP;

namespace AdSamples.PreferencesOptionsPage
{
    /// <summary>
    /// Pipe-testable counterparts of the Preferences page (which lives in the shim, PreferencesPage.cs). They use the
    /// same options store as the page: DXP.Utils.ServerOptionsReader / ServerOptionsWriter under the server name,
    /// section "General", value "Temperature".
    /// </summary>
    internal static class PreferencesOptionsPageCommands
    {
        private const string Section = "General";
        private const string Key = "Temperature";
        private const string DefaultTemperature = "100";

        // AppDomain data published by the shim (PreferenceStore.Publish): same strings as there.
        private const string StatusKey = SampleInfo.Name + ".Status";
        private const string CachedKey = SampleInfo.Name + ".Cached";
        private const string ReloadKey = SampleInfo.Name + ".Reload";
        private const string WriteIniKey = SampleInfo.Name + ".WriteIni";
        private const string ReadIniKey = SampleInfo.Name + ".ReadIni";

        [HotCommand("ShowPreference", "Show the stored Temperature preference")]
        public static string ShowPreference(CommandContext ctx)
        {
            IOptionsReader reader = Utils.ServerOptionsReader(SampleInfo.Name);
            var sb = new StringBuilder();
            sb.AppendLine($"Stored [{Section}] {Key} = '{reader.ReadString(Section, Key, DefaultTemperature)}' " +
                          $"(ValueExists: {reader.ValueExists(Section, Key)}, default '{DefaultTemperature}')");
            sb.AppendLine("Cached by the page: " + (Shim<Func<string>>(CachedKey) is { } cached ? $"'{cached()}'" : "(shim state not available)"));
            sb.Append(Shim<Func<string>>(StatusKey)?.Invoke() ?? "Shim status: not available (is the deployed shim of this sample loaded?)");
            return sb.ToString();
        }

        [HotCommand("SetPreference", "Write the Temperature preference")]
        public static string SetPreference(CommandContext ctx)
        {
            IOptionsWriter writer = Utils.ServerOptionsWriter(SampleInfo.Name);
            string action;
            if (ctx.Flag("Erase"))
            {
                writer.EraseSection(Section);
                action = $"Erased section [{Section}].";
            }
            else
            {
                var value = ctx.Get("Temperature");
                if (string.IsNullOrWhiteSpace(value))
                    throw new UserException("Give a value: Temperature=<text> (or Erase to remove the section).");
                writer.WriteString(Section, Key, value);
                action = $"Wrote [{Section}] {Key} = '{value}'.";
            }
            writer.Flush();

            // Read back through a new reader, then refresh the page's cache so the next Preferences dialog shows it.
            IOptionsReader reader = Utils.ServerOptionsReader(SampleInfo.Name);
            var readBack = reader.ReadString(Section, Key, DefaultTemperature);
            var reloaded = Shim<Func<string>>(ReloadKey)?.Invoke();
            return action + Environment.NewLine +
                   $"Read back: '{readBack}' (ValueExists: {reader.ValueExists(Section, Key)})" + Environment.NewLine +
                   "Page cache reloaded: " + (reloaded != null ? $"'{reloaded}'" : "(shim state not available)");
        }

        [HotCommand("ExportPreferencesFolder", "Show the Preferences export folder")]
        public static string ExportPreferencesFolder(CommandContext ctx)
        {
            var folder = GlobalVars.Client?.GetClientAPI()?.SpecialFolder_ExportPreferences();
            if (string.IsNullOrEmpty(folder))
                throw new UserException("IClientAPI_Interface.SpecialFolder_ExportPreferences returned nothing.");

            var sb = new StringBuilder();
            sb.AppendLine($"SpecialFolder_ExportPreferences: '{folder}' (exists: {Directory.Exists(folder)})");
            if (!ctx.Flag("WriteIni"))
                return sb.Append("WriteIni not given: nothing written.").ToString();

            var write = Shim<Func<string, string>>(WriteIniKey);
            var read = Shim<Func<string, string>>(ReadIniKey);
            if (write == null || read == null)
                throw new UserException("The shim's data manager is not available (is the deployed shim of this sample loaded?).");

            var target = ctx.Get("Folder", folder);
            if (!Directory.Exists(target))
                throw new UserException($"Folder not found: '{target}'. Give an existing folder: Folder=<folder>.");

            // The data manager's own logic (GeneralDataManager in the shim), called directly: AD calls it only from its
            // Preferences export/import UI. It writes the value the page has cached (see ShowPreference).
            var file = write(target);
            sb.AppendLine($"Wrote {file}");
            sb.Append($"Read back Temperature: '{read(file) ?? "(missing)"}'");
            return sb.ToString();
        }

        private static T Shim<T>(string key) where T : class => AppDomain.CurrentDomain.GetData(key) as T;
    }
}
