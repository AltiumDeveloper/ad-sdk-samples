using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // GetDXPWorkspace is an extension method of DXP.IClient
using EDP;

namespace AdSamples.ProjectOdbOutput
{
    /// <summary>
    /// Generates the ODB++ files of the focused project straight from the project model:
    /// IProject.DM_Outputers("ODB") returns the configured ODB++ outputer, IOutputer.DM_Generate_OutputFilesTo
    /// writes its files to a folder. No Output Job document is opened.
    /// </summary>
    internal static class ProjectOdbOutputCommands
    {
        /// <summary>
        /// Generate "Out=&lt;folder&gt;|Outputer=&lt;type&gt;|Overrides=&lt;text&gt;|Compile=true".
        /// </summary>
        [HotCommand("Generate", "Generate the focused project's ODB++ output files")]
        public static string Generate(CommandContext ctx)
        {
            // The folder is a parameter; by default a sub-folder of the temp folder, so the generated files are
            // easy to find and don't mix with other temp files.
            string outDir = Path.GetFullPath(ctx.Get("Out", Path.Combine(Path.GetTempPath(), "ProjectOdbOutput")));
            // "ODB" is an output type. In the SpiritLevel-SL1 example project it is the output's type
            // (OutputType7=ODB), while the output's own name is "ODB++ Files".
            string outputerType = ctx.Get("Outputer", "ODB");
            // Second argument of DM_Generate_OutputFilesTo, empty by default. Its format is not documented, so it
            // is passed through unchanged.
            string overrides = ctx.Get("Overrides", "");

            // Get the workspace and project again on each run. The DM_* members used here are extension methods
            // that need `using EDP;`.
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a PCB project and focus one of its documents.");

            string projectName = Path.GetFileName(project.DM_ProjectFullPath());

            if (ctx.Flag("Compile"))
            {
                ctx.Log($"Compiling {projectName}...");
                if (!project.DM_Compile())
                    ctx.Log("DM_Compile returned false.");
            }

            // DM_Outputers looks the outputer up by output type and returns null when there is none; a
            // project without an ODB++ output is an expected failure here.
            IOutputer outputer = project.DM_Outputers(outputerType)
                ?? throw new UserException(
                    $"{projectName} has no '{outputerType}' outputer. Configure an ODB++ output for the project (for example in an Output Job) or pass Outputer=<type>.");

            string description = outputer.DM_GetDescription();
            string generator = outputer.DM_GeneratorName();
            ctx.Log($"Outputer: {description} (generator {generator})");

            Directory.CreateDirectory(outDir);
            // DM_Generate_OutputFilesTo returns only a success flag, so the folder is compared before and after
            // to report what was written. ODB++ writes a directory tree (steps, layers, ...), hence the recursive
            // snapshot.
            Dictionary<string, DateTime> before = Snapshot(outDir);

            ctx.Log($"Generating to {outDir}...");
            bool ok = outputer.DM_Generate_OutputFilesTo(outDir, overrides);

            List<string> written = Snapshot(outDir)
                .Where(f => !before.TryGetValue(f.Key, out DateTime t) || t != f.Value)
                .Select(f => f.Key.Substring(outDir.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Project: {projectName}");
            sb.AppendLine($"Outputer: '{outputerType}': description '{description}', generator '{generator}', " +
                          $"document '{outputer.DM_GetDocumentPath()}', variant '{outputer.DM_GetState_VariantName()}'");
            sb.AppendLine($"DM_Generate_OutputFilesTo returned {ok}");
            sb.AppendLine($"Output folder: {outDir}");
            sb.Append($"Files written: {written.Count}");
            // An ODB++ job has many files; list the top-level entries with a count each, not every file.
            foreach (var group in written.GroupBy(TopLevel, StringComparer.OrdinalIgnoreCase))
            {
                int n = group.Count();
                bool isFile = n == 1 && string.Equals(group.First(), group.Key, StringComparison.OrdinalIgnoreCase);
                sb.Append(Environment.NewLine).Append("  ").Append(group.Key);
                if (!isFile)
                    sb.Append($"{Path.DirectorySeparatorChar} ({n} files)");
            }
            return sb.ToString();
        }

        /// <summary>First path segment of a path relative to the output folder.</summary>
        private static string TopLevel(string relative)
        {
            int i = relative.IndexOf(Path.DirectorySeparatorChar);
            return i < 0 ? relative : relative.Substring(0, i);
        }

        /// <summary>Full path to last-write time of every file under the folder.</summary>
        private static Dictionary<string, DateTime> Snapshot(string dir) =>
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(f => f, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
    }
}
