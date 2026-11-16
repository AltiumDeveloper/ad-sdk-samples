using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;

namespace AdSamples.ChangePhysicalSheet
{
    /// <summary>
    /// Multi-channel designs: one logical schematic sheet (a SchDoc referenced by a repeated sheet symbol, or by
    /// several sheet symbols) is expanded by the compiler into several physical documents. An open schematic can show
    /// any one of them; ILogicalDocument selects which one is active.
    /// </summary>
    internal static class ChangePhysicalSheetCommands
    {
        // The editor-tab update view notification (cViewNotification_EditorTabUpdate). DXP.NotificationCode.ViewNotificationCode
        // in the .NET SDK has no member with the value 45, so the number is kept here under that name.
        private const int ViewNotification_EditorTabUpdate = 45;

        /// <summary>
        /// Read-only: lists the project's physical documents and, for every schematic that is already open, the
        /// physical sheets its ILogicalDocument knows and which one is active. Opens no document and changes none; it
        /// compiles the project unless Compile=false.
        /// </summary>
        [HotCommand("ListPhysicalSheets", "List the physical sheets of each schematic")]
        public static string ListPhysicalSheets(CommandContext ctx)
        {
            IClient client = Client();
            IProject project = FocusedProject();
            Compile(ctx, project);

            var text = new StringBuilder();
            int count = project.DM_PhysicalDocumentCount();
            text.AppendLine($"{Path.GetFileName(project.DM_ProjectFileName())}: {count} physical documents");
            for (int i = 0; i < count; i++)
            {
                IDocument document = project.DM_PhysicalDocuments(i);
                text.AppendLine($"  [{i}] {document.DM_DocumentKind()} {document.DM_FileName()}" +
                                $" instance='{document.DM_PhysicalInstanceName()}' path='{document.DM_PhysicalInstancePath()}'" +
                                $" channel={document.DM_ChannelIndex()}");
            }

            foreach (string path in SchematicPaths(project))
            {
                // GetDocumentByPath does not open anything: a closed sheet is only reported.
                IServerDocument serverDocument = client.GetDocumentByPath(path);
                if (serverDocument == null)
                {
                    text.AppendLine($"{Path.GetFileName(path)}: not open");
                    continue;
                }

                try
                {
                    ILogicalDocument logical = AsLogicalDocument(serverDocument);
                    List<string> sheets = PhysicalSheets(logical);
                    text.AppendLine($"{Path.GetFileName(path)}: {sheets.Count} physical sheets, active '{logical.GetActivePhysicalDocument()}'");
                    foreach (string sheet in sheets)
                        text.AppendLine($"    '{sheet}'");
                }
                catch (COMException ex)
                {
                    text.AppendLine($"{Path.GetFileName(path)}: ILogicalDocument call failed: {ex.Message} (0x{ex.HResult:X8})");
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Compiles the focused project, opens each schematic and makes each of its
        /// physical sheets active in turn. Returns what was visited.
        /// Parameters: Notify=true broadcasts the editor-tab update view notification after each switch
        /// (off by default, see README); Restore=false leaves the last physical sheet active; Compile=false skips
        /// the compile.
        /// </summary>
        [HotCommand("CyclePhysicalSheets", "Make each physical sheet active in turn")]
        public static string CyclePhysicalSheets(CommandContext ctx)
        {
            IClient client = Client();
            IProject project = FocusedProject();
            bool notify = ctx.Flag("Notify");
            bool restore = BoolParam(ctx, "Restore", true);
            Compile(ctx, project);

            var text = new StringBuilder();
            int switches = 0, notifications = 0;
            List<string> paths = SchematicPaths(project);
            text.AppendLine($"{Path.GetFileName(project.DM_ProjectFileName())}: {paths.Count} schematic documents");

            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                ctx.Log($"Opening {name}");
                IServerDocument serverDocument = client.OpenDocument("SCH", path);
                if (serverDocument == null)
                {
                    text.AppendLine($"{name}: could not be opened");
                    continue;
                }
                client.ShowDocument(serverDocument);

                try
                {
                    ILogicalDocument logical = AsLogicalDocument(serverDocument);
                    List<string> sheets = PhysicalSheets(logical);
                    string original = logical.GetActivePhysicalDocument();
                    text.AppendLine($"{name}: {sheets.Count} physical sheets, active before '{original}'");

                    foreach (string sheet in sheets)
                    {
                        // The names from GetPhysicalDocuments are passed back unchanged.
                        logical.SetActivePhysicalDocument(sheet);
                        switches++;
                        if (notify)
                        {
                            // An SDK notification object (DXP.ViewNotification implements DXP.INotification), so the
                            // sample defines no COM-visible class of its own.
                            client.BroadcastNotification(new ViewNotification(ViewNotification_EditorTabUpdate));
                            notifications++;
                        }
                        text.AppendLine($"    set '{sheet}' -> active '{logical.GetActivePhysicalDocument()}'");
                    }

                    // An empty "before" value is not passed back: what an empty name does is not documented.
                    if (restore && sheets.Count > 0 && !string.IsNullOrEmpty(original))
                    {
                        logical.SetActivePhysicalDocument(original);
                        if (notify)
                        {
                            client.BroadcastNotification(new ViewNotification(ViewNotification_EditorTabUpdate));
                            notifications++;
                        }
                        text.AppendLine($"    restored -> active '{logical.GetActivePhysicalDocument()}'");
                    }
                }
                catch (COMException ex)
                {
                    text.AppendLine($"{name}: ILogicalDocument call failed: {ex.Message} (0x{ex.HResult:X8})");
                }
            }

            text.AppendLine($"Done: {switches} switches, {notifications} view notifications (code {ViewNotification_EditorTabUpdate}) broadcast.");
            return text.ToString().TrimEnd();
        }

        private static IClient Client() =>
            DXP.GlobalVars.Client ?? throw new UserException("The Altium Designer client is not available.");

        private static IProject FocusedProject()
        {
            IWorkspace workspace = Client().GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("No workspace is available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Focus a schematic project in the Projects panel.");
            // Documents outside any project belong to the "Free Documents" pseudo project.
            if (string.Equals(Path.GetFileNameWithoutExtension(project.DM_ProjectFileName()), "Free Documents", StringComparison.OrdinalIgnoreCase))
                throw new UserException("The focused project is Free Documents. Focus a saved schematic project.");
            return project;
        }

        private static void Compile(CommandContext ctx, IProject project)
        {
            // The physical document list exists only after a compile.
            if (!BoolParam(ctx, "Compile", true))
                return;
            ctx.Log("Compiling " + Path.GetFileName(project.DM_ProjectFileName()));
            bool cancelled = false;
            project.DM_CompileEx(true, ref cancelled);
            if (cancelled)
                throw new UserException("The compile was cancelled.");
        }

        /// <summary>
        /// Distinct full paths of the project's SCH physical documents. A repeated sheet appears once per channel
        /// in DM_PhysicalDocuments, so duplicate paths are removed.
        /// </summary>
        private static List<string> SchematicPaths(IProject project)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            int count = project.DM_PhysicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDocument document = project.DM_PhysicalDocuments(i);
                if (document == null || !string.Equals(document.DM_DocumentKind(), "SCH", StringComparison.OrdinalIgnoreCase))
                    continue;
                string path = document.DM_FullPath();
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                    paths.Add(path);
            }
            return paths;
        }

        /// <summary>
        /// The .NET form of an IDispatch(ServerDocument) cast. Both interfaces are IDispatch-based in the .NET SDK, so the
        /// cast itself always succeeds; whether the server document answers the ILogicalDocument members is only
        /// known when they are called (a COMException otherwise). The null check is a
        /// guard in case the cast can fail after all.
        /// </summary>
        private static ILogicalDocument AsLogicalDocument(IServerDocument serverDocument) =>
            serverDocument as ILogicalDocument
            ?? throw new UserException($"{Path.GetFileName(serverDocument.GetFileName())} is not an ILogicalDocument.");

        private static List<string> PhysicalSheets(ILogicalDocument logical)
        {
            var sheets = new List<string>();
            int count = logical.GetPhysicalDocumentCount();
            for (int j = 0; j < count; j++)
                sheets.Add(logical.GetPhysicalDocuments(j));
            return sheets;
        }

        /// <summary>A Key=true|false parameter with a default (ctx.Flag is false when the key is absent).</summary>
        private static bool BoolParam(CommandContext ctx, string name, bool defaultValue)
        {
            string value = ctx.Get(name);
            if (value == null)
                return defaultValue;
            return value.Length == 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }
    }
}
