using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.ViaHoleSizeCheck
{
    /// <summary>
    /// Checks one via against a design rule that exists only for the duration of the command: a hole size rule is
    /// created with PCBRuleFactory, added to the board, configured through IPCB_MaxMinHoleSizeConstraint, asked for a
    /// violation with ActualCheck, and removed again.
    /// </summary>
    internal static class ViaHoleSizeCheckCommands
    {
        private const string TempRuleName = "ViaHoleSizeCheck_Temp";

        /// <summary>
        /// CheckViaHoleSize "X=&lt;mil&gt;|Y=&lt;mil&gt;|MinMil=1|MaxMil=2|MinPercent=20|MaxPercent=80|Percent=false|AddViolation=true".
        /// The via is the one at X/Y (mils from the board origin), else the first selected via, else (menu only) the
        /// via the user clicks.
        /// </summary>
        [HotCommand("CheckViaHoleSize", "Check a via against a temporary hole size rule")]
        public static string CheckViaHoleSize(CommandContext ctx)
        {
            // The limits are parameters. Absolute limits are in mils here, internal units on the rule.
            double minMil = Number(ctx, "MinMil", 1);
            double maxMil = Number(ctx, "MaxMil", 2);
            double minPercent = Number(ctx, "MinPercent", 20);
            double maxPercent = Number(ctx, "MaxPercent", 80);
            bool absolute = !ctx.Flag("Percent");
            // By default the returned violation is added to the board and removed straight away.
            bool addViolation = !string.Equals(ctx.Get("AddViolation", "true"), "false", StringComparison.OrdinalIgnoreCase);

            IPCB_ServerInterface pcbServer = PcbServer();
            IPCB_Board board = CurrentBoard(pcbServer);
            IPCB_Via via = FindVia(ctx, board)
                ?? throw new UserException("No via chosen (the pick was cancelled).");

            // Read the via before the rule exists, so the report does not depend on the check.
            string viaText = DescribeVia(board, via);
            int hole = via.GetState_HoleSize();
            int size = via.GetState_Size();

            string limits = absolute
                ? string.Format(CultureInfo.InvariantCulture, "absolute {0} mil .. {1} mil", minMil, maxMil)
                : string.Format(CultureInfo.InvariantCulture, "{0}% .. {1}% of the via diameter", minPercent, maxPercent);

            string result;
            string ruleSummary = "";
            IPCB_Rule rule = null;
            bool ruleOnBoard = false;
            pcbServer.PreProcess();
            try
            {
                rule = pcbServer.PCBRuleFactory(TRuleKind.eRule_MaxMinHoleSize)
                    ?? throw new UserException("PCBRuleFactory returned no hole size rule.");
                // A new rule has an empty name; a recognisable one makes a leftover rule easy to find and delete.
                rule.SetState_Name(TempRuleName);
                board.AddPCBObject(rule);
                ruleOnBoard = true;

                // Type tests on SDK interfaces always succeed (they are all IDispatch), so the rule kind is what
                // makes this cast valid, not the cast itself.
                var constraint = (IPCB_MaxMinHoleSizeConstraint)rule;
                constraint.SetState_AbsoluteValues(absolute);
                constraint.SetState_MinLimit(EDP.Utils.MilsToCoord(minMil));
                constraint.SetState_MaxLimit(EDP.Utils.MilsToCoord(maxMil));
                constraint.SetState_MinPercent(minPercent);
                constraint.SetState_MaxPercent(maxPercent);
                ruleSummary = constraint.GetState_DataSummaryString();

                // A hole size rule applies to single objects, so the second object is null. ActualCheck returns
                // null when the via is within the limits, so the result is checked before it is used.
                IPCB_Violation violation = constraint.ActualCheck(via, null);
                if (violation == null)
                {
                    result = "no violation: the hole size is within the limits.";
                }
                else
                {
                    // Read the violation before it goes to the board and is removed again.
                    result = $"violation '{violation.GetState_Name()}': {violation.GetState_Description()}";
                    if (addViolation)
                    {
                        board.AddPCBObject(violation);
                        board.RemovePCBObject(violation);
                        result += " (added to the board and removed again)";
                    }
                }
            }
            finally
            {
                if (ruleOnBoard)
                    board.RemovePCBObject(rule);
                pcbServer.PostProcess();
            }

            return string.Join(Environment.NewLine,
                viaText,
                string.Format(CultureInfo.InvariantCulture, "Hole {0} of diameter {1} = {2:0.#}%.",
                    Mil(hole), Mil(size), size == 0 ? 0 : 100.0 * hole / size),
                $"Rule '{TempRuleName}' ({limits}; DataSummaryString '{ruleSummary}'): {result}",
                "The temporary rule was removed from the board.");
        }

        /// <summary>ListVias "Max=20": the vias of the board with location (mils from the board origin) and sizes.</summary>
        [HotCommand("ListVias", "List the vias of the board with location and hole size")]
        public static string ListVias(CommandContext ctx)
        {
            int max = (int)Number(ctx, "Max", 20);
            IPCB_Board board = CurrentBoard(PcbServer());
            var lines = new List<string>();
            int count = 0;
            foreach (IPCB_Via via in Vias(board))
            {
                count++;
                if (count <= max)
                    lines.Add("    " + DescribeVia(board, via));
            }
            lines.Insert(0, $"{Path.GetFileName(board.GetState_FileName())}: {count} vias" +
                            (count > max ? $", the first {max} listed:" : ":"));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// AddTestVia "X=&lt;mil&gt;|Y=&lt;mil&gt;|HoleMil=10|SizeMil=20": helper. Places a
        /// top-to-bottom via (default 500 mil inside the bottom left corner of the board outline) so the check has a
        /// via with a known hole size.
        /// </summary>
        [HotCommand("AddTestVia", "Place a test via (test setup)")]
        public static string AddTestVia(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PcbServer();
            IPCB_Board board = CurrentBoard(pcbServer);
            int x, y;
            if (ctx.Get("X") != null || ctx.Get("Y") != null)
            {
                x = board.GetState_XOrigin() + EDP.Utils.MilsToCoord(Number(ctx, "X", 0));
                y = board.GetState_YOrigin() + EDP.Utils.MilsToCoord(Number(ctx, "Y", 0));
            }
            else
            {
                CoordRect outline = board.GetState_BoardOutline().BoundingRectangle();
                x = outline.Left + EDP.Utils.MilsToCoord(500);
                y = outline.Bottom + EDP.Utils.MilsToCoord(500);
            }

            IPCB_Via via;
            pcbServer.PreProcess();
            try
            {
                via = pcbServer.PCBObjectFactory(TObjectId.eViaObject, TDimensionKind.eNoDimension,
                          TObjectCreationMode.eCreate_Default) as IPCB_Via
                      ?? throw new UserException("PCBObjectFactory returned no via.");
                via.SetState_XLocation(x);
                via.SetState_YLocation(y);
                via.SetState_Size(EDP.Utils.MilsToCoord(Number(ctx, "SizeMil", 20)));
                via.SetState_HoleSize(EDP.Utils.MilsToCoord(Number(ctx, "HoleMil", 10)));
                via.SetState_LowLayer(new V7_Layer(TV6_Layer.eV6_TopLayer));
                via.SetState_HighLayer(new V7_Layer(TV6_Layer.eV6_BottomLayer));
                board.AddPCBObject(via);
            }
            finally
            {
                pcbServer.PostProcess();
            }
            board.GraphicalView_ZoomRedraw();

            return "Placed " + DescribeVia(board, via) + ". Close the document without saving to discard it.";
        }

        /// <summary>The via to check: X/Y, else the first selected via, else a click (interactive only).</summary>
        private static IPCB_Via FindVia(CommandContext ctx, IPCB_Board board)
        {
            if (ctx.Get("X") != null || ctx.Get("Y") != null)
            {
                int x = board.GetState_XOrigin() + EDP.Utils.MilsToCoord(Number(ctx, "X", 0));
                int y = board.GetState_YOrigin() + EDP.Utils.MilsToCoord(Number(ctx, "Y", 0));
                return ViaAt(board, x, y)
                    ?? throw new UserException(string.Format(CultureInfo.InvariantCulture,
                        "No via at X={0}, Y={1} mil (from the board origin). ListVias shows where the vias are.",
                        ctx.Get("X", "0"), ctx.Get("Y", "0")));
            }

            for (int i = 0; i < board.GetState_SelectecObjectCount(); i++)
            {
                IPCB_Primitive selected = board.GetState_SelectecObject(i);
                if (selected != null && selected.GetState_ObjectID() == TObjectId.eViaObject)
                    return (IPCB_Via)selected;
            }

            // ChooseLocation and the ambiguity dialog of GetObjectAtXYAskUserIfAmbiguous wait for the user: on the
            // pipe nobody would answer and AD would hang, so they are used only from the menu.
            if (!ctx.IsInteractive)
                throw new UserException("Select a via, or pass its location: X=<mil>|Y=<mil> (from the board origin).");

            int px = 0, py = 0;
            if (!board.ChooseLocation(ref px, ref py, "Choose a via"))
                return null; // Escape
            IPCB_Primitive picked = board.GetObjectAtXYAskUserIfAmbiguous(px, py,
                new PCB.TObjectSet { TObjectId.eViaObject }, PCBConstant.V6AllLayersSet, TEditingAction.eEditAction_Select);
            if (picked == null || picked.GetState_ObjectID() != TObjectId.eViaObject)
                throw new UserException("There is no via at the chosen location.");
            return (IPCB_Via)picked;
        }

        /// <summary>The via whose pad contains the point, nearest centre first; no dialog, so it works on the pipe.</summary>
        private static IPCB_Via ViaAt(IPCB_Board board, int x, int y)
        {
            IPCB_Via best = null;
            double bestDistance = double.MaxValue;
            foreach (IPCB_Via via in Vias(board))
            {
                double dx = via.GetState_XLocation() - (double)x, dy = via.GetState_YLocation() - (double)y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= via.GetState_Size() / 2.0 && distance < bestDistance)
                {
                    best = via;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>All vias of the board. The list is filled before it is returned, so the iterator is always destroyed.</summary>
        private static List<IPCB_Via> Vias(IPCB_Board board)
        {
            var vias = new List<IPCB_Via>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eViaObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive obj = iterator.FirstPCBObject(); obj != null; obj = iterator.NextPCBObject())
                {
                    if (obj.GetState_ObjectID() == TObjectId.eViaObject)
                        vias.Add((IPCB_Via)obj);
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return vias;
        }

        private static string DescribeVia(IPCB_Board board, IPCB_Via via)
        {
            IPCB_Net net = via.GetState_Net();
            return string.Format(CultureInfo.InvariantCulture, "Via at X={0}, Y={1} mil, hole {2}, diameter {3}, net {4}",
                Math.Round(EDP.Utils.CoordToMils(via.GetState_XLocation() - board.GetState_XOrigin()), 3),
                Math.Round(EDP.Utils.CoordToMils(via.GetState_YLocation() - board.GetState_YOrigin()), 3),
                Mil(via.GetState_HoleSize()), Mil(via.GetState_Size()),
                net == null ? "(none)" : net.GetState_Name());
        }

        /// <summary>Internal units (10,000 per mil) as "x mil (raw)", so the unit can be checked.</summary>
        private static string Mil(int coord) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###} mil ({1})", EDP.Utils.CoordToMils(coord), coord);

        private static double Number(CommandContext ctx, string name, double defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new UserException($"{name}={text} is not a number.");
            return value;
        }

        private static IPCB_ServerInterface PcbServer() =>
            PCB.GlobalVars.PCBServer ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");

        private static IPCB_Board CurrentBoard(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetCurrentPCBBoard()
            ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
    }
}
