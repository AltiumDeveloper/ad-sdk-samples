using System;
using System.Collections.Generic;
using System.IO;
using DXP;
using EDP;

namespace AdSamples.ProjectCreateWithSchematic
{
    /// <summary>
    /// Creates a new PCB project and adds a blank schematic to it. The SDK has no "create project" method on
    /// IWorkspace, so the project is created by sending the workspace manager's own process
    /// (WorkspaceManager:OpenObject), and the schematic with IWorkspace.DM_CreateNewDocument.
    /// </summary>
    internal static class ProjectCreateWithSchematicCommands
    {
        [HotCommand("CreateProject", "Create a PCB project with a blank schematic")]
        public static string CreateProject(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // The schematic server must be running before a schematic document can be created.
            if (SCH.GlobalVars.SchServer == null && !client.StartServer("SCH"))
                throw new UserException("Could not start the schematic server (SCH).");

            var workspace = client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");

            // Remember the open projects, so the command can tell whether a new project really appeared. The paths
            // are only used for the "was it already open" test; the reported counts both come from DM_ProjectCount.
            int projectsBefore = workspace.DM_ProjectCount();
            var pathsBefore = OpenProjectPaths(workspace);

            // Create a blank PCB project through the workspace manager's process.
            DXP.Utils.MessageRouterSendCommandToModule("WorkspaceManager:OpenObject", "Kind=PcbProject|ObjectKind=NewAnything", null);

            // Check that the new project is the focused one: if no new project was created and
            // focused, adding the schematic would change a project the user already had open.
            IProject project = workspace.DM_FocusedProject();
            string projectPath = project?.DM_ProjectFullPath();
            int projectsAfter = workspace.DM_ProjectCount();
            if (project == null || pathsBefore.Contains(projectPath ?? ""))
                throw new UserException(
                    $"WorkspaceManager:OpenObject did not create and focus a new project (projects open: {projectsBefore} before, " +
                    $"{projectsAfter} after; focused: '{projectPath ?? "none"}'). No schematic was added.");

            int documentsBefore = project.DM_LogicalDocumentCount();

            // Create a blank schematic. The return value is the bare name of the new, unsaved document
            // (e.g. "Sheet1.SchDoc"), and the document is already added to the focused project.
            string schPath = workspace.DM_CreateNewDocument("SCH");
            if (string.IsNullOrEmpty(schPath))
                throw new UserException($"DM_CreateNewDocument(\"SCH\") returned no path. Project '{projectPath}' was created without a schematic.");
            int documentsAfterCreate = project.DM_LogicalDocumentCount();

            // Add it to the new project as a source document.
            project.DM_AddSourceDocument(schPath);
            int documentsAfterAdd = project.DM_LogicalDocumentCount();

            // Open and show the schematic (OpenDocument and ShowDocument are extension methods from DXP).
            IServerDocument doc = client.OpenDocument("SCH", schPath);
            if (doc != null)
                client.ShowDocument(doc);

            // The document counts after each step are reported because whether DM_CreateNewDocument already adds
            // the document to the focused project is not documented.
            return $"Created project '{projectPath}' with schematic '{schPath}'.\n" +
                   $"Projects open: {projectsBefore} before, {projectsAfter} after.\n" +
                   $"Project documents: {documentsBefore} before, {documentsAfterCreate} after DM_CreateNewDocument, " +
                   $"{documentsAfterAdd} after DM_AddSourceDocument.\n" +
                   $"Schematic {(doc != null ? $"opened as '{Path.GetFileName(doc.GetFileName())}'" : "could not be opened")}. " +
                   "Nothing is saved.";
        }

        private static HashSet<string> OpenProjectPaths(IWorkspace workspace)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < workspace.DM_ProjectCount(); i++)
            {
                string path = workspace.DM_Projects(i)?.DM_ProjectFullPath();
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            return paths;
        }
    }
}
