using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DXP; // the IClient.OpenDocument/ShowDocument extension methods live in namespace DXP
using SCH; // IIntegratedLibraryManager lives in namespace SCH, although EDP.Utils hands it out

namespace AdSamples.InstalledLibraryReport
{
    /// <summary>
    /// Walks the installed libraries (the Installed tab of the Available Libraries dialog) through
    /// SCH.IIntegratedLibraryManager: every library, its components and each component's parameters.
    /// The manager is index-based and keyed by the library path: there are no library or component objects.
    /// </summary>
    internal static class InstalledLibraryReportCommands
    {
        private const string ReportFileName = "InstalledLibraries.txt";

        /// <summary>
        /// InstalledLibraryReport "Out=&lt;folder or file&gt;|Open=true|MaxComponents=&lt;n&gt;": writes the full report to a
        /// text file and returns a per-library summary with the file path. Open shows the file in Altium Designer
        /// (menu only). MaxComponents limits the components written per library (0 = all).
        /// </summary>
        [HotCommand("InstalledLibraryReport", "Report installed libraries, components and parameters")]
        public static string InstalledLibraryReport(CommandContext ctx)
        {
            int maxComponents = ParseMax(ctx.Get("MaxComponents", "0"));
            string path = ReportPath(ctx.Get("Out"));

            IIntegratedLibraryManager manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new UserException("The integrated library manager is not available (EDP.Utils.LoadIntegratedLibraryManager returned null).");

            var report = new List<string>();
            var summary = new StringBuilder();

            int libraryCount = manager.InstalledLibraryCount();
            report.Add("Installed Library Count : " + libraryCount);
            int totalComponents = 0, totalParameters = 0;

            // Indexes run from 0 to Count - 1 (the docs' InstalledLibraryPath example loops this way). Components and
            // parameters are addressed by (library path, component index[, parameter index]).
            for (int i = 0; i < libraryCount; i++)
            {
                string libraryPath = manager.InstalledLibraryPath(i);
                ctx.Log($"Library {i + 1}/{libraryCount}: {libraryPath}");

                int componentCount = manager.GetComponentCount(libraryPath);
                int listed = maxComponents > 0 ? Math.Min(componentCount, maxComponents) : componentCount;
                report.Add("=============================================");
                report.Add("Installed Library Path : " + libraryPath);
                report.Add("Component Count : " + componentCount);

                int libraryParameters = 0;
                for (int j = 0; j < listed; j++)
                {
                    int parameterCount = manager.GetParameterCount(libraryPath, j);
                    libraryParameters += parameterCount;
                    report.Add("    ------------------------------------");
                    report.Add("    Component Name : " + manager.GetComponentName(libraryPath, j));
                    report.Add("    Parameter Count : " + parameterCount);
                    for (int k = 0; k < parameterCount; k++)
                        report.Add($"    {manager.GetParameterName(libraryPath, j, k)} = {manager.GetParameterValue(libraryPath, j, k)}");
                }
                if (listed < componentCount)
                    report.Add($"    ... {componentCount - listed} more components not listed (MaxComponents={maxComponents})");

                totalComponents += componentCount;
                totalParameters += libraryParameters;
                summary.AppendLine($"  [{i}] {libraryPath}: {componentCount} components" +
                                   (listed < componentCount ? $" ({listed} listed)" : "") +
                                   $", {libraryParameters} parameters");
            }

            File.WriteAllLines(path, report);

            var result = new StringBuilder();
            result.AppendLine($"Installed libraries: {libraryCount}, components: {totalComponents}, parameters listed: {totalParameters}");
            result.Append(summary);
            result.AppendLine("Report written to " + path);

            if (ctx.Flag("Open"))
            {
                // Opening a document changes the workspace, so it is only done from the menu, never on the pipe.
                if (ctx.IsInteractive)
                {
                    DXP.IClient client = DXP.GlobalVars.Client;
                    DXP.IServerDocument document = client.OpenDocument("Text", path);
                    if (document != null)
                        client.ShowDocument(document);
                    else
                        result.AppendLine("Could not open the report in Altium Designer.");
                }
                else
                {
                    result.AppendLine("Open skipped (opening a document is done from the menu only).");
                }
            }

            return result.ToString().TrimEnd();
        }

        /// <summary>Out may be a folder (the report is InstalledLibraries.txt in it) or a file; default: the temp folder.</summary>
        private static string ReportPath(string outValue)
        {
            string path = string.IsNullOrWhiteSpace(outValue) ? Path.GetTempPath() : outValue;
            if (Directory.Exists(path))
                path = Path.Combine(path, ReportFileName);
            path = Path.GetFullPath(path);
            // Checked before the (slow) library walk, so a typo in Out fails fast with a readable message.
            string folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                throw new UserException($"The folder for Out does not exist: '{folder}'. Give an existing folder or a file in one.");
            return path;
        }

        private static int ParseMax(string text)
        {
            if (!int.TryParse(text, out int max) || max < 0)
                throw new UserException($"MaxComponents must be a non-negative whole number, got '{text}'.");
            return max;
        }
    }
}
