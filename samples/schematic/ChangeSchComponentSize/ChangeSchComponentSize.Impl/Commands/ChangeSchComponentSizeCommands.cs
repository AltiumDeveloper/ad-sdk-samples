using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.ChangeSchComponentSize
{
    /// <summary>
    /// Lets the user click schematic components and calls ISch_GraphicalObject.SetState_xSizeySize on each one.
    /// The SDK lessons: ISch_Document.ChooseLocationInteractively returns a clicked point, ISch_Document.CreateHitTest
    /// lists the objects under a point, and ISch_BasicContainer.ExportToParameters dumps an object's properties as
    /// a parameter string.
    /// </summary>
    internal static class ChangeSchComponentSizeCommands
    {
        /// <summary>
        /// ChangeComponentSize ["Mode=Pick|Selected|At=&lt;x,y&gt;|Max=&lt;n&gt;|Export=true|false"].
        /// Mode=Pick (default): prompts for clicks until Escape, a click on empty sheet, or Max picks. It waits
        /// for a human, so it only runs from the menu. Mode=Selected handles the selected components without a
        /// prompt. At=x,y (mils) runs one hit test at that point without a prompt. Both work on the pipe.
        /// </summary>
        [HotCommand("ChangeComponentSize", "Pick components and recalculate their size")]
        public static string ChangeComponentSize(CommandContext ctx)
        {
            bool export = ParseBool("Export", ctx.Get("Export", "true"));
            string at = ctx.Get("At");
            string mode = ctx.Get("Mode", "Pick");

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            if (!string.IsNullOrWhiteSpace(at))
            {
                // Unattended path: the point is given, only the hit test runs.
                DXP.Point point = ParsePoint(at);
                ISch_Component component = ComponentAt(sheet, point, out string hits);
                if (component == null)
                    return $"{file}: no component at {at} (hit objects: {hits}). Nothing changed.";
                var results = Resize(schServer, sheet, new[] { component }, ctx);
                return Report(file, results, export, $"hit test at {at} (hit objects: {hits})");
            }

            if (mode.Equals("Selected", StringComparison.OrdinalIgnoreCase))
            {
                List<ISch_Component> selected = SheetComponents(sheet).Where(c => c.GetState_Selection()).ToList();
                if (selected.Count == 0)
                    return $"{file}: no component is selected. Nothing changed.";
                return Report(file, Resize(schServer, sheet, selected, ctx), export, "selected components");
            }

            if (!mode.Equals("Pick", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Mode must be Pick or Selected, got '{mode}'.");

            if (!ctx.IsInteractive)
                throw new UserException(
                    "Mode=Pick waits for clicks in the schematic editor, which would block Altium Designer on the pipe. " +
                    "Use Mode=Selected, or At=x,y (mils) to test one point.");

            int max = ParseMax(ctx.Get("Max"), int.MaxValue);
            var all = new List<Resized>();
            string stopReason = $"stopped after {max} pick(s)";
            int picks = 0;
            while (picks < max)
            {
                var location = new DXP.Point(new SPoint());
                // Returns false when the user presses Escape. Ignoring the result would hit-test a stale location
                // and might never leave the loop.
                if (!sheet.ChooseLocationInteractively(ref location, "Choose a component (click empty sheet or Esc to finish)"))
                {
                    stopReason = "no location chosen (Escape)";
                    break;
                }
                picks++;

                ISch_HitTest hitTest = sheet.CreateHitTest(THitTestMode.eHitTest_AllObjects, location);
                if (hitTest == null || hitTest.GetState_HitTestCount() < 1)
                {
                    // Exit rule: a click that hits nothing ends the loop.
                    stopReason = "clicked an empty spot";
                    break;
                }

                ISch_Component component = FirstComponent(hitTest, out string hits);
                if (component == null)
                {
                    // A click on something that is not a component is ignored.
                    ctx.Log($"No component at ({Mils(location.X)}, {Mils(location.Y)}): {hits}");
                    continue;
                }

                List<Resized> one = Resize(schServer, sheet, new[] { component }, ctx);
                all.AddRange(one);
                ctx.Log(one[0].Summary);
            }

            return Report(file, all, export, stopReason);
        }

        /// <summary>
        /// ListComponents ["Max=&lt;n&gt;"]: helper. Lists each component's designator,
        /// selection, location and bounding rectangle in mils, and the centre of the rectangle as an At=x,y value
        /// that ChangeComponentSize accepts.
        /// </summary>
        [HotCommand("ListComponents", "List the components on the current sheet (test helper)")]
        public static string ListComponents(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max"), 50);
            ISch_Document sheet = CurrentSheet(SchServer());
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            List<ISch_Component> components = SheetComponents(sheet);
            if (components.Count == 0)
                return $"{file}: no components.";

            var text = new StringBuilder();
            text.Append($"{file}: {components.Count} component(s)");
            text.AppendLine(components.Count > max ? $", first {max} shown:" : ":");
            foreach (ISch_Component c in components.Take(max))
            {
                CoordRect r = c.BoundingRectangle();
                DXP.Point location = c.GetState_Location();
                text.AppendLine($"  {Designator(c)} selected={c.GetState_Selection()} " +
                                $"location=({Mils(location.X)}, {Mils(location.Y)}) bounds={Rect(r)} " +
                                $"At={Mils((r.Left + r.Right) / 2)},{Mils((r.Bottom + r.Top) / 2)}");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// SelectComponents "Designators=&lt;d1,d2,...&gt;" or "Designators=" (clear): helper.
        /// Selects exactly the listed components, so ChangeComponentSize Mode=Selected can run unattended.
        /// </summary>
        [HotCommand("SelectComponents", "Select components by designator (test helper)")]
        public static string SelectComponents(CommandContext ctx)
        {
            string list = ctx.Get("Designators");
            if (list == null)
                throw new UserException("Pass Designators=<d1,d2,...>, or Designators= to clear the selection.");
            var wanted = new HashSet<string>(
                list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            ISch_Document sheet = CurrentSheet(SchServer());
            var selected = new List<string>();
            foreach (ISch_Component c in SheetComponents(sheet))
            {
                bool select = wanted.Contains(Designator(c));
                if (c.GetState_Selection() != select)
                {
                    c.SetState_Selection(select);
                    c.GraphicallyInvalidate();
                }
                if (select)
                    selected.Add(Designator(c));
            }

            string[] missing = wanted.Except(selected, StringComparer.OrdinalIgnoreCase).ToArray();
            string result = $"{selected.Count} component(s) selected" +
                            (selected.Count > 0 ? ": " + string.Join(", ", selected) : "") + ".";
            return missing.Length == 0 ? result : result + " Not found: " + string.Join(", ", missing) + ".";
        }

        /// <summary>One component before and after SetState_xSizeySize.</summary>
        private sealed record Resized(string Designator, string Before, string After, string Parameters)
        {
            public string Summary => $"{Designator}: bounds {Before} -> {After}" + (Before == After ? " (unchanged)" : "");
        }

        /// <summary>
        /// Calls SetState_xSizeySize on each component inside the schematic change bracket: SCHMBeginModify/
        /// SCHMEndModify per object (docs: Working with schematics, "Changing objects"), and PreProcess/PostProcess
        /// on the sheet as the docs example ISch_RobotManager.SendMessage does.
        /// </summary>
        private static List<Resized> Resize(ISch_ServerInterface schServer, ISch_Document sheet,
                                            IEnumerable<ISch_Component> components, CommandContext ctx)
        {
            ISch_RobotManager robot = schServer.GetState_RobotManager();
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            var results = new List<Resized>();
            processControl.PreProcess(sheet, "");
            try
            {
                foreach (ISch_Component component in components)
                {
                    string parameters = component.ExportToParameters();
                    string before = Rect(component.BoundingRectangle());

                    robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                    component.SetState_xSizeySize();
                    robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                    component.GraphicallyInvalidate();

                    results.Add(new Resized(Designator(component), before, Rect(component.BoundingRectangle()), parameters));
                    if (results.Count % 100 == 0)
                        ctx.Log($"{results.Count} components processed...");
                }
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }
            return results;
        }

        private static string Report(string file, List<Resized> results, bool export, string how)
        {
            if (results.Count == 0)
                return $"{file}: no component resized ({how}).";
            var text = new StringBuilder();
            text.AppendLine($"{file}: SetState_xSizeySize called on {results.Count} component(s) ({how}):");
            foreach (Resized r in results)
            {
                text.AppendLine("  " + r.Summary);
                if (export)
                    text.AppendLine("    ExportToParameters: " + r.Parameters);
            }
            text.Append("The change is in memory only: save the document to keep it, or undo it.");
            return text.ToString();
        }

        /// <summary>The first component under the point, or null; <paramref name="hits"/> lists all hit objects.</summary>
        private static ISch_Component ComponentAt(ISch_Document sheet, DXP.Point point, out string hits)
        {
            ISch_HitTest hitTest = sheet.CreateHitTest(THitTestMode.eHitTest_AllObjects, point);
            if (hitTest == null || hitTest.GetState_HitTestCount() < 1)
            {
                hits = "none";
                return null;
            }
            return FirstComponent(hitTest, out hits);
        }

        private static ISch_Component FirstComponent(ISch_HitTest hitTest, out string hits)
        {
            var ids = new List<string>();
            ISch_Component found = null;
            for (int i = 0; i < hitTest.GetState_HitTestCount(); i++)
            {
                ISch_GraphicalObject prim = hitTest.GetState_HitObject(i);
                TObjectId id = prim.GetState_ObjectId();
                ids.Add(id.ToString());
                // Identify the component by ObjectId: a C# type test would not help (the
                // SDK's COM objects satisfy every interface cast at compile time).
                if (found == null && id == TObjectId.eSchComponent)
                    found = (ISch_Component)prim;
            }
            hits = ids.Count == 0 ? "none" : string.Join(", ", ids);
            return found;
        }

        private static List<ISch_Component> SheetComponents(ISch_Document sheet)
        {
            var list = new List<ISch_Component>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    list.Add((ISch_Component)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return list;
        }

        private static string Designator(ISch_Component component)
        {
            string text = component.GetState_SchDesignator()?.GetState_Text();
            return string.IsNullOrEmpty(text) ? "(no designator)" : text;
        }

        private static string Rect(CoordRect r) =>
            $"({Mils(r.Left)}, {Mils(r.Bottom)})-({Mils(r.Right)}, {Mils(r.Top)})";

        // EDP.Utils.CoordToMils assumes 10,000 internal units per mil. The docs' API conventions name it for
        // coordinates in general, with PCB examples only; it fits schematic coordinates too (README
        // Notes).
        // Invariant culture so the pipe output doesn't depend on the Windows locale.
        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>"x,y" in mils to a sheet point in internal units.</summary>
        private static DXP.Point ParsePoint(string text)
        {
            string[] parts = text.Split(',');
            if (parts.Length != 2 ||
                !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double xMil) ||
                !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double yMil))
                throw new UserException($"At must be 'x,y' in mils, for example At=1500,2300 (got '{text}').");
            return new DXP.Point(new SPoint { X = EDP.Utils.MilsToCoord(xMil), Y = EDP.Utils.MilsToCoord(yMil) });
        }

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer
            ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            // A schematic library is an ISch_Document too; tell them apart by the object ID
            // (docs/guides/schematic-basics.mdx "The current document").
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static bool ParseBool(string name, string value)
        {
            if (value.Length == 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1")
                return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0")
                return false;
            throw new UserException($"{name} must be true or false, got '{value}'.");
        }

        private static int ParseMax(string text, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive number (got '{text}').");
            return max;
        }
    }
}
