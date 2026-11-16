using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB; // also brings the IPCB_Component helper extensions GetState_Name / GetState_Comment into scope

namespace AdSamples.PcbComponentTextRotation
{
    /// <summary>
    /// Sets the rotation of a component's child text object (designator or comment) on every component of the
    /// current board. IPCB_Component.Name and IPCB_Component.Comment are read-only: they return the IPCB_Text object,
    /// whose Rotation (from IPCB_RectangularPrimitive) is then changed.
    /// </summary>
    internal static class PcbComponentTextRotationCommands
    {
        /// <summary>
        /// RotateComponentText "Angle=&lt;degrees&gt;|Target=Name|Comment": sets the rotation of the chosen text of every
        /// component (default: the designator, Name, to 90 degrees).
        /// </summary>
        [HotCommand("RotateComponentText", "Rotate every component's designator (or comment) text to an angle")]
        public static string RotateComponentText(CommandContext ctx)
        {
            double angle = ParseAngle(ctx.Get("Angle", "90"));
            bool comment = ParseTarget(ctx.Get("Target", "Name"));
            string targetName = comment ? "Comment" : "Name";

            IPCB_Board board = CurrentBoard();
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;

            int components = 0, changed = 0, withoutText = 0;
            string first = null;

            pcbServer.PreProcess();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    components++;
                    IPCB_Text text = comment ? component.GetState_Comment() : component.GetState_Name();
                    if (text == null)
                    {
                        withoutText++;
                        continue;
                    }

                    double before = text.GetState_Rotation();

                    // The change is bracketed on the component, not on the text object: the text is a child of the
                    // component. Whether that alone records the change for undo is not known (see README Notes).
                    component.BeginModify();
                    text.SetState_Rotation(angle);
                    component.GraphicallyInvalidate();
                    component.EndModify();

                    double after = text.GetState_Rotation();
                    if (after != before)
                        changed++;
                    if (first == null)
                        first = $"{Designator(component)}: {targetName}.Rotation {Deg(before)} -> {Deg(after)}";

                    if (components % 200 == 0)
                        ctx.Log($"{components} components processed");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
                pcbServer.PostProcess();
            }

            string boardName = Path.GetFileName(board.GetState_FileName());
            if (components == 0)
                return $"{boardName}: no components.";

            string result = $"{boardName}: {targetName}.Rotation set to {Deg(angle)} on {components - withoutText} of " +
                            $"{components} components, {changed} changed. First: {first}.";
            if (withoutText > 0)
                result += $" {withoutText} components have no {targetName} text object.";
            return result + " Close without saving (or undo) to discard.";
        }

        /// <summary>
        /// GetComponentTextRotation "Max=&lt;n&gt;": read-back command. Lists, per component,
        /// the component's rotation and the rotation and autoposition of its designator and comment text.
        /// </summary>
        [HotCommand("GetComponentTextRotation", "List the rotation of component designator and comment text")]
        public static string GetComponentTextRotation(CommandContext ctx)
        {
            if (!int.TryParse(ctx.Get("Max", "20"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 0)
                throw new UserException("Max must be a whole number >= 0 (0 lists all components).");

            IPCB_Board board = CurrentBoard();
            var lines = new List<string>();
            int components = 0;
            var nameAngles = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var commentAngles = new SortedDictionary<string, int>(StringComparer.Ordinal);

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    components++;
                    IPCB_Text name = component.GetState_Name();
                    IPCB_Text comment = component.GetState_Comment();
                    Count(nameAngles, name);
                    Count(commentAngles, comment);

                    if (max == 0 || components <= max)
                    {
                        lines.Add($"    {Designator(component)} ({component.GetState_Layer()}, rotation {Deg(component.GetState_Rotation())}): " +
                                  $"Name {TextInfo(name)} autopos {component.GetState_NameAutoPos()}, " +
                                  $"Comment {TextInfo(comment)} autopos {component.GetState_CommentAutoPos()}");
                    }
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            string boardName = Path.GetFileName(board.GetState_FileName());
            lines.Insert(0, $"{boardName}: {components} components. Name.Rotation: {Histogram(nameAngles)}. " +
                            $"Comment.Rotation: {Histogram(commentAngles)}.");
            if (max > 0 && components > max)
                lines.Add($"    ... {components - max} more (Max=0 lists all).");
            return string.Join(Environment.NewLine, lines);
        }

        private static double ParseAngle(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle) ||
                double.IsNaN(angle) || double.IsInfinity(angle))
                throw new UserException($"Angle '{value}' is not a number of degrees (for example Angle=90).");
            return angle;
        }

        /// <summary>True for Target=Comment, false for Target=Name.</summary>
        private static bool ParseTarget(string value)
        {
            if (string.Equals(value, "Name", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(value, "Comment", StringComparison.OrdinalIgnoreCase))
                return true;
            throw new UserException($"Target '{value}' is not valid: use Target=Name (designator) or Target=Comment.");
        }

        private static void Count(SortedDictionary<string, int> histogram, IPCB_Text text)
        {
            string key = text == null ? "none" : Deg(text.GetState_Rotation());
            histogram[key] = histogram.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        private static string Histogram(SortedDictionary<string, int> histogram)
        {
            var parts = new List<string>();
            foreach (var pair in histogram)
                parts.Add($"{pair.Key} x{pair.Value}");
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }

        private static string TextInfo(IPCB_Text text) =>
            text == null ? "(none)" : $"'{text.GetState_Text()}' {Deg(text.GetState_Rotation())}";

        private static string Designator(IPCB_Component component)
        {
            string designator = component.GetState_SourceDesignator();
            return string.IsNullOrEmpty(designator) ? "(no designator)" : designator;
        }

        private static string Deg(double degrees) => degrees.ToString("0.###", CultureInfo.InvariantCulture) + " deg";

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
