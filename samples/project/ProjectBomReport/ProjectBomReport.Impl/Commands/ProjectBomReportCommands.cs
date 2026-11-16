using System;
using System.Collections.Generic;
using System.IO;
using DXP; // IClient.GetDXPWorkspace and IClient.GetClientAPI are typed helpers in the DXP namespace
using EDP; // the DM_* objects of the workspace, project and documents
using SCH;

namespace AdSamples.ProjectBomReport
{
    /// <summary>
    /// Walks every schematic sheet of the focused project with an ISch_Iterator and reports each component: designator,
    /// current PCB footprint, comment, description, its parameters, its pins and where it comes from (a workspace
    /// library or a file-based library). The command returns the report as text.
    /// </summary>
    internal static class ProjectBomReportCommands
    {
        /// <summary>AppDomain data key of the last report (a string, so it survives a hot reload). The panel shows it.</summary>
        internal const string LastReportKey = "ProjectBomReport.LastReport";

        /// <summary>
        /// ProjectBomReport "Linkage=true|false|Out=&lt;file or folder&gt;": builds the report of the focused project.
        /// From the pipe it returns the whole report; from a menu or the panel it shows the report in the panel.
        /// </summary>
        [HotCommand("ProjectBomReport", "Report the components of the focused project as BOM text")]
        public static string ProjectBomReport(CommandContext ctx)
        {
            string linkageValue = ctx.Get("Linkage", "true");
            bool linkage = !(linkageValue.Equals("false", StringComparison.OrdinalIgnoreCase) || linkageValue == "0");

            IWorkspace workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a project (.PrjPcb) and focus one of its documents.");

            // The report is for a real project only, not for free documents.
            IProject freeDocuments = workspace.DM_FreeDocumentsProject();
            if (freeDocuments != null &&
                string.Equals(freeDocuments.DM_ProjectFullPath(), project.DM_ProjectFullPath(), StringComparison.OrdinalIgnoreCase))
                throw new UserException("The focused document is a free document. Focus a document of a project (.PrjPcb).");

            // Without the schematic server there is nothing to report: fail with a message before the busy cursor is on.
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a schematic sheet of the project first.");

            IClientAPI_Interface clientApi = DXP.GlobalVars.Client.GetClientAPI();
            clientApi?.BeginCursor(-11); // -11: the hourglass cursor (the BeginHourGlass default)
            List<string> report;
            string summary;
            try
            {
                (report, summary) = BuildReport(ctx, project, schServer, linkage);
            }
            finally
            {
                clientApi?.EndCursor();
            }

            string outPath = ctx.Get("Out");
            if (!string.IsNullOrEmpty(outPath))
            {
                if (Directory.Exists(outPath))
                    outPath = Path.Combine(outPath, Path.GetFileNameWithoutExtension(project.DM_ProjectFileName()) + ".BOM.txt");
                string outFolder = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!Directory.Exists(outFolder))
                    throw new UserException($"The folder of Out= does not exist: {outFolder}");
                File.WriteAllLines(outPath, report);
                summary += $" Written to {outPath}.";
            }
            report.Insert(0, summary);

            string text = string.Join(Environment.NewLine, report);
            AppDomain.CurrentDomain.SetData(LastReportKey, text);
            Ui.PanelContent.RefreshAll();

            if (ctx.IsInteractive)
            {
                // A report of a whole project is too long for the menu's message box: show it in the panel.
                SamplePanel.Show();
                return null;
            }
            return text;
        }

