using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DXP; // OpenDocument / ShowDocument / GetDXPWorkspace are typed extension methods of DXP.IClient
using EDP; // DM_FocusedProject, DM_LogicalDocuments(i), Outputer(i) are typed extension methods in EDP

namespace AdSamples.OutJobGenerateOutputers
{
    /// <summary>
    /// Generates the output of each outputer of the focused project's Output Jobs by calling
    /// IOutputer.DM_Generate_OutputFiles on it directly, outputer by outputer (IWSM_OutputJobDocument.OutputerCount /
    /// Outputer(i)). Compare the OutputJobGenerate sample, which runs whole output media through WorkspaceManager
    /// processes instead.
    /// </summary>
    internal static class OutJobGenerateOutputersCommands
    {
        // IDocument.DM_DocumentKind of an Output Job, and the kind passed to IClient.OpenDocument.
        private const string OutJobKind = "OUTPUTJOB";

        // Upper bound for the folder comparison, so a huge project folder cannot stall the command.
        private const int MaxSnapshotFiles = 20000;

        /// <summary>
        /// GenerateOutputers "Job=&lt;OutJob name&gt;|Generator=&lt;names&gt;|Show=false|DryRun=false".
        /// </summary>
        [HotCommand("GenerateOutputers", "Generate the files of every outputer in the project's Output Jobs")]
        public static string GenerateOutputers(CommandContext ctx)
        {
            // Job: part of the OutJob file name (case-insensitive). Empty = every OutJob.
            string jobFilter = ctx.Get("Job", "").Trim();
            // Generator: ';'-separated generator names or outputer descriptions ('|' separates the parameters).
            // Empty = every outputer. Lets a run pick cheap outputers and skip print outputers.
            var generators = new HashSet<string>(
                ctx.Get("Generator", "").Split(';').Select(g => g.Trim()).Where(g => g.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            // Showing each OutJob is opt-in, to avoid moving the user's focus.
            bool show = ctx.Flag("Show");
            // DryRun lists the outputers and generates nothing.
            bool dryRun = ctx.Flag("DryRun");

            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");
            var workspace = client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("Focus a project that contains an Output Job.");

            var sb = new StringBuilder();
            string projectFile = project.DM_ProjectFileName();
            sb.AppendLine($"Project: {projectFile}{(dryRun ? " (dry run, nothing generated)" : "")}");

            // Compile only when the project says it needs it. Compiling can take a while.
            if (project.DM_NeedsCompile())
            {
                ctx.Log("Compiling the project...");
                bool compiled = project.DM_Compile();
                sb.AppendLine($"Compiled: {compiled}");
            }
            else
            {
                sb.AppendLine("Compile not needed.");
            }

            List<IDocument> outJobs = OutJobsOf(project, jobFilter);

            // DM_Generate_OutputFiles writes where the outputer is configured to write, which this command does not
            // know. The project folder is compared before and after to show what appeared there.
            string projectDir = ProjectFolder(project);
            Dictionary<string, DateTime> before = dryRun || projectDir == null ? null : Snapshot(projectDir);

            int generated = 0, failed = 0, skipped = 0, listed = 0;
            foreach (IDocument doc in outJobs)
            {
                string path = doc.DM_FullPath();
                bool wasOpen = client.IsDocumentOpen(path);
                IServerDocument serverDoc = client.OpenDocument(OutJobKind, path);
                if (serverDoc == null)
                {
                    sb.AppendLine($"{doc.DM_FileName()}: could not be opened, skipped.");
                    continue;
                }
                if (show)
                    client.ShowDocument(serverDoc);

                // The SDK interfaces are IDispatch interfaces, so this cast succeeds.
                var outJob = (IWSM_OutputJobDocument)serverDoc;
                int count = outJob.GetState_OutputerCount();
                sb.AppendLine($"{doc.DM_FileName()} ({(wasOpen ? "was open" : "opened by this command")}): {count} outputer(s)");

                // The SDK lesson: the index order of GetState_Outputer(i) is not stable.
                // Generating an outputer reorders the collection, so a loop that calls GetState_Outputer(j) inside
                // the generating loop can hit the same outputer twice and never reach another one (for example
                // 'NC Drill Files' generated at index 12 and again at index 15). Take the references in
                // one pass first, then work on that snapshot.
                var outputers = new List<IOutputer>();
                for (int j = 0; j < count; j++)
                    outputers.Add(outJob.GetState_Outputer(j));

                for (int j = 0; j < outputers.Count; j++)
                {
                    IOutputer outputer = outputers[j];
                    if (outputer == null)
                    {
                        sb.AppendLine($"  [{j}] null");
                        continue;
                    }
                    string generator = outputer.DM_GeneratorName();
                    string description = outputer.DM_GetDescription();
                    string label = $"[{j}] '{description}' generator '{generator}'";

                    if (generators.Count > 0 && !generators.Contains(generator) && !generators.Contains(description))
                    {
                        sb.AppendLine($"  {label}: skipped");
                        skipped++;
                        continue;
                    }
                    if (dryRun)
                    {
                        sb.AppendLine($"  {label}, document '{outputer.DM_GetDocumentPath()}'");
                        listed++;
                        continue;
                    }

                    // The SDK lesson: the .NET signature takes the file list by ref and returns a success flag.
                    // What the string contains
                    // (separator, absolute or relative names) is not documented, so it is reported verbatim.
                    ctx.Log($"Generating {label}...");
                    string files = "";
                    try
                    {
                        bool ok = outputer.DM_Generate_OutputFiles(ref files);
                        sb.AppendLine($"  {label}: {(ok ? "generated" : "returned false")}, files '{files}'");
                        if (ok) generated++; else failed++;
                    }
                    catch (COMException ex)
                    {
                        // One failing outputer does not stop the others.
                        sb.AppendLine($"  {label}: COMException 0x{ex.HResult:X8} {ex.Message}");
                        failed++;
                    }
                }
            }

            if (dryRun)
            {
                sb.Append($"Outputers listed: {listed}, skipped: {skipped}.");
                return sb.ToString();
            }

            sb.AppendLine($"Outputers generated: {generated}, failed: {failed}, skipped: {skipped}.");
            if (before != null)
            {
                List<string> written = Snapshot(projectDir)
                    .Where(kv => !before.TryGetValue(kv.Key, out DateTime t) || t != kv.Value)
                    .Select(kv => kv.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                sb.AppendLine($"Files written or changed under the project folder {projectDir}: {written.Count}");
                foreach (string f in written.Take(50))
                    sb.AppendLine("  " + Path.GetRelativePath(projectDir, f));
                if (written.Count > 50)
                    sb.AppendLine($"  ... {written.Count - 50} more");
            }
            if (generated + failed == 0)
                sb.Append(generators.Count > 0
                    ? $"No outputer matched Generator={string.Join(";", generators)}. DryRun=true lists the names."
                    : "No outputer found.");
            return sb.ToString().TrimEnd();
        }

        private static List<IDocument> OutJobsOf(IProject project, string jobFilter)
        {
            var result = new List<IDocument>();
            int count = project.DM_LogicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDocument doc = project.DM_LogicalDocuments(i);
                if (doc == null || !string.Equals(doc.DM_DocumentKind(), OutJobKind, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (jobFilter.Length > 0 && doc.DM_FileName().IndexOf(jobFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                result.Add(doc);
            }
            if (result.Count == 0)
                throw new UserException(jobFilter.Length > 0
                    ? $"The focused project {project.DM_ProjectFileName()} has no Output Job whose name contains '{jobFilter}'."
                    : "Focus a project that contains an Output Job.");
            return result;
        }

        private static string ProjectFolder(IProject project)
        {
            // DM_ProjectFullPath is the .PrjPcb file; its folder holds the project (and usually its Project Outputs).
            string full = project.DM_ProjectFullPath();
            string dir = string.IsNullOrEmpty(full) ? null : Path.GetDirectoryName(full);
            return dir != null && Directory.Exists(dir) ? dir : null;
        }

        private static Dictionary<string, DateTime> Snapshot(string dir) =>
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .Take(MaxSnapshotFiles)
                     .ToDictionary(f => f, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
    }
}
