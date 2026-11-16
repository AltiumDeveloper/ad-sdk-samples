using System;
using System.Collections.Generic;
using DXP; // the IClient.OpenDocument/ShowDocument extension methods live in namespace DXP
using EDP;
using SCH;

namespace AdSamples.FixPcbComponent
{
    /// <summary>One schematic sheet of the focused project, opened in AD.</summary>
    internal sealed class ProjectSheet
    {
        public string Path;
        public IServerDocument ServerDocument;
        public ISch_Document Sheet;
    }

    /// <summary>
    /// The schematic side shared by both commands: the SCH sheets of the focused project, the components of a sheet
    /// with their designators, and reading or writing a component parameter by name.
    /// </summary>
    internal static class ProjectSchematics
    {
        /// <summary>IDocument.DM_DocumentKind of a schematic sheet.</summary>
        private const string SchematicKind = "SCH";

        public static IProject FocusedProject()
        {
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            return workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a PCB project and focus one of its documents.");
        }

        /// <summary>
        /// Opens every SCH logical document of the project (IClient.OpenDocument) and returns its ISch_Document from
        /// ISch_ServerInterface.GetSchDocumentByPath, which only finds documents that are open. With
        /// <paramref name="show"/> each sheet is also shown (IClient.ShowDocument); this
        /// changes the active document.
        /// </summary>
        public static List<ProjectSheet> OpenSheets(CommandContext ctx, IProject project, bool show)
        {
            IClient client = DXP.GlobalVars.Client;
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer;
            var sheets = new List<ProjectSheet>();
            int count = project.DM_LogicalDocumentCount();
            for (int i = 0; i < count; i++)
            {
                IDocument document = project.DM_LogicalDocuments(i);
                if (document == null || !string.Equals(document.DM_DocumentKind(), SchematicKind, StringComparison.OrdinalIgnoreCase))
                    continue;

                string path = document.DM_FullPath();
                ctx.Log("Opening " + System.IO.Path.GetFileName(path));
                IServerDocument serverDocument = client.OpenDocument(SchematicKind, path);
                if (serverDocument != null && show)
                    client.ShowDocument(serverDocument);

                // The SCH server may only be running after the first sheet was opened, so it is fetched again here.
                schServer ??= SCH.GlobalVars.SchServer;
                ISch_Document sheet = schServer?.GetSchDocumentByPath(path);
                if (sheet == null)
                {
                    // Such sheets are reported, not skipped silently.
                    ctx.Log("  not available as a schematic document, skipped: " + path);
                    continue;
                }
                sheets.Add(new ProjectSheet { Path = path, ServerDocument = serverDocument, Sheet = sheet });
            }
            return sheets;
        }

        /// <summary>
        /// The components of a sheet with their designator text (ISch_Component.SchDesignator; the .NET API has no
        /// Designator property). A multi-part component is one ISch_Component per
        /// part, so a designator can appear more than once.
        /// </summary>
        public static List<(string Designator, ISch_Component Component)> Components(ISch_Document sheet)
        {
            var result = new List<(string, ISch_Component)>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on " + System.IO.Path.GetFileName(sheet.GetState_DocumentName()) + ".");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });
                for (var component = iterator.FirstSchObject() as ISch_Component;
                     component != null;
                     component = iterator.NextSchObject() as ISch_Component)
                {
                    string designator = component.GetState_SchDesignator()?.GetState_Text();
                    if (!string.IsNullOrEmpty(designator))
                        result.Add((designator.Trim(), component));
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return result;
        }

        /// <summary>
        /// The component's parameter with this name (case-insensitive), found by
        /// iterating the component's eParameter children. When nothing is found and the name is
        /// "Comment", the component's system Comment parameter (ISch_Component.SchComment) is used: Altium Designer
        /// returns it from the iterator, so the fallback is a safety net.
        /// </summary>
        public static ISch_Parameter FindParameter(ISch_Component component, string name)
        {
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator != null)
            {
                try
                {
                    iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eParameter });
                    for (var parameter = iterator.FirstSchObject() as ISch_Parameter;
                         parameter != null;
                         parameter = iterator.NextSchObject() as ISch_Parameter)
                    {
                        if (string.Equals(parameter.GetState_Name(), name, StringComparison.OrdinalIgnoreCase))
                            return parameter;
                    }
                }
                finally
                {
                    component.SchIterator_Destroy(ref iterator);
                }
            }

            if (string.Equals(name, "Comment", StringComparison.OrdinalIgnoreCase))
                return component.GetState_SchComment();
            return null;
        }

        /// <summary>
        /// Sets the parameter's text, creating it (hidden, at the component's location) when the
        /// component has none of that name. Call between SCHMBeginModify/SCHMEndModify of the component.
        /// Returns "created", "updated" or "unchanged".
        /// </summary>
        public static string SetParameter(ISch_ServerInterface schServer, ISch_RobotManager robot, ISch_Component component,
                                          string name, string value)
        {
            ISch_Parameter parameter = FindParameter(component, name);
            if (parameter != null)
            {
                if (parameter.GetState_Text() == value)
                    return "unchanged";
                robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                parameter.SetState_Text(value);
                robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                return "updated";
            }

            // Properties first, then add the parameter to its owner and register it with the robot manager
            // (docs/guides/schematic-basics.mdx, "Creating objects"; which robot message a new object needs is
            // not documented).
            parameter = schServer.SchObjectFactory(SCH.TObjectId.eParameter, SCH.TObjectCreationMode.eCreate_Default) as ISch_Parameter
                ?? throw new UserException("SchObjectFactory(eParameter) returned no parameter.");
            parameter.SetState_Name(name);
            parameter.SetState_Text(value);
            parameter.SetState_IsHidden(true);
            parameter.SetState_Location(component.GetState_Location());
            component.AddSchObject(parameter);
            robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, parameter);
            return "created";
        }
    }
}
