using System;
using System.IO;
using System.Text;
using DXP; // OpenDocument is a typed extension method of DXP.IClient
using EDP; // Outputer(i) is a typed extension property of IWSM_OutputJobDocument

namespace AdSamples.OutputJobChangeSource
{
    /// <summary>
    /// Opens an Output Job (.OutJob), finds an outputer by its display name (IOutputer.DM_ViewName) and points it
    /// at another source document with IOutputer.DM_SetDocumentPath. ListOutputJobOutputers reads the outputers back.
    /// </summary>
    internal static class OutputJobChangeSourceCommands
    {
        // Document kind passed to IClient.OpenDocument.
        private const string OutJobKind = "OUTPUTJOB";

        /// <summary>
        /// ChangeOutputJobFile "OutJob=&lt;path&gt;|Outputer=Schematic Prints|Source=&lt;document path&gt;|Save=False|BeginModify=False".
        /// </summary>
        [HotCommand("ChangeOutputJobFile", "Redirect an OutJob outputer to another source document")]
        public static string ChangeOutputJobFile(CommandContext ctx)
        {
            string path = RequireOutJobPath(ctx);
            string outputerName = ctx.Get("Outputer", "Schematic Prints");
            // The value is passed to DM_SetDocumentPath as given:
            // how AD resolves a relative path (OutJob folder or project) is not documented.
            string source = ctx.Get("Source");
            if (string.IsNullOrWhiteSpace(source))
                throw new UserException("No Source specified. Pass the new source document: Source=<document path or file name>.");
            source = source.Trim().Trim('"');
            bool save = ctx.Flag("Save");
            // Optional: bracket the change with BeginModifyOutputer/EndModifyOutputer. Off by default,
            // because with the bracket the change is discarded (README, Notes).
            bool beginModify = ctx.Flag("BeginModify");

            IServerDocument serverDoc = OpenOutJob(path);
            // The SDK interfaces are IDispatch interfaces, so
            // the cast itself succeeds; a wrong object would only fail with a COMException on the first call.
            var outJob = (IWSM_OutputJobDocument)serverDoc;

            var sb = new StringBuilder();
            sb.AppendLine($"OutJob: {serverDoc.GetFileName()} (modified before: {serverDoc.GetModified()})");

            int count = outJob.GetState_OutputerCount();
            int changed = 0;
            for (int i = 0; i < count; i++)
            {
                IOutputer outputer = outJob.GetState_Outputer(i);
                // Exact (case-sensitive) match on the display name. The loop goes on
                // after a match, so every outputer with that name is changed.
                if (outputer == null || !string.Equals(outputer.DM_ViewName(), outputerName, StringComparison.Ordinal))
                    continue;

                string before = outputer.DM_GetDocumentPath();
                ctx.Log($"[{i}] '{outputerName}': DM_SetDocumentPath('{source}')");
                if (beginModify)
                {
                    bool began = outJob.BeginModifyOutputer(outputer);
                    try
                    {
                        outputer.DM_SetDocumentPath(source);
                    }
                    finally
                    {
                        outJob.EndModifyOutputer();
                    }
                    sb.AppendLine($"  [{i}] BeginModifyOutputer returned {began}");
                }
                else
                {
                    outputer.DM_SetDocumentPath(source);
                }

                // Read the path back so the result shows what AD stored (it may resolve or rewrite the value).
                sb.AppendLine($"  [{i}] '{outputerName}' document: '{before}' -> '{outputer.DM_GetDocumentPath()}'");
                changed++;
            }

            if (changed == 0)
                throw new UserException($"No outputer named '{outputerName}' in {Path.GetFileName(path)} ({count} outputers). ListOutputJobOutputers shows their names.");

            // Mark the document modified so that AD treats it as changed (and asks to save).
            serverDoc.SetModified(true);
            sb.AppendLine($"Outputers changed: {changed} of {count}. Modified: {serverDoc.GetModified()}.");

            // Save=True saves the OutJob, so the change can be checked in the file.
            if (save)
                sb.Append($"Saved: {serverDoc.DoSafeFileSave("")}.");
            else
                sb.Append("Not saved (pass Save=True to save the OutJob).");
            return sb.ToString();
        }

        /// <summary>ListOutputJobOutputers "OutJob=&lt;path&gt;": each outputer's name, kind and source document.</summary>
        [HotCommand("ListOutputJobOutputers", "List an Output Job's outputers and their source documents")]
        public static string ListOutputJobOutputers(CommandContext ctx)
        {
            string path = RequireOutJobPath(ctx);
            IServerDocument serverDoc = OpenOutJob(path);
            var outJob = (IWSM_OutputJobDocument)serverDoc;

            int count = outJob.GetState_OutputerCount();
            var sb = new StringBuilder();
            sb.AppendLine($"OutJob: {serverDoc.GetFileName()} (modified: {serverDoc.GetModified()})");
            sb.Append($"Outputers: {count}");
            for (int i = 0; i < count; i++)
            {
                IOutputer o = outJob.GetState_Outputer(i);
                sb.Append(Environment.NewLine).Append(o == null
                    ? $"  [{i}] null"
                    : $"  [{i}] '{o.DM_ViewName()}' generator '{o.DM_GeneratorName()}' kind '{o.DM_GetDocumentKind()}' document '{o.DM_GetDocumentPath()}'");
            }
            return sb.ToString();
        }

        private static string RequireOutJobPath(CommandContext ctx)
        {
            // The path of the OutJob is a required parameter.
            string path = ctx.Get("OutJob");
            if (string.IsNullOrWhiteSpace(path))
                throw new UserException("No OutJob specified. Pass the path of an .OutJob file: OutJob=<path>.");
            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(path))
                throw new UserException($"OutJob file not found: {path}");
            return path;
        }

        private static IServerDocument OpenOutJob(string path)
        {
            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");
            // A UserException is returned as text.
            return client.OpenDocument(OutJobKind, path)
                ?? throw new UserException($"Altium Designer could not open {path} as an Output Job.");
        }
    }
}