        private static (List<string> Report, string Summary) BuildReport(CommandContext ctx, IProject project, ISch_ServerInterface schServer, bool linkage)
        {
            var report = new List<string>();

            // The logical documents and their components are the project's compiled data model.
            string compileState = "up to date";
            if (project.DM_NeedsCompile())
                compileState = project.DM_Compile() ? "compiled" : "DM_Compile returned false";

            int sheets = 0, components = 0;
            var skipped = new List<string>();
            int docCount = project.DM_LogicalDocumentCount();
            for (int k = 0; k < docCount; k++)
            {
                IDocument document = project.DM_LogicalDocuments(k);
                if (document == null || document.DM_DocumentKind() != "SCH")
                    continue;

                // GetSchDocumentByPath finds only sheets the schematic server has loaded; the others are listed
                // as skipped.
                ISch_Document schDoc = schServer.GetSchDocumentByPath(document.DM_FullPath());
                if (schDoc == null)
                {
                    skipped.Add(document.DM_FileName());
                    continue;
                }

                ctx.Log("Reading " + document.DM_FileName());
                sheets++;
                report.Add("SCH file: " + document.DM_FileName());
                report.Add("Schematic Components Report:");
                report.Add("==============================");
                components += ReportSheet(schDoc, schServer, report, linkage);
                report.Add("");
            }

            foreach (string name in skipped)
                report.Add($"SCH file: {name}: not loaded in the schematic editor, skipped. Open the sheet and run the report again.");

            string summary = $"{Path.GetFileName(project.DM_ProjectFullPath())}: {components} component(s) on {sheets} schematic sheet(s)" +
                             $" (project {compileState}; {skipped.Count} sheet(s) skipped; linkage {(linkage ? "on" : "off")}).";
            return (report, summary);
        }

