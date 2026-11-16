using System;
using System.Collections.Generic;
using System.IO;
using DXP;

namespace AdSamples.OptionsStorage
{
    /// <summary>
    /// Reads and writes custom ("external") parameters in the options storage of the focused project:
    /// a section/key/value store reached through DM_OptionsStorage().
    /// </summary>
    internal static class OptionsStorageCommands
    {
        // GetExternalParameterValue returns the default argument when it finds no value. Passing a marker no user
        // would store lets the read-back tell "no value" from a real one. Whether a key stored with an empty
        // value also returns the default is not documented.
        private const string NoValue = "<no value>";

        /// <summary>
        /// AddUserParameter "Section=MySection|Key=UserName|Value=Allen[|Overwrite=true]": adds the value when the key
        /// has none, then reports the stored value.
        /// </summary>
        [HotCommand("AddUserParameter", "Add a user parameter to the focused project's options")]
        public static string AddUserParameter(CommandContext ctx)
        {
            string section = ctx.Get("Section", "MySection");
            string key = ctx.Get("Key", "UserName");
            string value = ctx.Get("Value", "Allen");
            bool overwrite = ctx.Flag("Overwrite");
            RequireName(section, "Section");
            RequireName(key, "Key");

            IDXPProject project = FocusedProject();
            IOptionsStorage storage = Storage(project);

            string action;
            string current = storage.GetExternalParameterValue(section, key, string.Empty);
            if (string.IsNullOrEmpty(current))
            {
                // An empty result means "not set yet", so the value is added. A missing key with an empty default
                // comes back as null, not "": test both.
                storage.AddExternalParameter(section, key, value);
                action = "added";
            }
            else if (overwrite && current != value)
            {
                // Overwrite=true replaces an existing value.
                storage.ModifyExternalParameter(section, key, value);
                action = $"modified (was '{current}')";
            }
            else
            {
                action = "kept (already set" + (overwrite ? " to the same value)" : "; pass Overwrite=true to replace it)");
            }

            string stored = storage.GetExternalParameterValue(section, key, "Not Set");
            return $"{ProjectName(project)}: [{section}] {key} {action}; stored value = '{stored}'.";
        }

        /// <summary>
        /// ReadUserParameter "Section=MySection|Key=UserName": reports the value, whether the section exists, the
        /// section text and the storage file name, without changing anything.
        /// </summary>
        [HotCommand("ReadUserParameter", "Read a user parameter from the focused project's options")]
        public static string ReadUserParameter(CommandContext ctx)
        {
            string section = ctx.Get("Section", "MySection");
            string key = ctx.Get("Key", "UserName");
            RequireName(section, "Section");
            RequireName(key, "Key");

            IDXPProject project = FocusedProject();
            IOptionsStorage storage = Storage(project);

            string value = storage.GetExternalParameterValue(section, key, NoValue);
            var lines = new List<string>
            {
                $"{ProjectName(project)}: [{section}] {key} = " + (value == NoValue ? "(no value)" : $"'{value}'"),
                $"  SectionExists('{section}') = {storage.SectionExists(section)}",
                $"  GetFileName() = '{storage.GetFileName()}'",
                $"  GetSectionText('{section}'):",
            };
            string text = storage.GetSectionText(section) ?? string.Empty;
            foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                lines.Add("    " + line);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>DeleteUserParameter "Section=MySection|Key=UserName": removes the key, then reads it back.</summary>
        [HotCommand("DeleteUserParameter", "Delete a user parameter from the focused project's options")]
        public static string DeleteUserParameter(CommandContext ctx)
        {
            // Undoes AddUserParameter.
            string section = ctx.Get("Section", "MySection");
            string key = ctx.Get("Key", "UserName");
            RequireName(section, "Section");
            RequireName(key, "Key");

            IDXPProject project = FocusedProject();
            IOptionsStorage storage = Storage(project);

            string before = storage.GetExternalParameterValue(section, key, NoValue);
            storage.DeleteExternalParameter(section, key);
            string after = storage.GetExternalParameterValue(section, key, NoValue);

            return $"{ProjectName(project)}: [{section}] {key} deleted; value before = {Show(before)}, after = {Show(after)}; " +
                   $"SectionExists('{section}') = {storage.SectionExists(section)}.";
        }

        /// <summary>The focused project, queried again on every command.</summary>
        private static IDXPProject FocusedProject()
        {
            IDXPWorkSpace workspace = DXP.GlobalVars.Client?.GetDXPWorkspace()
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and select it in the Projects panel.");
        }

        /// <summary>The project's options storage, or an error when there is none.</summary>
        private static IOptionsStorage Storage(IDXPProject project) =>
            project.DM_OptionsStorage()
            ?? throw new UserException($"{ProjectName(project)} has no options storage.");

        private static void RequireName(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new UserException($"{name} is empty. Pass it as a parameter: {name}=<text>.");
        }

        private static string Show(string value) => value == NoValue ? "(no value)" : $"'{value}'";

        private static string ProjectName(IDXPProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
