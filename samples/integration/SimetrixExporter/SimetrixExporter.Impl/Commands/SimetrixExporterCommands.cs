using System;
using System.IO;
using DXP; // IClient.GetCurrentView, IServerDocumentView.GetOwnerDocument, IClient.GetDocumentByPath are typed extensions here
using EDP; // IWorkspace.DM_FocusedProject / IProject.DM_ProjectFullPath

namespace AdSamples.SimetrixExporter
{
    /// <summary>
    /// Hands an Altium Designer document (a project, a schematic sheet or a schematic library) to a third-party
    /// native exporter, SIMetrixExporter.DLL, through its ExportToSimetrix export. The SDK part is resolving the
    /// focused project, the active view's document and its kind; the rest is loading a native DLL at run time.
    /// The DLL is not part of this sample: without it both commands stop with a "DLL not found" error after
    /// resolving the document and the target path.
    /// </summary>
    internal static class SimetrixExporterCommands
    {
        /// <summary>
        /// ExportProjectToSimetrix "Out=&lt;folder&gt;|Dll=&lt;path&gt;|DocumentIid=&lt;guid&gt;": exports the focused
        /// project. The target passed to the DLL is &lt;Out&gt;\SXPRJ.
        /// </summary>
        [HotCommand("ExportProjectToSimetrix", "Export the focused project via SIMetrixExporter.DLL")]
        public static string ExportProjectToSimetrix(CommandContext ctx)
        {
            IWorkspace workspace = AltiumSession.Workspace;
            IProject project = workspace.DM_FocusedProject();
            if (project == null || Equals(project, workspace.DM_FreeDocumentsProject()))
                throw new UserException("No project is focused. Select a project (or one of its documents) in the Projects panel.");

            string projectPath = project.DM_ProjectFullPath();
            if (string.IsNullOrEmpty(projectPath))
                throw new UserException("The focused project has no file path. Save the project first.");

            // The DLL gets the project's own server document, looked up by its path.
            IServerDocument projectDocument = DXP.GlobalVars.Client.GetDocumentByPath(projectPath)
                ?? throw new UserException($"No server document is open for the project '{projectPath}'.");

            // The target is a folder with the format name appended as one more path segment. Whether the DLL
            // treats <folder>\SXPRJ as a folder or a file name is not known.
            const string format = "SXPRJ";
            string folder = ctx.Get("Out", Path.GetTempPath());
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, format);

            return SimetrixDll.Export(ctx, target, format, projectDocument, Path.GetFileName(projectPath));
        }

        /// <summary>
        /// ExportFileToSimetrix "Out=&lt;file or folder&gt;|Dll=&lt;path&gt;|DocumentIid=&lt;guid&gt;": exports the
        /// active schematic sheet (SXSCH, .sxsch) or schematic library (SXLIB, .sxlib).
        /// </summary>
        [HotCommand("ExportFileToSimetrix", "Export the active schematic or library via SIMetrixExporter.DLL")]
        public static string ExportFileToSimetrix(CommandContext ctx)
        {
            // The view AD passed to the process (menu), else the current view (pipe).
            IServerDocumentView view = ctx.View as IServerDocumentView ?? DXP.GlobalVars.Client.GetCurrentView();
            IServerDocument document = view?.GetOwnerDocument()
                ?? throw new UserException("Open a schematic sheet or schematic library and make it the active document.");

            string kind = document.GetKind() ?? string.Empty;
            string format, extension;
            if (kind.Equals("SCH", StringComparison.OrdinalIgnoreCase))
            {
                format = "SXSCH";
                extension = ".sxsch";
            }
            else if (kind.Equals("SCHLIB", StringComparison.OrdinalIgnoreCase))
            {
                format = "SXLIB";
                extension = ".sxlib";
            }
            else
            {
                // No export format for any other kind: do not call the DLL.
                throw new UserException($"The active document is a '{kind}' document. Activate a schematic sheet (SCH) or schematic library (SCHLIB).");
            }

            string source = document.GetFileName();
            string defaultName = Path.GetFileNameWithoutExtension(source) + extension;

            // No save dialog, so a call from tools/ad.ps1 never blocks. Out is a file,
            // or a folder (existing, or ending with a separator) that gets the default name.
            string target = ctx.Get("Out");
            if (string.IsNullOrWhiteSpace(target))
                target = Path.Combine(Path.GetTempPath(), defaultName);
            else if (Directory.Exists(target) || target.EndsWith('\\') || target.EndsWith('/'))
                target = Path.Combine(target, defaultName);

            string targetFolder = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!Directory.Exists(targetFolder))
                throw new UserException($"The target folder '{targetFolder}' does not exist.");

            // SXSCH for a schematic sheet, SXLIB for a schematic library.
            return SimetrixDll.Export(ctx, target, format, document, Path.GetFileName(source));
        }
    }
}
