using System;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.SupplierPreference
{
    /// <summary>
    /// Reads and changes the supplier import parameter preferences in two ways: directly in the options store of the
    /// IntegratedLibrary server (DXP.IOptionsReader / DXP.IOptionsWriter) and through SCH.ISupplierManager.
    /// </summary>
    internal static class SupplierPreferenceCommands
    {
        // Server name, section and key layout: the SDK docs do not describe them.
        // Each import parameter is stored as a pair of indexed keys: ParameterName<i> and Visible<i>, i = 0, 1, ...
        private const string ServerName = "IntegratedLibrary";
        private const string Section = "Import Parameters";
        private const string NameKey = "ParameterName";
        private const string VisibleKey = "Visible";

        // Guard against a store that never yields an empty ParameterName.
        private const int MaxEntries = 1000;

        /// <summary>
        /// SupplierPreference "Name=&lt;parameter&gt;" (default Category): toggles the Visible flag of the import
        /// parameter, or appends it with Visible = true when it is not in the list, then flushes the options.
        /// </summary>
        [HotCommand("SupplierPreference", "Toggle the Visible flag of a supplier import parameter")]
        public static string SupplierPreference(CommandContext ctx)
        {
            string name = (ctx.Get("Name", "Category") ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new UserException("Empty Name. Pass the import parameter as a parameter: Name=<parameter>.");

            IOptionsManager options = OptionsManager();
            IOptionsReader reader = Reader(options);
            IOptionsWriter writer = options.GetOptionsWriter(ServerName)
                ?? throw new UserException($"GetOptionsWriter('{ServerName}') returned null.");

            int index = Find(reader, name, out bool found);
            var report = new StringBuilder();
            string visibleKey = VisibleKey + index;

            if (found)
            {
                // A missing Visible key is read as false; the report says whether it existed.
                bool keyExists = reader.ValueExists(Section, visibleKey);
                bool before = reader.ReadBoolean(Section, visibleKey, false);
                writer.WriteBoolean(Section, visibleKey, !before);
                report.AppendLine($"'{name}' found at index {index}: {visibleKey} was {before}" +
                                  (keyExists ? "" : " (key absent, read as false)") + $", written {!before}.");
            }
            else
            {
                // Not in the list: append it at the first free index, visible.
                writer.WriteString(Section, NameKey + index, name);
                writer.WriteBoolean(Section, visibleKey, true);
                report.AppendLine($"'{name}' not found: added as {NameKey}{index} with {visibleKey} = True.");
            }

            // Read back with the same reader before Flush.
            report.AppendLine($"Read back before Flush: {visibleKey} = {reader.ReadBoolean(Section, visibleKey, false)}");

            // Flush commits the written values (see the sample's Notes).
            writer.Flush();
            IOptionsReader fresh = Reader(options);
            report.AppendLine($"Read back after Flush (new reader): {NameKey}{index} = '{fresh.ReadString(Section, NameKey + index, "")}', " +
                              $"{visibleKey} = {fresh.ReadBoolean(Section, visibleKey, false)}");

            // The second API path: the supplier manager's view of the same preference.
            ISupplierManager manager = SupplierManager();
            ISupplierImportParameterPref pref = manager.FindImportParameterPreferences(name);
            report.Append(pref == null
                ? $"ISupplierManager.FindImportParameterPreferences('{name}'): null"
                : $"ISupplierManager.FindImportParameterPreferences('{name}'): Visible = {pref.Get_Visible()}");

            return report.ToString();
        }

        /// <summary>
        /// ListSupplierImportParameters ["Raw=true"]: the import parameters as stored in the options and as reported
        /// by the supplier manager. Raw=true also prints the whole section as returned by ReadSection.
        /// </summary>
        [HotCommand("ListSupplierImportParameters", "List the supplier import parameter preferences")]
        public static string ListSupplierImportParameters(CommandContext ctx)
        {
            IOptionsReader reader = Reader(OptionsManager());
            var report = new StringBuilder();

            report.AppendLine($"Options '{ServerName}' / '{Section}' (section exists: {reader.SectionExists(Section)}):");
            int stored = 0;
            for (; stored < MaxEntries; stored++)
            {
                string paramName = reader.ReadString(Section, NameKey + stored, "");
                if (string.IsNullOrEmpty(paramName))
                    break;
                string visible = reader.ValueExists(Section, VisibleKey + stored)
                    ? reader.ReadBoolean(Section, VisibleKey + stored, false).ToString()
                    : "(no key)";
                report.AppendLine($"  [{stored}] {paramName}: Visible = {visible}");
            }
            report.AppendLine($"  {stored} entr{(stored == 1 ? "y" : "ies")}");

            if (ctx.Flag("Raw"))
            {
                // ReadSection's return format is undocumented; it is printed as returned.
                report.AppendLine("ReadSection:");
                report.AppendLine(reader.ReadSection(Section) ?? "(null)");
            }

            ISupplierManager manager = SupplierManager();
            int count = manager.GetImportParameterPreferencesCount();
            report.AppendLine($"ISupplierManager import parameter preferences: {count}");
            // The index base of GetImportParameterPreferences is undocumented; 0 .. Count - 1 is assumed and a null
            // entry is reported, so a wrong base shows up in the output.
            for (int i = 0; i < count; i++)
            {
                // GetImportParameterPreferences is an extension method the SDK marks [Obsolete] (CS0612) without a
                // message or a replacement. It is the only typed way to reach a preference by index, so the warning
                // is suppressed for this call only.
#pragma warning disable CS0612
                ISupplierImportParameterPref pref = manager.GetImportParameterPreferences(i);
#pragma warning restore CS0612
                report.AppendLine(pref == null
                    ? $"  [{i}] (null)"
                    : $"  [{i}] {pref.Get_ParameterName()}: ImportName = '{pref.Get_ImportName()}', Visible = {pref.Get_Visible()}, " +
                      $"Exclude = {pref.Get_Exclude()}, Suffix = {pref.Get_Suffix()}");
            }

            return report.ToString().TrimEnd();
        }

        /// <summary>Index of the named entry (found = true), or the first free index (found = false).</summary>
        private static int Find(IOptionsReader reader, string name, out bool found)
        {
            for (int i = 0; i < MaxEntries; i++)
            {
                string paramName = reader.ReadString(Section, NameKey + i, "");
                if (string.IsNullOrEmpty(paramName))
                {
                    found = false;
                    return i;
                }
                // Exact, case-sensitive match.
                if (paramName == name)
                {
                    found = true;
                    return i;
                }
            }
            throw new UserException($"More than {MaxEntries} entries in '{Section}'; giving up.");
        }

        private static IOptionsManager OptionsManager() =>
            DXP.GlobalVars.Client.GetOptionsManager()
            ?? throw new UserException("The options manager is not available (GetOptionsManager returned null).");

        private static IOptionsReader Reader(IOptionsManager options) =>
            options.GetOptionsReader(ServerName, "")
            ?? throw new UserException($"GetOptionsReader('{ServerName}') returned null.");

        private static ISupplierManager SupplierManager() =>
            EDP.Utils.GetSupplierManager()
            ?? throw new UserException("The supplier manager is not available (EDP.Utils.GetSupplierManager returned null).");
    }
}