        /// <summary>Reports every component of one sheet and returns how many there were.</summary>
        private static int ReportSheet(ISch_Document schDoc, ISch_ServerInterface schServer, List<string> report, bool linkage)
        {
            int count = 0;
            ISch_Iterator iterator = schDoc.SchIterator_Create();
            if (iterator == null)
                return 0;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });
                for (var component = iterator.FirstSchObject() as ISch_Component; component != null; component = iterator.NextSchObject() as ISch_Component)
                {
                    count++;

                    // The footprint is the current implementation of model type PCBLIB.
                    string footprint = "";
                    foreach (ISch_Implementation implementation in Implementations(component))
                    {
                        if (implementation.GetState_ModelType() == "PCBLIB" && implementation.GetState_IsCurrent())
                            footprint = implementation.GetState_ModelName();
                    }

                    report.Add("Component Designator: " + (component.GetState_SchDesignator()?.GetState_Text() ?? "") + ", " +
                               "Footprint: " + footprint + ", " +
                               "Comment: " + (component.GetState_SchComment()?.GetState_Text() ?? "") + ", " +
                               "Description: " + component.GetState_ComponentDescription());

                    ReportParameters(component, report);
                    ReportPins(component, report);
                    if (linkage)
                        ReportLinkage(component, schServer, report);
                    report.Add("");
                }
            }
            finally
            {
                schDoc.SchIterator_Destroy(ref iterator);
            }
            return count;
        }

        /// <summary>The component's first-level child objects of kind eImplementation (its models).</summary>
        private static List<ISch_Implementation> Implementations(ISch_Component component)
        {
            var result = new List<ISch_Implementation>();
            ISch_Iterator iterator = component.SchIterator_Create();
            try
            {
                iterator.SetState_IterationDepth(TIterationDepth.eIterateFirstLevel);
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eImplementation });
                for (ISch_BasicContainer child = iterator.FirstSchObject(); child != null; child = iterator.NextSchObject())
                {
                    // A cast to ISch_Implementation succeeds for any object (docs: Interfaces at run time): check the ID.
                    if (child.GetState_ObjectId() == TObjectId.eImplementation)
                        result.Add((ISch_Implementation)child);
                }
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return result;
        }

        private static void ReportParameters(ISch_Component component, List<string> report, string indent = "")
        {
            bool found = false;
            ISch_Iterator iterator = component.SchIterator_Create();
            try
            {
                iterator.SetState_IterationDepth(TIterationDepth.eIterateFirstLevel);
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eParameter });
                for (var parameter = iterator.FirstSchObject() as ISch_Parameter; parameter != null; parameter = iterator.NextSchObject() as ISch_Parameter)
                {
                    if (!found)
                    {
                        report.Add("");
                        report.Add(indent + "    Parameters for this component:");
                        found = true;
                    }
                    report.Add(indent + "    Parameter Name: " + parameter.GetState_Name() + "     Parameter Value: " + parameter.GetState_Text());
                }
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            if (!found)
                report.Add(indent + "    There are no parameters for this component.");
        }

        private static void ReportPins(ISch_Component component, List<string> report)
        {
            bool found = false;
            ISch_Iterator iterator = component.SchIterator_Create();
            try
            {
                iterator.SetState_IterationDepth(TIterationDepth.eIterateFirstLevel);
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.ePin });
                for (var pin = iterator.FirstSchObject() as ISch_Pin; pin != null; pin = iterator.NextSchObject() as ISch_Pin)
                {
                    if (!found)
                    {
                        report.Add("");
                        report.Add("    Pins for this component:");
                        found = true;
                    }
                    // Location is in schematic internal coordinates, printed unconverted.
                    DXP.Point location = pin.GetState_Location();
                    report.Add($"    Pin {pin.GetState_Designator()} located at (x={location.X}, y={location.Y})");
                }
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            if (!found)
                report.Add("    There are no pins for this component.");
        }

        /// <summary>
        /// Where the component comes from. With a VaultGUID it is a workspace (vault) component: find its symbol and
        /// load the library copy to list its parameters. Otherwise ask the integrated library manager for the
        /// component in its source library and list that copy's parameters.
        /// </summary>
        private static void ReportLinkage(ISch_Component component, ISch_ServerInterface schServer, List<string> report)
        {
            report.Add("");
            string designItemId = component.GetState_DesignItemId();
            string libraryIdentifier = component.GetState_LibraryIdentifier();

            IIntegratedLibraryManager libraryManager = EDP.Utils.LoadIntegratedLibraryManager();
            if (libraryManager == null)
            {
                report.Add("    Linkage: the integrated library manager is not available.");
                return;
            }

            if (!string.IsNullOrEmpty(component.GetState_VaultGUID()))
            {
                report.Add("    Vault linkage for this component:");
                report.Add("    VaultGUID: " + component.GetState_VaultGUID() + ", " +
                           "ItemGUID: " + component.GetState_ItemGUID() + ", " +
                           "RevisionGUID: " + component.GetState_RevisionGUID() + ", " +
                           "DesignItemId: " + designItemId + ", " +
                           "LibReference: " + component.GetState_LibReference() + ", " +
                           "SourceLibraryName: " + component.GetState_SourceLibraryName());

                // IIntegratedLibraryManager has FindComponentSymbol, so no P/Invoke into LibraryInterface.dll
                // (its exported GetLibraryInterface) is needed.
                if (libraryManager.FindComponentSymbol(TLibIdentifierKind.eLibIdentifierKind_VaultName, libraryIdentifier, designItemId,
                        out string symbolLibraryPath, out string symbolReference))
                {
                    report.Add("    SymbolLibraryPath: " + symbolLibraryPath + "    SymbolReference: " + symbolReference);
                    ISch_Component libComponent = schServer.LoadComponent(TLibIdentifierKind.eLibIdentifierKind_VaultName, libraryIdentifier, designItemId);
                    if (libComponent == null)
                        report.Add("    Failed to load the component from the workspace library.");
                    else
                        ReportParameters(libComponent, report, "    ");
                }
                else
                {
                    report.Add("    Failed to find the component symbol in the workspace library.");
                }
                return;
            }

            report.Add("    Library linkage for this component:");
            report.Add("    DesignItemId: " + designItemId + ",     SourceLibraryName: " + component.GetState_SourceLibraryName());

            string foundInLibraryPath = "";
            string location = libraryManager.GetComponentLocation(component.GetState_SourceLibraryName(), designItemId, ref foundInLibraryPath);
            if (string.IsNullOrEmpty(location))
            {
                report.Add("        Failed to find component " + designItemId + " in the installed or project libraries.");
                return;
            }

            int componentCount = libraryManager.GetComponentCount(location);
            int index = 0;
            while (index < componentCount && libraryManager.GetComponentName(location, index) != designItemId)
                index++;
            if (index == componentCount)
            {
                report.Add("        Failed to find component " + designItemId + " in " + location);
                return;
            }

            int parameterCount = libraryManager.GetParameterCount(location, index);
            report.Add("        Parameters for this component in the source library (" + location + "):");
            report.Add("        Parameter count: " + parameterCount);
            for (int i = 0; i < parameterCount; i++)
            {
                report.Add("        Parameter Name: " + libraryManager.GetParameterName(location, index, i) +
                           "        Parameter Value: " + libraryManager.GetParameterValue(location, index, i));
            }
        }
    }
}
