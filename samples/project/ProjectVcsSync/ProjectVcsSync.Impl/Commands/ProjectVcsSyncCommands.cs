using System;
using System.Collections.Generic;
using System.IO;
using DXP;

namespace AdSamples.ProjectVcsSync
{
    /// <summary>
    /// Reads the version control (VCS) status of every logical document of the focused project and, in
    /// SyncProjectVcs, updates the out-of-date documents and commits the modified ones.
    /// The VCS service is the "VersionControl" server module, used through DXP.IVcsManager; the project's binding to
    /// its repository is a DXP.IVcsProject.
    /// </summary>
    internal static class ProjectVcsSyncCommands
    {
        private const string DefaultMessage = "Automatically commit changes";

        /// <summary>
        /// GetProjectVcsStatus "[List=true]": read-only. One line per logical document with its status flags.
        /// List=true also calls IVcsProvider.ListRepository for the project's folder.
        /// </summary>
        [HotCommand("GetProjectVcsStatus", "Report the VCS status of the focused project's documents")]
        public static string GetProjectVcsStatus(CommandContext ctx)
        {
            VcsBinding binding = Bind();
            if (binding.NotControlled != null)
                return binding.NotControlled;

            var lines = new List<string> { binding.Header() };

            if (ctx.Flag("List"))
                lines.Add(ListRepository(binding));

            int count = binding.Project.DM_LogicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDXPDocument document = binding.Project.DM_LogicalDocuments(i);
                if (document == null)
                    continue;
                string path = document.DM_FullPath();
                TVCSStatusSet status = binding.Manager.GetStatus(binding.VcsProject, path);
                lines.Add($"  {Path.GetFileName(path)}: {Describe(status)} ('{binding.Manager.GetStatusString(binding.VcsProject, path)}')" +
                          $" -> {Planned(status)}");
            }
            lines.Add($"{count} logical documents.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// SyncProjectVcs "[DryRun=false]|[Message=text]": for each logical document, Update when it is out of date,
        /// else Commit when it is modified. DryRun is true unless DryRun=false is passed,
        /// because a commit cannot be undone from AD.
        /// </summary>
        [HotCommand("SyncProjectVcs", "Update out-of-date and commit modified documents (dry run by default)")]
        public static string SyncProjectVcs(CommandContext ctx)
        {
            string dryRunText = ctx.Get("DryRun", "true");
            bool dryRun = !(dryRunText.Equals("false", StringComparison.OrdinalIgnoreCase) || dryRunText == "0");
            string message = ctx.Get("Message", DefaultMessage);
            if (string.IsNullOrWhiteSpace(message))
                throw new UserException("Message must not be empty.");

            VcsBinding binding = Bind();
            if (binding.NotControlled != null)
                return binding.NotControlled;

            // The manager's factory gives a list without contacting the repository.
            IVcsFileNamesFactory factory = binding.Manager.GetVcsFileNamesFactory()
                ?? throw new UserException("The VersionControl server returned no file name factory.");

            var lines = new List<string> { binding.Header() + (dryRun ? " Dry run: nothing is changed (pass DryRun=false to act)." : "") };
            int updated = 0, committed = 0;

            int count = binding.Project.DM_LogicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDXPDocument document = binding.Project.DM_LogicalDocuments(i);
                if (document == null)
                    continue;
                string path = document.DM_FullPath();
                string name = Path.GetFileName(path);
                TVCSStatusSet status = binding.Manager.GetStatus(binding.VcsProject, path);

                if (status.Contains(TVCSStatus.vcsOutOfDate))
                {
                    updated++;
                    if (dryRun)
                    {
                        lines.Add($"  {name}: out of date, would Update");
                        continue;
                    }
                    ctx.Log($"Update {name}");
                    // argForce = true. The meaning of the int result is not documented.
                    int result = binding.VcsProject.Update(factory.CreateFromSingleFileName(path), true);
                    lines.Add($"  {name}: Update returned {result}; status now {Describe(binding.Manager.GetStatus(binding.VcsProject, path))}");
                }
                else if (status.Contains(TVCSStatus.vcsModified))
                {
                    committed++;
                    if (dryRun)
                    {
                        lines.Add($"  {name}: modified, would Commit with message '{message}'");
                        continue;
                    }
                    ctx.Log($"Commit {name}");
                    int result = binding.VcsProject.Commit(factory.CreateFromSingleFileName(path), message);
                    lines.Add($"  {name}: Commit returned {result}; status now {Describe(binding.Manager.GetStatus(binding.VcsProject, path))}");
                }
            }

            string verb = dryRun ? "would be" : "were";
            lines.Add($"{count} logical documents: {updated} {verb} updated, {committed} {verb} committed.");
            return string.Join(Environment.NewLine, lines);
        }

