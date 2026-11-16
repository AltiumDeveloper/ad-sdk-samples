using System;
using System.IO;
using System.Text;
using DXP; // OpenDocument and GetDXPWorkspace are typed extension methods of DXP.IClient
using EDP; // DM_FocusedProject, OutputMedium(i), CreateOutputer... are typed extension methods in EDP

namespace AdSamples.OutJobCreateOutputer
{
    /// <summary>
    /// Opens an Output Job (.OutJob) document, adds it to the focused project and adds outputers to it with
    /// IWSM_OutputJobDocument.CreateOutputer. ListOutputers reads the media and outputers back.
    /// </summary>
    internal static class OutJobCreateOutputerCommands
    {
        // Document kind passed to IClient.OpenDocument.
        private const string OutJobKind = "OUTPUTJOB";

        /// <summary>
        /// CreateOutputer "OutJob=&lt;path&gt;|Category=Documentation|Type=PCB Print|Name=MyOutputer|MediumType=PDF".
        /// </summary>
        [HotCommand("CreateOutputer", "Add an outputer to an Output Job")]
        public static string CreateOutputer(CommandContext ctx)
        {
            string path = RequireOutJobPath(ctx);
            string category = ctx.Get("Category", "Documentation");
            string outputType = ctx.Get("Type", "PCB Print");
            string outputerName = ctx.Get("Name", "MyOutputer");
            string mediumType = ctx.Get("MediumType", "PDF");

            // Check the project first,
            // so that nothing is opened or saved when the command cannot complete.
            IProject project = FocusedProject();

            IServerDocument serverDoc = OpenOutJob(path);

            // Save the document right after opening it, with an empty kind argument.
            bool saved = serverDoc.DoSafeFileSave("");

            // Adding the document only when it is not in the project yet
            // keeps repeated runs from listing it twice.
            string projectName = Path.GetFileName(project.DM_ProjectFileName());
            bool added = false;
            if (project.DM_IndexOfSourceDocument(serverDoc.GetFileName()) < 0)
            {
                project.DM_AddSourceDocument(serverDoc.GetFileName());
                added = true;
            }

            // The SDK interfaces are IDispatch interfaces, so this cast always succeeds (docs: interfaces at run
            // time). The members used here work on the .NET proxy
            // (a wrong object would throw a COMException on the first call).
            var outJob = (IWSM_OutputJobDocument)serverDoc;

            int outputersBefore = outJob.GetState_OutputerCount();
            int mediumCount = outJob.GetState_OutputMediumCount();

            var sb = new StringBuilder();
            sb.AppendLine($"OutJob: {serverDoc.GetFileName()}");
            sb.AppendLine($"Saved after opening: {saved}. Added to {projectName}: {(added ? "yes" : "no, already in the project")}");
            sb.AppendLine($"Output media: {mediumCount}");

            // One CreateOutputer call per medium of the requested type. CreateOutputer takes
            // no medium argument, so the loop decides how many outputers are created, not where they go.
            int matching = 0;
            int created = 0;
            for (int i = 0; i < mediumCount; i++)
            {
                IOutputMedium medium = outJob.GetState_OutputMedium(i);
                string type = medium?.GetState_TypeString() ?? "";
                bool match = string.Equals(type, mediumType, StringComparison.OrdinalIgnoreCase);
                sb.AppendLine($"  [{i}] '{medium?.GetState_Name()}' type '{type}'{(match ? " -> CreateOutputer" : "")}");
                if (!match)
                    continue;

                matching++;
                ctx.Log($"CreateOutputer('{category}', '{outputType}', '{outputerName}') for medium {i}");
                IOutputer outputer = outJob.CreateOutputer(category, outputType, outputerName);
                if (outputer == null)
                {
                    sb.AppendLine("      CreateOutputer returned null");
                    continue;
                }
                created++;
                sb.AppendLine($"      created: {Describe(outputer)}");
            }

            int outputersAfter = outJob.GetState_OutputerCount();
            sb.AppendLine($"Media of type '{mediumType}': {matching}. Outputers created: {created}.");
            sb.Append($"OutputerCount: {outputersBefore} -> {outputersAfter}. The OutJob was saved before the outputers were added: save it again to keep them.");
            return sb.ToString();
        }

        /// <summary>ListOutputers "OutJob=&lt;path&gt;": the OutJob's media (with their outputers) and all outputers.</summary>
        [HotCommand("ListOutputers", "List an Output Job's media and outputers")]
        public static string ListOutputers(CommandContext ctx)
        {
            string path = RequireOutJobPath(ctx);
            IServerDocument serverDoc = OpenOutJob(path);
            var outJob = (IWSM_OutputJobDocument)serverDoc;

            var sb = new StringBuilder();
            sb.AppendLine($"OutJob: {serverDoc.GetFileName()} (modified: {serverDoc.GetModified()})");

            int mediumCount = outJob.GetState_OutputMediumCount();
            sb.AppendLine($"Output media: {mediumCount}");
            for (int i = 0; i < mediumCount; i++)
            {
                IOutputMedium medium = outJob.GetState_OutputMedium(i);
                if (medium == null)
                {
                    sb.AppendLine($"  [{i}] null");
                    continue;
                }
                int n = outJob.GetState_MediumOutputersCount(medium);
                sb.AppendLine($"  [{i}] '{medium.GetState_Name()}' type '{medium.GetState_TypeString()}', outputers: {n}");
                for (int j = 0; j < n; j++)
                    sb.AppendLine($"      {Describe(outJob.GetState_MediumOutputer(medium, j))}");
            }

            int outputerCount = outJob.GetState_OutputerCount();
            sb.Append($"Outputers: {outputerCount}");
            for (int i = 0; i < outputerCount; i++)
                sb.Append(Environment.NewLine).Append($"  [{i}] {Describe(outJob.GetState_Outputer(i))}");
            return sb.ToString();
        }

        // DM_ViewName is the outputer's own name (the argOutputerName of CreateOutputer, the name shown in the OutJob
        // editor); DM_GetDescription is the predefined output's default name ('MyOutputer' vs
        // 'PCB Prints').
        private static string Describe(IOutputer outputer) =>
            outputer == null
                ? "null"
                : $"'{outputer.DM_ViewName()}' ({outputer.DM_GetDescription()}) generator '{outputer.DM_GeneratorName()}' document '{outputer.DM_GetDocumentPath()}'";

        private static string RequireOutJobPath(CommandContext ctx)
        {
            // The path of the OutJob is a required parameter.
            string path = ctx.Get("OutJob");
            if (string.IsNullOrWhiteSpace(path))
                throw new UserException("No OutJob specified. Pass the path of an .OutJob file: OutJob=<path>.");
            path = Path.GetFullPath(path.Trim().Trim('"'));
            // A missing file is an expected failure.
            if (!File.Exists(path))
                throw new UserException($"OutJob file not found: {path}");
            return path;
        }

        private static IProject FocusedProject()
        {
            // Get the workspace and project again in each command (docs: interfaces at run time).
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and focus one of its documents.");
        }

        private static IServerDocument OpenOutJob(string path)
        {
            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");
            return client.OpenDocument(OutJobKind, path)
                ?? throw new UserException($"Altium Designer could not open {path} as an Output Job.");
        }
    }
}
