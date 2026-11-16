using System;
using System.Collections.Generic;
using System.IO;
using DXP; // IClient.GetDXPWorkspace is a typed helper in the DXP namespace
using EDP; // the DM_* objects of the workspace, project and documents

namespace AdSamples.SchChildDocumentCount
{
    /// <summary>
    /// Compiles the project of the focused schematic and reports how many child documents the sheet has in the
    /// project's document hierarchy (IDocument.DM_ChildDocumentCount), with their names (IDocument.DM_ChildDocuments).
    /// The hierarchy is part of the project data model, which is built by compiling, so the count is read both before
    /// and after the compile.
    /// </summary>
    internal static class SchChildDocumentCountCommands
    {
        /// <summary>
        /// CountChild "Compile=true|false": compiles the project (unless Compile=false) and returns the child count of
        /// the focused schematic document.
        /// </summary>
        [HotCommand("CountChild", "Compile the project and count the child documents of the focused schematic")]
        public static string CountChild(CommandContext ctx)
        {
            string compileValue = ctx.Get("Compile", "true");
            bool compile = !(compileValue.Equals("false", StringComparison.OrdinalIgnoreCase) || compileValue == "0");

            IWorkspace workspace = Workspace();
            IDocument document = FocusedSchematic(workspace);
            string path = document.DM_FullPath();

            // The document's own project, which is normally the focused project.
            IProject project = document.DM_Project()
                ?? throw new UserException($"{Path.GetFileName(path)} has no project.");

            var lines = new List<string>();
            IProject freeDocuments = workspace.DM_FreeDocumentsProject();
            bool isFree = freeDocuments != null &&
                          string.Equals(freeDocuments.DM_ProjectFullPath(), project.DM_ProjectFullPath(), StringComparison.OrdinalIgnoreCase);
            if (isFree)
                lines.Add("Note: the document is a free document (not part of a project), so it has no sheet hierarchy.");

            int countBefore = document.DM_ChildDocumentCount();
            string compileState;
            if (compile)
            {
                // DM_Compile returns a bool, so a failed compile is reported.
                bool compiled = project.DM_Compile();
                compileState = compiled ? "compiled" : "DM_Compile returned false, the count may be stale";

                // Query the focused document again after the compile instead of reusing the object read before it.
                document = FocusedSchematic(workspace);
            }
            else
            {
                compileState = "not compiled (Compile=false)";
            }

            int count = document.DM_ChildDocumentCount();
            lines.Insert(0, $"{Path.GetFileName(path)} (project {ProjectName(project)}): {count} child documents " +
                            $"({compileState}; before the compile: {countBefore}).");

            for (int i = 0; i < count; i++)
            {
                IDocument child = document.DM_ChildDocuments(i);
                lines.Add("    " + (child == null ? "<null>" : child.DM_FileName()));
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>The focused document, which must be a schematic sheet (DM_DocumentKind "SCH").</summary>
        private static IDocument FocusedSchematic(IWorkspace workspace)
        {
            IDocument document = workspace.DM_FocusedDocument()
                ?? throw new UserException("Open a schematic first.");
            string kind = document.DM_DocumentKind();
            if (!string.Equals(kind, "SCH", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"This is not a schematic document ({Path.GetFileName(document.DM_FullPath())} is '{kind}'). Focus a .SchDoc first.");
            return document;
        }

        private static IWorkspace Workspace() =>
            DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");

        private static string ProjectName(IProject project)
        {
            string path = project.DM_ProjectFullPath();
            return string.IsNullOrEmpty(path) ? project.DM_ProjectFileName() : Path.GetFileName(path);
        }
    }
}
