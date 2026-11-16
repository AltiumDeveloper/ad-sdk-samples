using System;
using System.Collections.Generic;
using System.Globalization;
using PCB;

namespace AdSamples.ClearanceConstraint
{
    /// <summary>
    /// Finds a clearance rule by name and changes the clearance it requires between arcs and tracks
    /// (a per-pair value of IPCB_ClearanceConstraint), then reads it back.
    /// </summary>
    internal static class ClearanceConstraintCommands
    {
        /// <summary>
        /// ChangeClearance "Rule=&lt;name&gt;|Factor=&lt;number&gt;": multiplies the rule's Arc-to-Track clearance by
        /// the factor. Defaults: rule "Clearance", factor 3.
        /// </summary>
        [HotCommand("ChangeClearance", "Multiply the Arc-to-Track clearance of a clearance rule")]
        public static string ChangeClearance(CommandContext ctx)
        {
            string ruleName = ctx.Get("Rule", "Clearance");
            string factorText = ctx.Get("Factor", "3");
            if (!double.TryParse(factorText, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor)
                || factor <= 0 || double.IsInfinity(factor))
                throw new UserException($"Factor must be a positive number (got '{factorText}').");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            IPCB_ClearanceConstraint rule = FindClearanceRule(board, ruleName, out List<string> clearanceRules)
                ?? throw new UserException(
                    $"No clearance rule named '{ruleName}' on this board. Clearance rules: " +
                    (clearanceRules.Count > 0 ? string.Join(", ", clearanceRules) : "none") + ".");

            // The pair is given by object kind (TObjectClearanceId), not by object; the order of the two kinds
            // does not matter. A pair without a value of its own reads as the rule's Gap.
            const TObjectClearanceId kind1 = TObjectClearanceId.eObjectClearanceID_Arc;
            const TObjectClearanceId kind2 = TObjectClearanceId.eObjectClearanceID_Track;

            int before = rule.GetClearance_1(kind1, kind2);
            int gapBefore = rule.GetState_Gap();
            TClearanceConstraintMode modeBefore = rule.GetState_Mode();

            double scaled = Math.Round(before * factor);
            if (scaled > int.MaxValue)
                throw new UserException($"{Metric(before)} x {factorText} is out of range.");
            int requested = (int)scaled;

            // A rule is a PCB object like any other: bracket the change so that the board records it (undo,
            // document modified) and refreshes its views.
            pcbServer.PreProcess();
            try
            {
                rule.BeginModify();
                rule.SetClearance_1(kind1, kind2, requested);
                rule.EndModify();
            }
            finally
            {
                pcbServer.PostProcess();
            }

            int after = rule.GetClearance_1(kind1, kind2);
            int gapAfter = rule.GetState_Gap();
            TClearanceConstraintMode modeAfter = rule.GetState_Mode();

            // Assigning a pair value can also change Gap (the most common pair value becomes the gap), so Gap and
            // Mode are reported before and after.
            return $"Rule '{rule.GetState_Name()}': Arc-Track clearance {Metric(before)} -> {Metric(after)} " +
                   $"(x{factor.ToString(CultureInfo.InvariantCulture)}, requested {Metric(requested)}). " +
                   $"Gap {Metric(gapBefore)} -> {Metric(gapAfter)}. Mode {ModeName(modeBefore)} -> {ModeName(modeAfter)}. " +
                   "The change is in memory; run DRC to see its effect and save the document to keep it.";
        }

        /// <summary>
        /// Walks the board's rules (eRuleObject) and returns the clearance rule with the given name
        /// (case-insensitive), or null. Collects the names of all clearance rules.
        /// </summary>
        private static IPCB_ClearanceConstraint FindClearanceRule(IPCB_Board board, string name,
            out List<string> clearanceRules)
        {
            clearanceRules = new List<string>();
            IPCB_ClearanceConstraint found = null;

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.eRuleObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (IPCB_Primitive obj = iterator.FirstPCBObject(); obj != null; obj = iterator.NextPCBObject())
                {
                    var rule = (IPCB_Rule)obj;
                    // Type tests succeed for every SDK interface (they are all IDispatch), so branch on the rule
                    // kind before treating the rule as an IPCB_ClearanceConstraint.
                    if (rule.GetState_RuleKind() != TRuleKind.eRule_Clearance)
                        continue;

                    string ruleName = rule.GetState_Name();
                    clearanceRules.Add(ruleName);
                    if (found == null && string.Equals(ruleName, name, StringComparison.OrdinalIgnoreCase))
                        found = (IPCB_ClearanceConstraint)rule;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            return found;
        }

        private static string Metric(int coord) => EDP.Utils.CoordUnitToString(coord, TUnit.eMetric);

        private static string ModeName(TClearanceConstraintMode mode) =>
            mode == TClearanceConstraintMode.eClearanceConstraintMode_SingleClearance ? "SingleClearance" : "ObjectsClearance";
    }
}
