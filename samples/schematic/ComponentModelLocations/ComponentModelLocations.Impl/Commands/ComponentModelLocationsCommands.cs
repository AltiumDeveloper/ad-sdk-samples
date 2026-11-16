using System;
using System.Collections.Generic;
using System.IO;
using DXP; // IClient.GetDXPWorkspace and the OpenDocument/ShowDocument extension methods live in namespace DXP
using EDP;
using SCH; // IIntegratedLibraryManager lives in namespace SCH, although EDP.Utils hands it out

namespace AdSamples.ComponentModelLocations
{
    /// <summary>
    /// For every part of the focused schematic sheet, asks the integrated library manager where the part's symbol
    /// and each of its model data files (footprints, 3D, simulation models) come from. Parts and implementations are
    /// read from the design model (EDP.IPart, EDP.IComponentImplementation), so the project is compiled first.
    /// </summary>
    internal static class ComponentModelLocationsCommands
    {
        /// <summary>
        /// PartLibraryReport "Out=&lt;file or folder&gt;|Open=true|Compile=false": returns the report; Out also writes it
        /// to a file, Open shows the file in Altium Designer.
        /// </summary>
        [HotCommand("PartLibraryReport", "Report library and model file locations of the parts on the focused sheet")]
        public static string PartLibraryReport(CommandContext ctx)
        {
            // Compile is on unless explicitly turned off (ctx.Flag would treat a missing parameter as false).
            bool compile = !string.Equals(ctx.Get("Compile", "true"), "false", StringComparison.OrdinalIgnoreCase);

            IWorkspace workspace = Workspace();
            IDocument sheet = FocusedSchematic(workspace);

            string compileNote = null;
            IProject project = sheet.DM_Project();
            if (!compile)
            {
                compileNote = "not compiled (Compile=false)";
            }
            else if (project == null)
            {
                compileNote = "not compiled (the sheet has no project)";
            }
            else
            {
                // IProject.DM_Compile compiles the sheet's own project from .NET and returns a bool, which is
                // reported.
                ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                compileNote = project.DM_Compile()
                    ? "compiled"
                    : "DM_Compile returned false, the parts may be out of date";

                // Query the focused document again after the compile instead of reusing the object read before it.
                sheet = FocusedSchematic(workspace);
            }

            int partCount = sheet.DM_PartCount();
            if (partCount == 0)
                throw new UserException($"{sheet.DM_FileName()} has no parts in the design model ({compileNote}). " +
                                        "Place components, or open the sheet inside its project and compile it.");

            // Loaded once per command, not for every part.
            IIntegratedLibraryManager libraries = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new UserException("The integrated library manager is not available (EDP.Utils.LoadIntegratedLibraryManager returned null).");

            var report = new List<string>();
            var totals = new Totals();

            for (int i = 0; i < partCount; i++)
            {
                IPart part = sheet.DM_Parts(i);
                if (part == null)
                    continue;
                ctx.Log($"Part {i + 1}/{partCount}");
                totals.Parts++;
                ReportPart(report, libraries, part, totals);
            }

            string header = $"{sheet.DM_FileName()} ({compileNote}): {totals.Parts} parts, {totals.Implementations} implementations, " +
                            $"{totals.Datafiles} data files ({totals.DatafilesResolved} located, {totals.Datafiles - totals.DatafilesResolved} not located).";
            report.Insert(0, header);
            report.Insert(1, "");

            string text = string.Join(Environment.NewLine, report);
            string written = WriteReport(ctx, sheet.DM_FileName(), report);
            return written == null ? text : text + Environment.NewLine + "Report written to " + written;
        }

