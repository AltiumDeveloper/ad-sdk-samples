using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // OpenDocument / ShowDocument / GetCommandLauncher are typed extension methods of DXP.IClient
using EDP; // OutputMedium(i) is a typed extension property of IWSM_OutputJobDocument

namespace AdSamples.OutputJobGenerate
{
    /// <summary>
    /// Runs the media (output containers) of every Output Job (.OutJob) of the focused project by launching the
    /// built-in WorkspaceManager processes with a parameter string (RunCommand).
    /// ListOutputMedia only reads the media, so the type strings can be checked before anything is generated.
    /// </summary>
    internal static class OutputJobGenerateCommands
    {
        // IDocument.DM_DocumentKind of an Output Job, and the kind passed to IClient.OpenDocument.
        private const string OutJobKind = "OUTPUTJOB";

        // IOutputMedium.TypeString values, as returned for the SpiritLevel example's
        // '3D Prints.OutJob': 'Print', 'PDF', 'Generate Files' and 'Multimedia' (the OutJob file stores other
        // words for the same media: Printer, Publish, GeneratedFiles, Multimedia). 'Print Job' is the printer
        // medium's *name* there, not its type string; that name is accepted as an alias of 'Print'.
        private const string GenerateFilesType = "Generate Files";
        private const string PrintType = "Print";
        private const string PrintJobAlias = "Print Job";
        private const string PdfType = "PDF";

