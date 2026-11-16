using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // GetDXPWorkspace is an extension method of DXP.IClient
using EDP;

namespace AdSamples.ProjectPickPlaceOutput
{
    /// <summary>
    /// Generates the output files of one of the focused project's outputers (by default "Pick Place") straight from
    /// the project model: IProject.DM_Outputers returns the outputer, IOutputer.DM_Generate_OutputFilesTo writes its
    /// files to a folder. No Output Job document is opened.
    /// </summary>
    internal static class ProjectPickPlaceOutputCommands
    {
        /// <summary>
        /// Generate "Out=&lt;folder&gt;|Outputer=&lt;name&gt;|Overrides=&lt;text&gt;|Compile=true".
        /// </summary>
        [HotCommand("Generate", "Generate the focused project's Pick Place output files")]
        public static string Generate(CommandContext ctx)
        {
            // The folder is a parameter; by default a sub-folder of the temp folder, so the generated files are
            // easy to find and don't mix with other temp files.
            string outDir = Path.GetFullPath(ctx.Get("Out", Path.Combine(Path.GetTempPath(), "ProjectPickPlaceOutput")));
            string outputerName = ctx.Get("Outputer", "Pick Place");
            // Second argument of DM_Generate_OutputFilesTo, empty by default. Its format is not documented, so it
            // is passed through unchanged.
            string overrides = ctx.Get("Overrides", "");

            // Get the workspace and project again on each run (docs: interfaces at run time). The DM_* members
            // used here are extension methods that need `using EDP;`.
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and focus one of its documents.");

            string projectName = Path.GetFileName(project.DM_ProjectFullPath());

            if (ctx.Flag("Compile"))
            {
                ctx.Log($"Compiling {projectName}...");
                if (!project.DM_Compile())
                    ctx.Log("DM_Compile returned false.");
            }

            // DM_Outputers takes the output type (for example "Pick Place"), not an index and not the output's own
            // name; the match is not case sensitive and an unknown type returns null. A project
            // without such an outputer is an expected failure here.
            IOutputer outputer = project.DM_Outputers(outputerName)
                ?? throw new UserException(
                    $"{projectName} has no '{outputerName}' outputer. Configure one (for example in an Output Job of the project) or pass Outputer=<name>.");

            ctx.Log($"Outputer: {outputer.DM_GetDescription()} (generator {outputer.DM_GeneratorName()})");

            Directory.CreateDirectory(outDir);
            // DM_Generate_OutputFilesTo returns only a success flag, so the folder is compared before and after
            // to report what was written.
            Dictionary<string, DateTime> before = Snapshot(outDir);

            ctx.Log($"Generating to {outDir}...");
            bool ok = outputer.DM_Generate_OutputFilesTo(outDir, overrides);

            List<string> written = Snapshot(outDir)
                .Where(f => !before.TryGetValue(f.Key, out DateTime t) || t != f.Value)
                .Select(f => f.Key)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Project: {projectName}");
            sb.AppendLine($"Outputer: '{outputerName}': description '{outputer.DM_GetDescription()}', generator '{outputer.DM_GeneratorName()}', " +
                          $"document '{outputer.DM_GetDocumentPath()}', variant '{outputer.DM_GetState_VariantName()}'");
            sb.AppendLine($"DM_Generate_OutputFilesTo returned {ok}");
            sb.AppendLine($"Output folder: {outDir}");
            sb.Append($"Files written: {written.Count}");
            foreach (string file in written)
                sb.Append(Environment.NewLine).Append("  ").Append(file.Substring(outDir.Length).TrimStart(Path.DirectorySeparatorChar));
            return sb.ToString();
        }

        /// <summary>Full path to last-write time of every file under the folder.</summary>
        private static Dictionary<string, DateTime> Snapshot(string dir) =>
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
    }
}