        /// <summary>Appends the lines of one part: its library location, then each implementation (model).</summary>
        private static void ReportPart(List<string> report, IIntegratedLibraryManager libraries, IPart part, Totals totals)
        {
            string libraryReference = part.DM_LibraryReference();

            // The return value is the path of the symbol inside its library. For a component of an integrated library
            // it can be a path inside the .IntLib; argFoundInLibraryPath then names the top-level library.
            string componentTopLevel = "";
            string componentLocation = libraries.GetComponentLocation(part.DM_SourceLibraryName(), libraryReference, ref componentTopLevel);

            report.Add("Designator:         " + part.DM_LogicalDesignator());
            report.Add("  Part type:          " + part.DM_PartType());
            report.Add("  Library reference:  " + libraryReference);
            report.Add("  Source library:     " + part.DM_SourceLibraryName());
            report.Add("  Component location: " + Show(componentLocation));
            report.Add("  Top level location: " + Show(componentTopLevel));

            int implementationCount = part.DM_ImplementationCount();
            if (implementationCount == 0)
                report.Add("  No implementations (models)");

            for (int j = 0; j < implementationCount; j++)
            {
                IComponentImplementation model = part.DM_Implementations(j);
                if (model == null)
                    continue;
                totals.Implementations++;

                string modelName = model.DM_ModelName();
                string modelType = model.DM_ModelType();
                bool integrated = model.DM_IntegratedModel();
                report.Add($"  Model {j + 1}: {modelName} ({modelType}{(integrated ? ", integrated" : "")})");
                report.Add("    Description: " + model.DM_Description());

                int parameterCount = model.DM_ParameterCount();
                if (parameterCount == 0)
                    report.Add("    No parameters for this implementation");
                for (int k = 0; k < parameterCount; k++)
                {
                    IParameter parameter = model.DM_Parameters(k);
                    if (parameter != null)
                        report.Add($"    Parameter: {parameter.DM_Name()} = {parameter.DM_Value()} ({parameter.DM_ObjectKindString()})");
                }

                int datafileCount = model.DM_DatafileCount();
                if (datafileCount == 0)
                    report.Add("    No data files");
                for (int k = 0; k < datafileCount; k++)
                {
                    totals.Datafiles++;

                    // A fresh output variable per call: with one shared variable, a call that does not set it would
                    // print the previous call's value.
                    string foundIn = "";
                    string location;
                    string entity = model.DM_DatafileEntity(k);
                    string kind = model.DM_DatafileKind(k);
                    if (integrated)
                    {
                        // The component location is passed as argComponentLibraryName.
                        location = libraries.GetComponentDatafileLocation(k, modelName, modelType, libraryReference,
                                                                          componentLocation, ref foundIn);
                    }
                    else
                    {
                        // argForComponentInstance = true (the part is a placed component instance).
                        location = libraries.FindDatafileInStandardLibs(entity, kind, model.DM_DatafileLocation(k),
                                                                        true, ref foundIn);
                    }

                    if (!string.IsNullOrEmpty(location) || !string.IsNullOrEmpty(foundIn))
                        totals.DatafilesResolved++;

                    report.Add($"    Data file {k + 1}: {entity} ({kind}), via " +
                               (integrated ? "GetComponentDatafileLocation" : "FindDatafileInStandardLibs"));
                    report.Add("      Data file location: " + Show(location));
                    report.Add("      Top level location: " + Show(foundIn));
                }
            }
            report.Add("");
        }

        /// <summary>Writes the report when Out or Open is given; returns the path, or null.</summary>
        private static string WriteReport(CommandContext ctx, string sheetName, List<string> report)
        {
            string path = ctx.Get("Out");
            bool open = ctx.Flag("Open");
            if (string.IsNullOrEmpty(path) && !open)
                return null;

            string fileName = "ComponentModelLocations_" + Path.GetFileNameWithoutExtension(sheetName) + ".txt";
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), fileName);
            else if (Directory.Exists(path))
                path = Path.Combine(path, fileName);
            path = Path.GetFullPath(path);
            File.WriteAllLines(path, report);

            if (open)
            {
                IClient client = DXP.GlobalVars.Client;
                IServerDocument reportDocument = client.OpenDocument("Text", path);
                if (reportDocument != null)
                    client.ShowDocument(reportDocument);
            }
            return path;
        }

        private static string Show(string value) => string.IsNullOrEmpty(value) ? "<empty>" : value;

        private static IDocument FocusedSchematic(IWorkspace workspace)
        {
            IDocument document = workspace.DM_FocusedDocument()
                ?? throw new UserException("No document is focused. Open and focus a .SchDoc first.");
            string kind = document.DM_DocumentKind();
            if (!string.Equals(kind, "SCH", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"{document.DM_FileName()} is not a schematic document (document kind '{kind}'). Focus a .SchDoc first.");
            return document;
        }

        // GetDXPWorkspace returns IDXPWorkSpace; the EDP view of the same object is IWorkspace.
        private static IWorkspace Workspace() =>
            DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");

        private sealed class Totals
        {
            public int Parts, Implementations, Datafiles, DatafilesResolved;
        }
    }
}
