using System;
using System.Collections.Generic;
using System.IO;
using DXP;
using EDP;

namespace AdSamples.ProjectParameterRefresh
{
    /// <summary>
    /// Changes the value of a project parameter through the design model (EDP.IProject / EDP.IParameter) and asks
    /// the Projects panel to show the change with IProject.DM_RefreshInWorkspaceForm.
    /// </summary>
    internal static class ProjectParameterRefreshCommands
    {
        // The value a menu run writes when no Value is given.
        private const string DefaultValue = "KKKKEEE";

        /// <summary>
        /// SetProjectParameter "Name=&lt;name&gt;|Index=&lt;n&gt;|Value=&lt;text&gt;": sets the parameter given by Name
        /// (or by 0-based Index when no Name is given; default 0) of the focused project, then refreshes the project
        /// in the Projects panel.
        /// </summary>
        [HotCommand("SetProjectParameter", "Set a project parameter and refresh the Projects panel")]
        public static string SetProjectParameter(CommandContext ctx)
        {
            string value = ctx.Get("Value", DefaultValue);
            IProject project = FocusedProject();

            int count = project.DM_ParameterCount();
            if (count == 0)
                throw new UserException($"{ProjectName(project)} has no project parameters. Add one in Project Options › Parameters first.");

            IParameter parameter = FindParameter(project, count, ctx.Get("Name"), ctx.Get("Index", "0"), out int index);
            string name = parameter.DM_Name();
            string previous = parameter.DM_Value();

            parameter.DM_SetValue(value);
            // The SDK lesson: after changing design-model data, ask the Projects panel to redraw
            // the project. Whether the panel would update without this call is not documented.
            project.DM_RefreshInWorkspaceForm();

            // Read the value back through a fresh lookup, so the result shows what the project now holds.
            string now = project.DM_Parameters(index).DM_Value();
            return $"{ProjectName(project)}: parameter #{index} '{name}' changed from '{previous}' to '{now}'; " +
                   "Projects panel refreshed. The change is in memory: save the project to keep it.";
        }

        /// <summary>ListProjectParameters: one "#i Name = Value" line per parameter of the focused project.</summary>
        [HotCommand("ListProjectParameters", "List the parameters of the focused project")]
        public static string ListProjectParameters(CommandContext ctx)
        {
            IProject project = FocusedProject();
            int count = project.DM_ParameterCount();
            var lines = new List<string> { $"{ProjectName(project)}: {count} parameter(s)" };
            for (int i = 0; i < count; i++)
            {
                IParameter parameter = project.DM_Parameters(i);
                lines.Add($"  #{i} {parameter.DM_Name()} = {parameter.DM_Value()}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// The parameter named <paramref name="name"/> (case-insensitive), or the one at <paramref name="indexText"/>
        /// when no name is given. The index is checked against the parameter count.
        /// </summary>
        private static IParameter FindParameter(IProject project, int count, string name, string indexText, out int index)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < count; i++)
                {
                    IParameter candidate = project.DM_Parameters(i);
                    if (string.Equals(candidate.DM_Name(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        return candidate;
                    }
                }
                throw new UserException($"{ProjectName(project)} has no parameter named '{name}'. Run ListProjectParameters to see them.");
            }

            if (!int.TryParse(indexText, out index) || index < 0 || index >= count)
                throw new UserException($"Index '{indexText}' is out of range: {ProjectName(project)} has {count} parameter(s) (0 to {count - 1}).");
            return project.DM_Parameters(index)
                ?? throw new UserException($"Parameter #{index} of {ProjectName(project)} is not available.");
        }

        /// <summary>
        /// The focused project, queried again on every command. A missing workspace or project is reported.
        /// </summary>
        private static IProject FocusedProject()
        {
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and select it in the Projects panel.");
        }

        private static string ProjectName(IProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
