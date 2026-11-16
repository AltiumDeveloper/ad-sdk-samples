using System;
using System.Globalization;
using System.IO;
using SCH;

namespace AdSamples.FindComponent
{
    /// <summary>
    /// Finds a component on the current schematic sheet by its designator text with a filtered ISch_Iterator, then
    /// moves the view onto it by launching the schematic server's Zoom process (SCH:Zoom) with DXP.Utils.RunCommand.
    /// </summary>
    internal static class FindComponentCommands
    {
        /// <summary>The schematic server process launched to centre the view on a rectangle.</summary>
        private const string ZoomProcess = "SCH:Zoom";

        /// <summary>FindComponent ["Designator=&lt;text&gt;|Zoom=true|false"]: finds the first match and zooms to it.</summary>
        [HotCommand("FindComponent", "Find a component by designator and zoom to it")]
        public static string FindComponent(CommandContext ctx)
        {
            // "C4" is the default.
            string designator = ctx.Get("Designator", "C4");
            if (string.IsNullOrEmpty(designator))
                throw new UserException("Designator must not be empty.");
            bool zoom = ParseBool("Zoom", ctx.Get("Zoom", "true"));

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            ISch_Component found = null;
            int visited = 0;
            ISch_Iterator iterator = sheet.SchIterator_Create();
            try
            {
                // Only components: SCH.TObjectSet is the .NET form of the C++ ObjectSet.insert(eSchComponent).
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });

                // FirstSchObject/NextSchObject return ISch_BasicContainer; the filter makes the cast safe.
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                {
                    visited++;
                    var component = (ISch_Component)obj;
                    // A component without a designator object is skipped.
                    ISch_Designator schDesignator = component.GetState_SchDesignator();
                    if (schDesignator != null && string.Equals(schDesignator.GetState_Text(), designator, StringComparison.Ordinal))
                    {
                        found = component; // first match wins
                        break;
                    }
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }

            if (found == null)
                return $"{file}: no component with designator '{designator}' among {visited} component(s).";

            // BoundingRectangle_Full covers the component with its designator, comment and parameters.
            SCH.CoordRect rect = found.BoundingRectangle_Full();
            int centreX = (rect.X1 + rect.X2) / 2;
            int centreY = (rect.Y1 + rect.Y2) / 2;
            int width = rect.X2 - rect.X1;
            int height = rect.Y2 - rect.Y1;

            string where =
                $"{file}: found '{designator}', bounding rectangle ({Mils(rect.X1)}, {Mils(rect.Y1)}) - " +
                $"({Mils(rect.X2)}, {Mils(rect.Y2)}) mil [internal {rect.X1}, {rect.Y1}, {rect.X2}, {rect.Y2}]";
            if (!zoom)
                return where + ". Zoom=false: view not changed.";

            // The SDK docs do not describe SCH:Zoom's parameters. The values are the internal coordinates,
            // unchanged. Invariant culture keeps them
            // plain integers whatever the user's locale.
            string parameters = string.Format(CultureInfo.InvariantCulture,
                "Object=Point|Location.X={0}|Location.Y={1}|Width={2}|Height={3}", centreX, centreY, width, height);
            // RunCommand returns nothing, so the result can only say what was asked for.
            DXP.Utils.RunCommand(ZoomProcess, parameters);
            return where + $". Ran {ZoomProcess} '{parameters}'.";
        }

        /// <summary>
        /// Converts with EDP.Utils.CoordToMils (10,000 internal units per mil, documented for PCB). That schematic
        /// coordinates use the same factor is not stated in the SDK docs: the internal values are reported too.
        /// </summary>
        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.##", CultureInfo.InvariantCulture);

        private static bool ParseBool(string name, string value)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Length == 0)
                return true;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0")
                return false;
            throw new UserException($"{name} must be true or false, got '{value}'.");
        }
    }
}
