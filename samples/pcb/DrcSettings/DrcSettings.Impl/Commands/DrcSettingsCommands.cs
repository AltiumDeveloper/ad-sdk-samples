using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCB;

namespace AdSamples.DrcSettings
{
    /// <summary>
    /// Design rule check settings of the current board. The .NET IPCB_Board has no accessor for the board's
    /// IPCB_DesignRuleCheckerOptions, so this sample shows the DRC settings that are reachable: the online DRC switch
    /// in the PCB system options, the DRCEnabled flag of each rule, and a batch DRC run.
    /// </summary>
    internal static class DrcSettingsCommands
    {
        [HotCommand("ReportDrcSettings", "Report the online DRC switch and the board's design rules")]
        public static string ReportDrcSettings(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PcbServer();
            IPCB_Board board = CurrentBoard(pcbServer);

            // DoOnlineDRC is a PCB Editor preference (IPCB_ServerInterface.SystemOptions), not a property of the board.
            bool online = SystemOptions(pcbServer).GetState_DoOnlineDRC();

            var kinds = new SortedDictionary<string, (int Enabled, int Disabled)>(StringComparer.Ordinal);
            int enabled = 0, disabled = 0;
            ForEach<IPCB_Rule>(board, TObjectId.eRuleObject, rule =>
            {
                // DRCEnabled reads false also when the rule's rule set is disabled (reference: IPCB_Rule.DRCEnabled).
                bool on = rule.GetState_DRCEnabled();
                string kind = rule.GetState_RuleKind().ToString();
                kinds.TryGetValue(kind, out var counts);
                kinds[kind] = on ? (counts.Enabled + 1, counts.Disabled) : (counts.Enabled, counts.Disabled + 1);
                if (on) enabled++; else disabled++;
            });

            int violations = CountViolations(board);

            var lines = new List<string>
            {
                $"{BoardName(board)}: online DRC {(online ? "on" : "off")}, {enabled + disabled} rules " +
                $"({enabled} enabled, {disabled} disabled), {violations} violations on the board."
            };
            lines.AddRange(kinds.Select(k => $"    {k.Key}: {k.Value.Enabled} enabled, {k.Value.Disabled} disabled"));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// EnableOnlineAndBatchCheck "Online=true|false": switches online DRC on or off. Despite the name,
        /// it does not touch the batch check, which has no switch in the reachable API.
        /// </summary>
        [HotCommand("EnableOnlineAndBatchCheck", "Turn online DRC on or off")]
        public static string EnableOnlineAndBatchCheck(CommandContext ctx)
        {
            string value = ctx.Get("Online");
            if (!bool.TryParse(value, out bool online))
                throw new UserException("Pass Online=true or Online=false. This changes a PCB Editor preference for every document.");

            IPCB_ServerInterface pcbServer = PcbServer();
            CurrentBoard(pcbServer); // no board is an explicit error
            IPCB_SystemOptions options = SystemOptions(pcbServer);

            bool previous = options.GetState_DoOnlineDRC();
            options.SetState_DoOnlineDRC(online);
            bool now = options.GetState_DoOnlineDRC();

            // Export_ToIniFile is not called: the stored preferences are not rewritten by this sample.
            return $"Online DRC changed from {OnOff(previous)} to {OnOff(now)}. " +
                   $"This is a PCB Editor preference, not board state: run Online={previous.ToString().ToLowerInvariant()} to restore it.";
        }

        /// <summary>
        /// RunBatchDrc "Out=&lt;folder or file&gt;|Format=Html|Text": runs the batch design rule check with the board's
        /// rules and writes the report. The report is not opened (argDisplayReportFile false) and not published.
        /// </summary>
        [HotCommand("RunBatchDrc", "Run a batch DRC and write the report file")]
        public static string RunBatchDrc(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PcbServer();
            IPCB_Board board = CurrentBoard(pcbServer);

            string format = ctx.Get("Format", "Html");
            TDRCReportFileFormat reportFormat;
            string extension;
            if (string.Equals(format, "Html", StringComparison.OrdinalIgnoreCase))
                (reportFormat, extension) = (TDRCReportFileFormat.eDRC_HTML, ".html");
            else if (string.Equals(format, "Text", StringComparison.OrdinalIgnoreCase))
                (reportFormat, extension) = (TDRCReportFileFormat.eDRC_Text, ".txt");
            else
                throw new UserException($"Unknown Format '{format}'. Use Format=Html or Format=Text.");

            string path = ReportPath(ctx.Get("Out", Path.GetTempPath()), BoardName(board), extension);
            DateTime started = DateTime.Now;
            int before = CountViolations(board);

            ctx.Log($"Running batch DRC on {BoardName(board)}...");
            bool result = board.RunBatchDesignRuleCheck(path, reportFormat, false, false);

            int after = CountViolations(board);
            bool written = File.Exists(path) && File.GetLastWriteTime(path) >= started.AddSeconds(-2);
            return $"RunBatchDesignRuleCheck returned {result}. Violations on the board: {before} before, {after} after. " +
                   (written ? $"Report written to {path}" : $"No report file at {path}");
        }

        /// <summary>Returns the report file path: Out is a folder (file name derived from the board) or a file.</summary>
        private static string ReportPath(string outPath, string boardName, string extension)
        {
            string fileName = "DrcSettings_" + Path.GetFileNameWithoutExtension(boardName) + extension;
            string path = Directory.Exists(outPath) ? Path.Combine(outPath, fileName) : outPath;
            path = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder))
                throw new UserException($"The folder '{folder}' does not exist.");
            return path;
        }

        private static int CountViolations(IPCB_Board board)
        {
            int count = 0;
            ForEach<IPCB_Violation>(board, TObjectId.eViolationObject, _ => count++);
            return count;
        }

        /// <summary>Visits the board objects of one kind on all layers; the iterator is always destroyed.</summary>
        private static void ForEach<T>(IPCB_Board board, TObjectId objectId, Action<T> visit) where T : class
        {
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { objectId });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive obj = iterator.FirstPCBObject(); obj != null; obj = iterator.NextPCBObject())
                {
                    // The object set filter yields only objects of this kind, so the cast is to the matching interface.
                    visit((T)(object)obj);
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
        }

        private static string OnOff(bool value) => value ? "on" : "off";

        private static string BoardName(IPCB_Board board) => Path.GetFileName(board.GetState_FileName());

        private static IPCB_SystemOptions SystemOptions(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetState_SystemOptions()
            ?? throw new UserException("The PCB system options are not available.");

        private static IPCB_ServerInterface PcbServer() =>
            PCB.GlobalVars.PCBServer
            ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");

        private static IPCB_Board CurrentBoard(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetCurrentPCBBoard()
            ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
    }
}
