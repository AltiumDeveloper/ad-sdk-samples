using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AdSamples.RunScript
{
    /// <summary>
    /// Runs a procedure of an existing script (DelphiScript and the other scripting languages of Altium Designer)
    /// from an extension. The SDK lesson: an extension does not need a scripting API; it launches the built-in
    /// process ScriptingSystem:RunScript with DXP.Utils.RunCommand, passing the script and the procedure as a
    /// "Key=Value|Key=Value" parameter string.
    /// </summary>
    internal static class RunScriptCommands
    {
        // The process id and its parameter names (ProjectName, FileName, ProcName).
        // The SDK reference does not document them.
        private const string RunScriptProcess = "ScriptingSystem:RunScript";

        private const string Usage =
            "Pass the script and the procedure: ProjectName=<path to a .PrjScr>|ProcName=<procedure> " +
            "or FileName=<path to a script file of a script project>|ProcName=<procedure>.";

        // File and procedure names WriteDemoScript uses.
        private const string DemoProjectFile = "RunScriptDemo.PrjScr";
        private const string DemoScriptFile = "RunScriptDemo.pas";
        private const string DemoMarkerFile = "RunScriptDemo.txt";
        private const string DemoProcName = "RunScriptDemo";

        /// <summary>
        /// RunScript "ProjectName=&lt;.PrjScr&gt;|ProcName=&lt;procedure&gt;" or "FileName=&lt;script&gt;|ProcName=&lt;procedure&gt;".
        /// Both ProjectName and FileName may be given: the project form runs first, then the file form.
        /// The script's own code decides what it changes.
        /// </summary>
        /// <remarks>
        /// When the script engine
        /// cannot find the script or procedure, it shows a modal "Error" box ("Program cannot find script ...")
        /// inside RunCommand. RunCommand does not return, and raises no exception, until someone clicks OK, so
        /// the command and Altium Designer stay blocked. This code cannot detect that case beforehand.
        /// </remarks>
        [HotCommand("RunScript",
            "Run a procedure of an existing script. A script or procedure AD cannot find shows a modal error box that blocks AD until OK is clicked")]
        public static string RunScript(CommandContext ctx)
        {
            string procName = ctx.Get("ProcName");
            if (string.IsNullOrWhiteSpace(procName))
                throw new UserException("No ProcName specified. " + Usage);
            procName = procName.Trim();
            // A procedure name is an identifier. Checking it here also keeps '|' and '=' out of the parameter string.
            if (!Regex.IsMatch(procName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new UserException($"ProcName '{procName}' is not a valid procedure name.");

            string project = ExistingFile(ctx, "ProjectName");
            string file = ExistingFile(ctx, "FileName");
            if (project == null && file == null)
                throw new UserException("No script specified. " + Usage);

            // One parameter string per form: the script location, then the procedure.
            var runs = new List<(string Label, string Parameters)>();
            if (project != null)
                runs.Add((Path.GetFileName(project), $"ProjectName={project}|ProcName={procName}"));
            if (file != null)
                runs.Add((Path.GetFileName(file), $"FileName={file}|ProcName={procName}"));

            var result = new StringBuilder();
            foreach (var (label, parameters) in runs)
            {
                ctx.Log($"Running {RunScriptProcess} {parameters}");
                // RunCommand returns nothing, so a failure is not reported back here. A script
                // the engine cannot find makes it show a modal error box, and this call only returns after the
                // box is closed. Only an exception from the call itself would reach this code.
                DXP.Utils.RunCommand(RunScriptProcess, parameters);
                result.AppendLine($"Ran {RunScriptProcess} for {procName} in '{label}'.");
            }

            result.Append("RunCommand reports no result: check the script's own output to see whether it ran.");
            return result.ToString();
        }

        /// <summary>
        /// WriteDemoScript "Out=&lt;folder&gt;": writes RunScriptDemo.pas, a DelphiScript with one procedure
        /// (RunScriptDemo) written to save RunScriptDemo.txt next to it, and RunScriptDemo.PrjScr, a minimal script
        /// project that lists the .pas. It deletes an old RunScriptDemo.txt first. AD accepts the minimal
        /// project and runs the procedure.
        /// </summary>
        [HotCommand("WriteDemoScript",
            "Write a demo script project (RunScriptDemo.PrjScr, RunScriptDemo.pas); deletes an old RunScriptDemo.txt")]
        public static string WriteDemoScript(CommandContext ctx)
        {
            string folder = ctx.Get("Out", Path.GetTempPath());
            if (!Directory.Exists(folder))
                throw new UserException($"Out folder '{folder}' does not exist.");
            folder = Path.GetFullPath(folder);

            string projectPath = Path.Combine(folder, DemoProjectFile);
            string scriptPath = Path.Combine(folder, DemoScriptFile);
            string markerPath = Path.Combine(folder, DemoMarkerFile);
            if (File.Exists(markerPath))
                File.Delete(markerPath);

            // DelphiScript string literals are in single quotes; a quote inside is doubled.
            string markerLiteral = markerPath.Replace("'", "''");
            string script =
                "// Written by the RunScript sample (WriteDemoScript). It only writes one text file.\r\n" +
                $"procedure {DemoProcName};\r\n" +
                "var\r\n" +
                "  Lines : TStringList;\r\n" +
                "begin\r\n" +
                "  Lines := TStringList.Create;\r\n" +
                "  try\r\n" +
                $"    Lines.Add('{DemoProcName} ran');\r\n" +
                $"    Lines.SaveToFile('{markerLiteral}');\r\n" +
                "  finally\r\n" +
                "    Lines.Free;\r\n" +
                "  end;\r\n" +
                "end;\r\n";
            // UTF-8 without a byte order mark. How the script engine reads non-ASCII text (a user name in the
            // temp path, for example) is not known.
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            // A script project saved by Altium Designer is an INI-style file in UTF-8 with a byte order mark: a
            // [Design] section starting with Version=1.0, and one [Document<n>] section per script with
            // DocumentPath relative to the project. This keeps only
            // those keys and drops the PCB-oriented defaults AD also writes. AD accepts it.
            string project =
                "[Design]\r\n" +
                "Version=1.0\r\n" +
                "\r\n" +
                "[Document1]\r\n" +
                $"DocumentPath={DemoScriptFile}\r\n";
            File.WriteAllText(projectPath, project, new UTF8Encoding(true));

            return $"Wrote {projectPath} and {scriptPath}.{Environment.NewLine}" +
                   $"Try: RunScript \"ProjectName={projectPath}|ProcName={DemoProcName}\"{Environment.NewLine}" +
                   $"The procedure is written to save {markerPath} (deleted now) when it runs; " +
                   "if AD cannot find the script it shows a modal error box that blocks AD until OK is clicked.";
        }

        /// <summary>The full path of an existing file from parameter <paramref name="name"/>, or null when it is not given.</summary>
        private static string ExistingFile(CommandContext ctx, string name)
        {
            string path = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(path))
                return null;
            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(path))
                throw new UserException($"{name}: file not found: {path}");
            return path;
        }
    }
}