        // ---------------------------------------------------------------------------------------------------------

        /// <summary>The focused project with its VCS manager and binding, or the reason it is not under version control.</summary>
        private sealed class VcsBinding
        {
            public IDXPProject Project;
            public IVcsManager Manager;
            public IVcsProject VcsProject;
            public string Location;
            public string NotControlled;

            public string Header() =>
                $"{ProjectName(Project)}: provider {VcsProject.ProviderType()}, repository root '{VcsProject.GetRepositoryRoot()}', " +
                $"location '{Location}'.";
        }

        private static VcsBinding Bind()
        {
            IClient client = DXP.GlobalVars.Client ?? throw new UserException("The client is not available.");

            // The VersionControl server module itself implements IVcsManager.
            IServerModule server = client.GetServerModuleByName("VersionControl")
                ?? throw new UserException("The VersionControl server is not loaded.");
            IVcsManager manager = server as IVcsManager
                ?? throw new UserException("The VersionControl server does not expose IVcsManager.");

            IDXPWorkSpace workspace = client.GetDXPWorkspace()
                ?? throw new UserException("The workspace is not available.");
            IDXPProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project and select it in the Projects panel.");

            var binding = new VcsBinding { Project = project, Manager = manager };

            // The IDXPProject.VCSProject property is DM_GetVCSProject in .NET and returns object.
            binding.VcsProject = project.DM_GetVCSProject() as IVcsProject;
            if (binding.VcsProject == null)
            {
                binding.NotControlled = $"{ProjectName(project)}: project is not under version control (no VCS project).";
                return binding;
            }

            // An empty repository location of the project folder means "not under version control".
            string projectDir = Path.GetDirectoryName(project.DM_ProjectFullPath()) ?? string.Empty;
            binding.Location = manager.GetLocationInRepository(binding.VcsProject, projectDir);
            if (string.IsNullOrEmpty(binding.Location))
                binding.NotControlled = $"{ProjectName(project)}: project is not under version control.";
            return binding;
        }

        /// <summary>Calls ListRepository for the project file; its result is only counted.</summary>
        private static string ListRepository(VcsBinding binding)
        {
            IVcsProvider provider = binding.Manager.GetProviderByType(binding.VcsProject.ProviderType());
            if (provider == null)
                return $"  No provider for {binding.VcsProject.ProviderType()}.";
            string root = binding.VcsProject.GetRepositoryRoot();
            string path = binding.VcsProject.GetLocationInRepository(binding.Project.DM_ProjectFullPath());
            IVcsFileNames files = provider.ListRepository(root, path, false);
            int n = files?.GetCount() ?? 0;
            var names = new List<string>();
            for (int i = 0; i < n && i < 20; i++)
                names.Add(files.GetItems(i));
            return $"  {provider.GetName()}.ListRepository('{root}', '{path}'): {n} entries" +
                   (n > 0 ? ": " + string.Join(", ", names) + (n > 20 ? ", ..." : "") : "");
        }

        private static string Describe(TVCSStatusSet status)
        {
            var flags = new List<string>();
            foreach (TVCSStatus value in Enum.GetValues(typeof(TVCSStatus)))
                if (status.Contains(value))
                    flags.Add(value.ToString());
            return flags.Count == 0 ? "[]" : "[" + string.Join(", ", flags) + "]";
        }

        private static string Planned(TVCSStatusSet status) =>
            status.Contains(TVCSStatus.vcsOutOfDate) ? "Update"
            : status.Contains(TVCSStatus.vcsModified) ? "Commit"
            : "nothing";

        private static string ProjectName(IDXPProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