        private static bool IsPrintType(string type) =>
            string.Equals(type, PrintType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, PrintJobAlias, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// RunOutputJobs "Out=&lt;folder&gt;|Types=Generate Files|Medium=&lt;name&gt;|Route=RunCommand|EditProperties=False".
        /// </summary>
        [HotCommand("RunOutputJobs", "Run the Output Job media of the focused project")]
        public static string RunOutputJobs(CommandContext ctx)
        {
            // A sub folder of the temp folder keeps generated files together.
            string outDir = Path.GetFullPath(ctx.Get("Out", Path.Combine(Path.GetTempPath(), SampleInfo.Name)).Trim().Trim('"'));
            // Print Job sends output to a printer; Print and PDF are opt-in.
            // ';' separates the values because '|' separates the parameters.
            var types = new HashSet<string>(
                ctx.Get("Types", GenerateFilesType).Split(';').Select(t => t.Trim()).Where(t => t.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            string mediumFilter = ctx.Get("Medium", "").Trim();
            string route = ctx.Get("Route", "RunCommand").Trim();
            if (!new[] { "RunCommand", "MessageRouter", "Launcher" }.Contains(route, StringComparer.OrdinalIgnoreCase))
                throw new UserException($"Unknown Route '{route}'. Use RunCommand, MessageRouter or Launcher.");
            // "DoEditProperties = True" may open the medium's properties dialog, which blocks
            // AD on the pipe path, so it is left out unless asked for.
            bool editProperties = ctx.Flag("EditProperties");

            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");
            IProject project = FocusedProject(client);
            List<IDocument> outJobs = OutJobsOf(project);

            Directory.CreateDirectory(outDir);
            Dictionary<string, DateTime> before = Snapshot(outDir);

            var sb = new StringBuilder();
            sb.AppendLine($"Project: {project.DM_ProjectFileName()} ({outJobs.Count} Output Job(s)), Out: {outDir}, route: {route}");
            int launched = 0, skipped = 0;
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
                // Show the OutJob. The processes get no OutJob file name, so they are expected
                // to act on the shown (current) Output Job; that works, but it is not a documented rule.
                client.ShowDocument(serverDoc);
                var outJob = (IWSM_OutputJobDocument)serverDoc;
                int count = outJob.GetState_OutputMediumCount();
                sb.AppendLine($"{doc.DM_FileName()} ({(wasOpen ? "was open" : "opened by this command")}): {count} medium/media");

                for (int j = 0; j < count; j++)
                {
                    IOutputMedium medium = outJob.GetState_OutputMedium(j);
                    if (medium == null)
                        continue;
                    string name = medium.GetState_Name();
                    string type = medium.GetState_TypeString();
                    if ((mediumFilter.Length > 0 && !string.Equals(name, mediumFilter, StringComparison.OrdinalIgnoreCase))
                        || !(types.Contains(type) || (IsPrintType(type) && (types.Contains(PrintType) || types.Contains(PrintJobAlias)))))
                    {
                        sb.AppendLine($"  [{j}] '{name}' type '{type}': skipped");
                        skipped++;
                        continue;
                    }

                    string target = Path.Combine(outDir, name);
                    string process, parameters;
                    string restorePath = null;
                    if (IsPrintType(type))
                    {
                        process = "WorkspaceManager:Print";
                        parameters = "Action=PublishMultimedia|DisableDialog=True|ObjectKind=OutputBatch"
                                   + "|OutputMedium=" + name
                                   + "|PromptOverwrite=FALSE|OpenOutput=FALSE";
                        // The Print Job medium gets the folder through its OutputPath property
                        // (the bare folder with a trailing separator).
                        restorePath = medium.GetState_OutputPath();
                        medium.SetState_OutputPath(outDir.TrimEnd('\\') + "\\");
                    }
                    else if (string.Equals(type, PdfType, StringComparison.OrdinalIgnoreCase))
                    {
                        // The PDF medium.
                        process = "WorkspaceManager:Print";
                        parameters = "Action=PublishToPDF|DisableDialog=True|ObjectKind=OutputBatch"
                                   + "|OutputMedium=" + name
                                   + "|OutputFilePath=" + target
                                   + "|PromptOverwrite=FALSE|OpenOutput=FALSE";
                    }
                    else
                    {
                        // The 'Generate Files' medium; any other type named in Types takes it too.
                        // OutputFilePath = <Out>\<medium name>.
                        Directory.CreateDirectory(target);
                        process = "WorkspaceManager:GenerateReport";
                        parameters = "ObjectKind=OutputBatch"
                                   + "|OutputMedium=" + name
                                   + "|OutputFilePath=" + target
                                   + "|OutputPathMedia=[Media Name]"
                                   + "|ReleaseManaged=false"
                                   + (editProperties ? "|DoEditProperties=True" : "")
                                   + "|DoGenerate=True"
                                   + "|PromptOverwrite=FALSE|OpenOutput=FALSE";
                    }

                    ctx.Log($"[{j}] {process} {parameters}");
                    try
                    {
                        Launch(client, route, process, parameters);
                    }
                    finally
                    {
                        // Put the medium's OutputPath back so the OutJob keeps its setting.
                        // Assumes the process has finished when the call returns.
                        if (restorePath != null)
                            medium.SetState_OutputPath(restorePath);
                    }
                    sb.AppendLine($"  [{j}] '{name}' type '{type}': {process} {parameters}");
                    launched++;
                }
            }

            // The processes return nothing, so the folder is compared before and after to show what was written.
            List<string> written = Snapshot(outDir)
                .Where(kv => !before.TryGetValue(kv.Key, out DateTime t) || t != kv.Value)
                .Select(kv => kv.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            sb.AppendLine($"Media launched: {launched}, skipped: {skipped}. Files written or changed under Out: {written.Count}");
            foreach (string f in written.Take(50))
                sb.AppendLine("  " + Path.GetRelativePath(outDir, f));
            if (written.Count > 50)
                sb.AppendLine($"  ... {written.Count - 50} more");
            if (launched == 0)
                sb.Append($"No medium matched (Types={string.Join(";", types)}{(mediumFilter.Length > 0 ? ", Medium=" + mediumFilter : "")}). ListOutputMedia shows the names and type strings.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>ListOutputMedia: each Output Job of the focused project with its media (name, type string, output path).</summary>
        [HotCommand("ListOutputMedia", "List the Output Job media of the focused project")]
        public static string ListOutputMedia(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");
            IProject project = FocusedProject(client);
            List<IDocument> outJobs = OutJobsOf(project);

            var sb = new StringBuilder();
            sb.Append($"Project: {project.DM_ProjectFileName()} ({outJobs.Count} Output Job(s))");
            foreach (IDocument doc in outJobs)
            {
                string path = doc.DM_FullPath();
                bool wasOpen = client.IsDocumentOpen(path);
                IServerDocument serverDoc = client.OpenDocument(OutJobKind, path);
                sb.Append(Environment.NewLine).Append($"{doc.DM_FileName()} ({(wasOpen ? "was open" : "opened by this command")})");
                if (serverDoc == null)
                {
                    sb.Append(": could not be opened");
                    continue;
                }
                var outJob = (IWSM_OutputJobDocument)serverDoc;
                int count = outJob.GetState_OutputMediumCount();
                sb.Append($": {count} medium/media");
                for (int j = 0; j < count; j++)
                {
                    IOutputMedium m = outJob.GetState_OutputMedium(j);
                    sb.Append(Environment.NewLine).Append(m == null
                        ? $"  [{j}] null"
                        : $"  [{j}] '{m.GetState_Name()}' type '{m.GetState_TypeString()}' output path '{m.GetState_OutputPath()}'");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Three ways to launch a built-in process from .NET. Route=RunCommand is the direct one; the other two
        /// are kept for comparison. RunCommand and MessageRouterSendCommandToModule both generate and
        /// return after the files exist; ICommandLauncher.LaunchCommand returns at once and generates nothing.
        /// </summary>
        private static void Launch(IClient client, string route, string process, string parameters)
        {
            if (string.Equals(route, "MessageRouter", StringComparison.OrdinalIgnoreCase))
            {
                DXP.Utils.MessageRouterSendCommandToModule(process, parameters, client.GetCurrentView());
            }
            else if (string.Equals(route, "Launcher", StringComparison.OrdinalIgnoreCase))
            {
                ICommandLauncher launcher = client.GetCommandLauncher()
                    ?? throw new UserException("IClient.GetCommandLauncher returned nothing.");
                string p = parameters;
                launcher.LaunchCommand(process, ref p, client.GetCurrentView());
            }
            else
            {
                DXP.Utils.RunCommand(process, parameters);
            }
        }

        private static IProject FocusedProject(IClient client)
        {
            var workspace = client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("Focus a project that contains an Output Job.");
        }

        private static List<IDocument> OutJobsOf(IProject project)
        {
            var result = new List<IDocument>();
            int count = project.DM_LogicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDocument doc = project.DM_LogicalDocuments(i);
                if (doc != null && string.Equals(doc.DM_DocumentKind(), OutJobKind, StringComparison.OrdinalIgnoreCase))
                    result.Add(doc);
            }
            if (result.Count == 0)
                throw new UserException($"The focused project {project.DM_ProjectFileName()} contains no Output Job ({count} logical documents).");
            return result;
        }

        private static Dictionary<string, DateTime> Snapshot(string dir) =>
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .ToDictionary(f => f, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
    }
}
