using System;
using System.Collections.Generic;
using DXP; // IClient.GetDXPWorkspace is an extension method in namespace DXP
using EDP;

namespace AdSamples.SheetSymbolChildSheets
{
    /// <summary>
    /// Lists the sheet symbols of the focused schematic sheet and the child sheets each one points to. Both come from
    /// the design model (EDP: IDocument, ISheetSymbol), not from the schematic primitives, so the project is compiled
    /// first to bring the model up to date.
    /// </summary>
    internal static class SheetSymbolChildSheetsCommands
    {
        /// <summary>
        /// ShowChildSheet "Compile=true|false": compiles the project of the focused schematic (default true), then
        /// returns one line per sheet symbol and one indented line per child sheet.
        /// </summary>
        [HotCommand("ShowChildSheet", "List the sheet symbols of the focused schematic and their child sheets")]
        public static string ShowChildSheet(CommandContext ctx)
        {
            bool compile = !string.Equals(ctx.Get("Compile", "true"), "false", StringComparison.OrdinalIgnoreCase);

            // GetDXPWorkspace returns IDXPWorkSpace; the EDP view of the same object is IWorkspace.
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");

            IDocument sheet = workspace.DM_FocusedDocument()
                ?? throw new UserException("No document is focused. Open and focus a schematic sheet first.");

            string kind = sheet.DM_DocumentKind();
            if (!string.Equals(kind, "SCH", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"{sheet.DM_FileName()} is not a schematic document (document kind '{kind}').");

            var lines = new List<string>();

            // A sheet that is not part of a project lives in the Free Documents project (IWorkspace reference). The
            // command says so and lists what the model returns, without compiling.
            // Open point: what DM_Project returns for a free document (null or the Free Documents project) is not
            // documented, so both are handled.
            IProject project = sheet.DM_Project();
            IProject freeDocuments = workspace.DM_FreeDocumentsProject();
            bool isFree = project == null ||
                          (freeDocuments != null &&
                           string.Equals(project.DM_ProjectFullPath(), freeDocuments.DM_ProjectFullPath(),
                                         StringComparison.OrdinalIgnoreCase));
            if (isFree)
                lines.Add($"Note: {sheet.DM_FileName()} is a free document (not part of a project); it was not compiled.");
            else if (compile)
            {
                // WorkspaceManager:Compile (Action=Compile|ObjectKind=Project) is not exposed under that name in .NET.
                // IProject.DM_Compile on the sheet's own project does the same job and returns a bool, which is
                // reported.
                ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                if (!project.DM_Compile())
                    lines.Add("Warning: DM_Compile returned false; the sheet symbol list may be out of date.");
            }

            // Keep the parent sheet in its own variable: reusing it for the child sheets would, after the first
            // child, enumerate the child's sheet symbols (with the parent's count).
            int symbolCount = sheet.DM_SheetSymbolCount();
            string header = $"{sheet.DM_FileName()}" +
                            (isFree ? "" : $" (project {project.DM_ProjectFileName()}" + (compile ? ", compiled" : ", not compiled") + ")");
            if (symbolCount == 0)
            {
                lines.Insert(0, header + ": this schematic document has no sheet symbol.");
                return string.Join(Environment.NewLine, lines);
            }

            int childTotal = 0, withoutChild = 0;
            var body = new List<string>();
            for (int i = 0; i < symbolCount; i++)
            {
                ISheetSymbol symbol = sheet.DM_SheetSymbols(i);
                if (symbol == null)
                {
                    body.Add($"Sheet symbol #{i}: (null)");
                    continue;
                }

                // DM_SheetSymbolFileName is the file name that links the symbol to its sheet (reference text);
                // DM_ChildSheet(j) returns the child sheets as IDocument. How the two differ for missing files or
                // repeated channels is not documented.
                body.Add($"Sheet symbol {symbol.DM_LogicalDesignator()}: {symbol.DM_SheetSymbolFileName()}");

                int childCount = symbol.DM_ChildSheetCount();
                if (childCount == 0)
                {
                    // Report it and go on with the next symbol.
                    withoutChild++;
                    body.Add("    (this sheet symbol has no child sheet)");
                    continue;
                }

                for (int j = 0; j < childCount; j++)
                {
                    IDocument child = symbol.DM_ChildSheet(j);
                    childTotal++;
                    body.Add("    Child sheet: " + (child == null ? "(null)" : child.DM_FileName()));
                }
            }

            lines.Insert(0, $"{header}: {symbolCount} sheet symbols, {childTotal} child sheets" +
                            (withoutChild > 0 ? $", {withoutChild} sheet symbols without a child sheet." : "."));
            lines.AddRange(body);
            return string.Join(Environment.NewLine, lines);
        }
    }
}
