using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.SchComponentHitTest
{
    /// <summary>
    /// Asks for a click on the current schematic sheet, hit-tests that point and lists the parameters of the
    /// component found there. The SDK lessons: ISch_Document.ChooseLocationInteractively returns a clicked point,
    /// ISch_Document.CreateHitTest lists the objects under it, and a child iterator on ISch_Component filtered to
    /// eParameter walks the component's parameters.
    /// </summary>
    internal static class SchComponentHitTestCommands
    {
        /// <summary>
        /// CheckComponentProperty ["At=&lt;x,y&gt;|Export=true|false"]. Without At it prompts for one
        /// click on the sheet; that waits for a human, so it only runs from the menu. At=x,y (mils) hit-tests that
        /// point without a prompt and works on the pipe. Export=false leaves out the raw ExportToParameters text.
        /// </summary>
        [HotCommand("CheckComponentProperty", "Click a component and list its parameters")]
        public static string CheckComponentProperty(CommandContext ctx)
        {
            bool export = ParseBool("Export", ctx.Get("Export", "true"));
            string at = ctx.Get("At");

            ISch_Document sheet = CurrentSheet();
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            DXP.Point location;
            if (!string.IsNullOrWhiteSpace(at))
            {
                location = ParsePoint(at);
            }
            else
            {
                if (!ctx.IsInteractive)
                    throw new UserException(
                        "Without At=x,y this command waits for a click in the schematic editor, which would block " +
                        "Altium Designer on the pipe. Pass At=x,y (mils), or run it from the menu.");

                // The point must be constructed before the call: it is passed by reference and filled in.
                location = new DXP.Point(new SPoint());
                // Returns false when the user cancels (Escape): then there is no chosen location
                // to hit-test.
                if (!sheet.ChooseLocationInteractively(ref location, "Choose a location to click"))
                    return $"{file}: no location chosen (cancelled).";
            }

            string where = $"({Mils(location.X)}, {Mils(location.Y)})";
            ISch_HitTest hitTest = sheet.CreateHitTest(THitTestMode.eHitTest_AllObjects, location);
            int count = hitTest?.GetState_HitTestCount() ?? 0;
            if (count < 1)
                return $"{file}: nothing at {where}.";

            // Scan the whole hit list for the first component: it need not be the first hit object, so stopping
            // after the first hit object whatever its type would miss it.
            var ids = new List<string>();
            ISch_Component component = null;
            for (int i = 0; i < count; i++)
            {
                ISch_GraphicalObject prim = hitTest.GetState_HitObject(i);
                TObjectId id = prim.GetState_ObjectId();
                ids.Add(id.ToString());
                // Identify the object by ObjectId: a C# type test would not help, because the SDK's COM objects
                // satisfy every interface cast at compile time.
                if (component == null && id == TObjectId.eSchComponent)
                    component = (ISch_Component)prim;
            }
            string hits = string.Join(", ", ids);

            if (component == null)
                return $"{file}: no component at {where} (hit objects: {hits}).";

            List<(string Name, string Text, string Export)> parameters = Parameters(component);

            var text = new StringBuilder();
            text.AppendLine($"{file}: component {Designator(component)} at {where} (hit objects: {hits}), " +
                            $"{parameters.Count} parameter(s):");
            foreach (var p in parameters)
            {
                text.AppendLine($"  {p.Name} = '{p.Text}'");
                if (export)
                    text.AppendLine("    ExportToParameters: " + p.Export);
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// ListComponents ["Max=&lt;n&gt;"]: test helper. Lists each component's designator
        /// and bounding rectangle in mils, and the centre of the rectangle as an At=x,y value for
        /// CheckComponentProperty.
        /// </summary>
        [HotCommand("ListComponents", "List the components on the current sheet (test helper)")]
        public static string ListComponents(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max"), 50);
            ISch_Document sheet = CurrentSheet();
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            var components = new List<ISch_Component>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    components.Add((ISch_Component)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }

            if (components.Count == 0)
                return $"{file}: no components.";

            var text = new StringBuilder();
            text.Append($"{file}: {components.Count} component(s)");
            text.AppendLine(components.Count > max ? $", first {max} shown:" : ":");
            foreach (ISch_Component c in components.Take(max))
            {
                CoordRect r = c.BoundingRectangle();
                text.AppendLine($"  {Designator(c)} bounds=({Mils(r.Left)}, {Mils(r.Bottom)})-({Mils(r.Right)}, {Mils(r.Top)}) " +
                                $"At={Mils((r.Left + r.Right) / 2)},{Mils((r.Bottom + r.Top) / 2)}");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// The component's parameters, through a child iterator filtered to eParameter.
        /// </summary>
        private static List<(string Name, string Text, string Export)> Parameters(ISch_Component component)
        {
            var list = new List<(string, string, string)>();
            ISch_Iterator iterator = component.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the component.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eParameter });
                // FirstSchObject returns ISch_BasicContainer; the filter guarantees parameters, so cast.
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                {
                    var parameter = (ISch_Parameter)obj;
                    list.Add((parameter.GetState_Name(), parameter.GetState_Text(), parameter.ExportToParameters()));
                }
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return list;
        }

        private static string Designator(ISch_Component component)
        {
            string text = component.GetState_SchDesignator()?.GetState_Text();
            return string.IsNullOrEmpty(text) ? "(no designator)" : text;
        }

        // EDP.Utils.CoordToMils assumes 10,000 internal units per mil. This holds for schematic
        // coordinates as well.
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

        private static ISch_Document CurrentSheet()
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
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
