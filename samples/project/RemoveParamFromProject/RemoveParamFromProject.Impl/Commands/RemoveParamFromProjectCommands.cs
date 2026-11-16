using System;
using System.Collections.Generic;
using System.IO;
using DXP;

namespace AdSamples.RemoveParamFromProject
{
    /// <summary>
    /// Removes, lists and adds the project-level parameters of the focused project through DXP.IDXPProject.
    /// Parameters are addressed by index (DM_GetParameterName / DM_GetParameterValue / DM_RemoveParameter).
    /// </summary>
    internal static class RemoveParamFromProjectCommands
    {
        /// <summary>RemoveParamFromProject: removes every parameter of the focused project, without confirmation.</summary>
        [HotCommand("RemoveParamFromProject", "Remove all parameters from the focused project")]
        public static string RemoveParamFromProject(CommandContext ctx)
        {
            IDXPProject project = FocusedProject();
            int before = project.DM_GetParameterCount();
            if (before == 0)
                return $"{ProjectName(project)}: no parameters, nothing removed.";

            var removed = new List<string>();
            // Removing index i shifts the later parameters down one index, so
            // a loop from 0 upwards would skip every second parameter. The loop runs from the last index to 0,
            // so each removal leaves the indexes still to visit unchanged.
            for (int i = before - 1; i >= 0; i--)
            {
                removed.Add(project.DM_GetParameterName(i));
                project.DM_RemoveParameter(i);
            }
            removed.Reverse();

            int after = project.DM_GetParameterCount();
            return $"{ProjectName(project)}: removed {removed.Count} parameter(s) ({string.Join(", ", removed)}); " +
                   $"count {before} -> {after}. The project is changed in memory: save it to keep the change.";
        }

        /// <summary>ListProjectParameters: one "Name = Value" line per parameter of the focused project.</summary>
        [HotCommand("ListProjectParameters", "List the parameters of the focused project")]
        public static string ListProjectParameters(CommandContext ctx)
        {
            IDXPProject project = FocusedProject();
            int count = project.DM_GetParameterCount();
            var lines = new List<string> { $"{ProjectName(project)}: {count} parameter(s)" };
            for (int i = 0; i < count; i++)
                lines.Add($"  #{i} {project.DM_GetParameterName(i)} = {project.DM_GetParameterValue(i)}");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>AddProjectParameter "Name=&lt;name&gt;|Value=&lt;text&gt;": adds a parameter, or sets it if the name exists.</summary>
        [HotCommand("AddProjectParameter", "Add or update a parameter of the focused project")]
        public static string AddProjectParameter(CommandContext ctx)
        {
            string name = ctx.Get("Name");
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("No Name specified. Pass it as a parameter: Name=<name>|Value=<text>.");
            string value = ctx.Get("Value", string.Empty);

            IDXPProject project = FocusedProject();
            int before = project.DM_GetParameterCount();
            // DM_AddParameter with an existing name adds a second parameter of the same name, so an
            // existing name is updated with DM_SetParameter instead.
            bool exists = project.DM_GetParameterExist(name);
            if (exists)
                project.DM_SetParameter(name, value);
            else
                project.DM_AddParameter(name, value);
            int after = project.DM_GetParameterCount();

            return $"{ProjectName(project)}: {(exists ? "updated" : "added")} parameter '{name}' = '{value}'; " +
                   $"count {before} -> {after}.";
        }

        /// <summary>
        /// The focused project, queried again on every command. A missing workspace or focused project is reported
        /// as an error.
        /// </summary>
        private static IDXPProject FocusedProject()
        {
            IDXPWorkSpace workspace = DXP.GlobalVars.Client?.GetDXPWorkspace()
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and select it in the Projects panel.");
        }

        private static string ProjectName(IDXPProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
