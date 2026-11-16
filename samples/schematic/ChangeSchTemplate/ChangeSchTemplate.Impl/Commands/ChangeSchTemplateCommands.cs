using System;
using System.IO;
using DXP; // GetDXPWorkspace is an extension method of DXP.IClient: without this using it does not compile
using EDP;
using SCH;

namespace AdSamples.ChangeSchTemplate
{
    /// <summary>
    /// Applies a schematic sheet template (.SchDot) to the active schematic sheet by running AD's own
    /// Sch:ChangeCurrentTemplate process with DXP.Utils.RunCommand. The template folder comes from the workspace
    /// preferences (IWorkspacePreferences.GetTemplatePath_Sch).
    /// </summary>
    internal static class ChangeSchTemplateCommands
    {
        // The default of the Template parameter.
        private const string DefaultTemplate = "A4.SchDot";

        /// <summary>
        /// ChangeTemplate "Template=&lt;file name or full path&gt;|Silent=&lt;true|false&gt;": applies the template to the
        /// active schematic sheet and returns the sheet's template file name before and after.
        /// </summary>
        [HotCommand("ChangeTemplate", "Apply a sheet template to the active schematic")]
        public static string ChangeTemplate(CommandContext ctx)
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            // A schematic library is an ISch_Document too; tell them apart by the object ID (schematic basics guide).
            if (document.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet first.");

            string templateFile = ResolveTemplate(ctx.Get("Template", DefaultTemplate));

            // Silent is passed on to Sch:ChangeCurrentTemplate. Without it AD may ask the user to confirm. A modal dialog on the pipe path would block AD,
            // so Silent=false is accepted only from the AD menu.
            bool silent = !string.Equals(ctx.Get("Silent", "true"), "false", StringComparison.OrdinalIgnoreCase);
            if (!silent && !ctx.IsInteractive)
                throw new UserException("Silent=false may open a dialog; run it from the AD menu, not from the pipe.");

            string previous = document.GetState_TemplateFileName();

            // The 2-argument RunCommand acts on the focused document, which GetCurrentSchDocument just returned.
            // The parameter string is AD style Key=Value|Key=Value; FileName and Silent are not in the SDK reference.
            string parameters = "FileName=" + templateFile + (silent ? "|Silent=True" : string.Empty);
            DXP.Utils.RunCommand("Sch:ChangeCurrentTemplate", parameters);

            // Read the result back from the document rather than trusting the process: RunCommand returns nothing.
            string current = document.GetState_TemplateFileName();
            string changed = string.Equals(previous, current, StringComparison.OrdinalIgnoreCase)
                ? "unchanged"
                : "changed";
            return $"{Path.GetFileName(document.GetState_DocumentName())}: template {changed}, " +
                   $"'{previous}' -> '{current}' (applied {templateFile}). Not saved.";
        }

        /// <summary>
        /// A rooted path is used as given. A bare file name is looked up in the schematic template folder of the
        /// workspace preferences. Either way the file must exist.
        /// </summary>
        private static string ResolveTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new UserException("Template is empty. Pass Template=<file name or full path of a .SchDot>.");

            string path = template;
            if (!Path.IsPathRooted(template))
            {
                // The .NET way to get the workspace, with no P/Invoke into WorkspaceManager.dll.
                var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                    ?? throw new UserException("The workspace is not available.");
                IWorkspacePreferences preferences = workspace.DM_Preferences()
                    ?? throw new UserException("The workspace preferences are not available.");
                string folder = preferences.GetTemplatePath_Sch();
                if (string.IsNullOrWhiteSpace(folder))
                    throw new UserException("No schematic template folder is configured. Pass a full path: Template=<path>.");
                path = Path.Combine(folder, template);
            }

            if (!File.Exists(path))
                throw new UserException($"Template file not found: {path}.{Available(Path.GetDirectoryName(path))}");
            return path;
        }

        /// <summary>
        /// The .SchDot files in <paramref name="folder"/>, for the "not found" message. AD ships ISO/ANSI
        /// templates ("ISO A3 Landscape.SchDot" and so on) and no "A4.SchDot", so the default name
        /// does not resolve: listing what is there turns a dead end into a usable hint.
        /// </summary>
        private static string Available(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return string.Empty;
            string[] files = Directory.GetFiles(folder, "*.SchDot");
            if (files.Length == 0)
                return $" No .SchDot files in {folder}.";
            return " Available: " + string.Join(", ", Array.ConvertAll(files, Path.GetFileName)) + ".";
        }
    }
}
