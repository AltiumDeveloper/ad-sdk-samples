using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DXP; // the IClient.OpenDocument/ShowDocument extension methods live in namespace DXP
using EDP;

namespace AdSamples.HierarchicalDesign
{
    /// <summary>
    /// Lists the physical designators of every component instance of the focused project. The components are read
    /// from the project's flattened document: the compiled view in which all sheets of the hierarchy are merged into
    /// one, so a sheet that is used several times (multichannel) contributes one component per channel.
    /// </summary>
    internal static class HierarchicalDesignCommands
    {
        /// <summary>The report file name.</summary>
        private const string ReportFileName = "Project_Report.Txt";

        /// <summary>
        /// GetAllComponents "Out=&lt;file or folder&gt;|Open=true|Max=&lt;n&gt;": returns the component count and the
        /// first Max designators; Out writes all designators to a file (one per line), Open also
        /// opens that file in Altium Designer.
        /// </summary>
        [HotCommand("GetAllComponents", "List the physical designators of the compiled project")]
        public static string GetAllComponents(CommandContext ctx)
        {
            int max = int.TryParse(ctx.Get("Max", "50"), out int m) && m >= 0 ? m : 50;

            // GetDXPWorkspace returns IDXPWorkSpace; the EDP view of the same object is IWorkspace.
            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its documents.");

            // The flattened document exists only in the compiled model. Compile only when the model is stale, and
            // report a failed compile.
            bool compiledNow = false, compileFailed = false;
            if (project.DM_NeedsCompile())
            {
                ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                compiledNow = true;
                compileFailed = !project.DM_Compile();
            }

            IDocument flattened = project.DM_DocumentFlattened()
                ?? throw new UserException("The project has no flattened document. Does it contain a schematic sheet, and does it compile?");

            int count = flattened.DM_ComponentCount();
            var designators = new List<string>(count);
            var lines = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                // Defensive: a null entry is skipped, so the reported count is the number of components actually read,
                // which could then be lower than DM_ComponentCount.
                IComponent component = flattened.DM_Components(i);
                if (component == null)
                    continue;

                // The physical designator is the one that ends up on the PCB, the logical designator the one drawn on
                // the sheet. In the Bluetooth Sentinel example a sheet used six times gave logical LED1 and physical
                // LED1 to LED6; the format of channel designators depends on the project's settings.
                string physical = component.DM_PhysicalDesignator();
                designators.Add(physical);

                if (designators.Count <= max)
                    lines.AppendLine($"  {physical} (logical {component.DM_LogicalDesignator()}, sheet {component.DM_OwnerDocumentName()})");
            }

            var result = new StringBuilder();
            if (compileFailed)
                result.AppendLine("Warning: DM_Compile returned false; the component list may be out of date.");
            result.AppendLine($"{project.DM_ProjectFileName()}: {designators.Count} components in the flattened document" +
                              (compiledNow ? " (compiled first)." : " (already compiled)."));
            result.Append(lines);
            if (designators.Count > max)
                result.AppendLine($"  ... {designators.Count - max} more (Max={max}).");

            string written = WriteReport(ctx, designators);
            if (written != null)
                result.AppendLine("Report written to " + written);
            return result.ToString().TrimEnd();
        }

        /// <summary>Writes the designators when Out or Open is given; returns the path, or null.</summary>
        private static string WriteReport(CommandContext ctx, List<string> designators)
        {
            string path = ctx.Get("Out");
            bool open = ctx.Flag("Open");
            if (string.IsNullOrEmpty(path) && !open)
                return null;

            // The all-user application data folder (ProgramData) may not be writable; use the temp folder unless
            // Out names a file or folder.
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), ReportFileName);
            else if (Directory.Exists(path))
                path = Path.Combine(path, ReportFileName);
            path = Path.GetFullPath(path);
            File.WriteAllLines(path, designators);

            if (open)
            {
                IClient client = DXP.GlobalVars.Client;
                IServerDocument reportDocument = client.OpenDocument("Text", path);
                if (reportDocument != null)
                    client.ShowDocument(reportDocument);
            }
            return path;
        }
    }
}
