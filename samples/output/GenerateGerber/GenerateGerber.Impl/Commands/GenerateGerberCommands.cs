using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DXP; // OpenDocument / CloseDocument / GetCurrentView / GetOwnerDocument are typed extension methods
using EDP; // GetState_Outputer and DM_CurrentProjectVariant are typed extension methods

namespace AdSamples.GenerateGerber
{
    /// <summary>
    /// Generates Gerber files for the active PcbDoc without any user interface: a template Output Job is copied to a
    /// temp file, attached to the focused project, opened as an EDP.IWSM_OutputJobDocument, pointed at the PcbDoc,
    /// and only its "Gerber Files" outputer is run into a chosen folder. Afterwards the job is closed, detached and
    /// deleted, so the project is left as it was.
    /// </summary>
    internal static class GenerateGerberCommands
    {
        // IServerDocument.GetKind of a PCB document, and the kind passed to
        // IClient.OpenDocument for an Output Job.
        private const string PcbKind = "PCB";
        private const string OutJobKind = "OUTPUTJOB";

        // IOutputer.DM_ViewName of the outputer to run: the OutputName1 entry of the bundled Gerber.OutJob.
        private const string GerberOutputerName = "Gerber Files";

        // Manifest name of the embedded template (LogicalName in GenerateGerber.Impl.csproj).
        private const string TemplateResource = "GenerateGerber.Gerber.OutJob";

        /// <summary>GenerateGerber "Out=&lt;folder&gt;|OutJob=&lt;.OutJob template&gt;".</summary>
        [HotCommand("GenerateGerber", "Generate Gerber files for the active PcbDoc through an Output Job")]
        public static string GenerateGerber(CommandContext ctx)
        {
            string outDir = Path.GetFullPath(ctx.Get("Out", Path.Combine(Path.GetTempPath(), SampleInfo.Name)).Trim().Trim('"'));
            string templatePath = ctx.Get("OutJob", "").Trim().Trim('"');

            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");
            var workspace = client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("Focus the project of the PcbDoc first.");

            // A command run through the pipe has no enabled state, so check here that the active document is a PCB.
            IServerDocument pcbDoc = client.GetCurrentView()?.GetOwnerDocument();
            if (pcbDoc == null || !string.Equals(pcbDoc.GetKind(), PcbKind, StringComparison.OrdinalIgnoreCase))
                throw new UserException("Make a .PcbDoc the active document first.");
            string pcbFileName = pcbDoc.GetFileName();

            // A temp copy keeps the template unchanged; the job is only a vehicle for the outputer settings.
            string tempOutJob = Path.Combine(Path.GetTempPath(), $"{SampleInfo.Name}-{Guid.NewGuid():N}.OutJob");
            string source = WriteTemplate(templatePath, tempOutJob);

            Directory.CreateDirectory(outDir);
            Dictionary<string, DateTime> before = Snapshot(outDir);

            var sb = new StringBuilder();
            sb.AppendLine($"Project: {project.DM_ProjectFileName()}, PcbDoc: {Path.GetFileName(pcbFileName)}");
            sb.AppendLine($"OutJob template: {source}");

            IServerDocument jobDoc = null;
            bool gerberFound = false, generated = false;
            // The open is inside the try block, so a failed open still removes the job from the project.
            project.DM_AddSourceDocument(tempOutJob);
            try
            {
                jobDoc = client.OpenDocument(OutJobKind, tempOutJob)
                    ?? throw new UserException($"Could not open the Output Job {tempOutJob}.");
                var outJob = jobDoc as IWSM_OutputJobDocument
                    ?? throw new UserException($"The opened document (kind '{jobDoc.GetKind()}') is not an Output Job document.");

                // Generate for the project's current variant, if it has one.
                IProjectVariant variant = project.DM_CurrentProjectVariant();
                if (variant != null)
                {
                    outJob.SetState_VariantName(variant.DM_Description());
                    sb.AppendLine($"Variant: {variant.DM_Description()}");
                }
                else
                {
                    sb.AppendLine("Variant: none (the project has no current variant)");
                }

                int count = outJob.GetState_OutputerCount();
                sb.AppendLine($"Outputers: {count}");
                for (int i = 0; i < count; i++)
                {
                    IOutputer outputer = outJob.GetState_Outputer(i);
                    if (outputer == null)
                        continue;
                    // Every outputer is pointed at the PcbDoc, but only the Gerber one is run.
                    outputer.DM_SetDocumentPath(pcbFileName);
                    string name = outputer.DM_ViewName();
                    if (!string.Equals(name, GerberOutputerName, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"  [{i}] '{name}': not run");
                        continue;
                    }
                    gerberFound = true;
                    ctx.Log($"Generating '{name}' into {outDir}");
                    // The second argument (parameter overrides) is empty; its format is undocumented.
                    generated = outputer.DM_Generate_OutputFilesTo(outDir, "");
                    sb.AppendLine($"  [{i}] '{name}': DM_Generate_OutputFilesTo returned {generated}");
                }
            }
            finally
            {
                if (jobDoc != null)
                    client.CloseDocument(jobDoc);
                project.DM_RemoveSourceDocument(tempOutJob);
                try { File.Delete(tempOutJob); }
                catch (IOException ex) { sb.AppendLine($"Temp OutJob not deleted: {ex.Message}"); }
                // A read-back so the result shows the project no longer lists the temp job.
                sb.AppendLine($"Temp OutJob detached: {project.DM_IndexOfSourceDocument(tempOutJob) < 0}");
            }

            if (!gerberFound)
                sb.AppendLine($"No outputer named '{GerberOutputerName}' in the Output Job.");

            // DM_Generate_OutputFilesTo returns only a bool: compare the folder to show what was written.
            List<string> written = Snapshot(outDir)
                .Where(kv => !before.TryGetValue(kv.Key, out DateTime t) || t != kv.Value)
                .Select(kv => kv.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            sb.AppendLine($"Out: {outDir}. Files written or changed: {written.Count}");
            foreach (string f in written.Take(50))
                sb.AppendLine("  " + Path.GetRelativePath(outDir, f));
            if (written.Count > 50)
                sb.AppendLine($"  ... {written.Count - 50} more");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Writes the Output Job template to <paramref name="target"/>: the file given by OutJob=, or the
        /// Gerber.OutJob embedded in this assembly. Returns a description of the source for the result text.
        /// </summary>
        private static string WriteTemplate(string templatePath, string target)
        {
            if (templatePath.Length > 0)
            {
                string full = Path.GetFullPath(templatePath);
                if (!File.Exists(full))
                    throw new UserException($"Output Job file not found: {full}");
                File.Copy(full, target, overwrite: true);
                return full;
            }

            using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
                ?? throw new UserException($"The embedded resource {TemplateResource} is missing from the build.");
            using (FileStream file = File.Create(target))
                resource.CopyTo(file);
            return "Gerber.OutJob (embedded)";
        }

        private static Dictionary<string, DateTime> Snapshot(string dir) =>
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .ToDictionary(f => f, File.GetLastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
    }
}
