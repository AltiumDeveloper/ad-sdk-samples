using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.ModifySchWireColor
{
    /// <summary>
    /// Recolours every wire of the current schematic sheet. The SDK lesson: a change to a schematic object is
    /// announced to the editor with ISch_RobotManager.SendMessage (SCHMBeginModify before, SCHMEndModify after),
    /// so that undo and the other editor sub-systems see it (docs: Working with schematics, "Changing objects").
    /// </summary>
    internal static class ModifySchWireColorCommands
    {
        // A shift such as 0x0000C8 << (n * 8) gives red, green, blue for the first three wires, then a value with
        // the high byte set (C# masks the shift count to 5 bits, so n = 4 starts over). The sample cycles through the
        // three valid values instead. Colours are BGR: 0x0000C8 is red (docs: ISch_GraphicalObject.Color).
        private const string DefaultColors = "0000C8,00C800,C80000";

        /// <summary>
        /// ModifySchWireColor ["Colors=&lt;BGR hex,...&gt;|Property=Color|UnderlineColor|Zoom=true|false"]:
        /// gives the wires of the current sheet the listed colours in turn and returns how many were changed.
        /// </summary>
        [HotCommand("ModifySchWireColor", "Give every wire on the current sheet its own colour")]
        public static string ModifySchWireColor(CommandContext ctx)
        {
            int[] colors = ParseColors(ctx.Get("Colors", DefaultColors));
            bool underline = ParseProperty(ctx.Get("Property", "Color"));
            // Zoom to the whole document at the end for menu clicks; on the pipe only on
            // request, so an automated run doesn't change the user's view.
            bool zoom = ParseBool("Zoom", ctx.Get("Zoom", ctx.IsInteractive ? "true" : "false"));

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            ISch_RobotManager robot = schServer.GetState_RobotManager();

            // PreProcess/PostProcess bracket the whole loop, as the docs example ISch_RobotManager.SendMessage
            // does. A run is not guaranteed to be exactly one undo step (README Notes).
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            var changed = new List<string>();
            // Check the iterator for null before adding the filter, and destroy it in the finally block.
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            processControl.PreProcess(sheet, "");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eWire });
                int n = 0;
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject(), n++)
                {
                    // The filter guarantees wires; a type test would not help (it succeeds for every SDK object).
                    var wire = (ISch_Wire)obj;
                    int color = colors[n % colors.Length];

                    // Source = the object being changed, destination = BroadCast.
                    robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                    if (underline)
                        wire.SetState_UnderlineColor(color); // through ISch_WireHelper
                    else
                        wire.SetState_Color(color);          // the wire's line colour (the guide's choice)
                    robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);

                    // Redraw the wire now; without it the canvas shows the old colour until the next repaint.
                    wire.GraphicallyInvalidate();
                    changed.Add(Describe(n, wire));
                    if ((n + 1) % 500 == 0)
                        ctx.Log($"{n + 1} wires recoloured...");
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
                processControl.PostProcess(sheet, "");
            }

            if (zoom)
                ZoomToDocument(ctx);

            string file = Path.GetFileName(sheet.GetState_DocumentName());
            if (changed.Count == 0)
                return $"{file}: no wires, nothing changed.";

            var text = new StringBuilder();
            text.AppendLine($"{file}: {changed.Count} wire(s) recoloured ({(underline ? "UnderlineColor" : "Color")}, " +
                            $"{colors.Length} colour(s) in turn). Read back:");
            foreach (string line in changed.Take(10))
                text.AppendLine("  " + line);
            if (changed.Count > 10)
                text.AppendLine($"  ... {changed.Count - 10} more (ListWireColors shows all).");
            text.Append("The change is in memory only: save the document to keep it, or undo it.");
            return text.ToString();
        }

        /// <summary>ListWireColors ["Max=&lt;n&gt;"]: Color and UnderlineColor of each wire (read-only).</summary>
        [HotCommand("ListWireColors", "List the colours of the wires on the current sheet")]
        public static string ListWireColors(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max", "200"));
            ISch_Document sheet = CurrentSheet(SchServer());

            var lines = new List<string>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eWire });
                int n = 0;
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject(), n++)
                    lines.Add(Describe(n, (ISch_Wire)obj));
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }

            string file = Path.GetFileName(sheet.GetState_DocumentName());
            if (lines.Count == 0)
                return $"{file}: no wires.";
            var text = new StringBuilder();
            text.Append($"{file}: {lines.Count} wire(s)");
            text.AppendLine(lines.Count > max ? $", first {max} shown:" : ":");
            foreach (string line in lines.Take(max))
                text.AppendLine("  " + line);
            return text.ToString().TrimEnd();
        }

        private static string Describe(int n, ISch_Wire wire) =>
            $"#{n + 1}: Color={Hex(wire.GetState_Color())} UnderlineColor={Hex(wire.GetState_UnderlineColor())}";

        private static string Hex(int bgr) => "0x" + bgr.ToString("X6", CultureInfo.InvariantCulture);

        private static void ZoomToDocument(CommandContext ctx)
        {
            // The SCH:Zoom process with Action=Document. No SetFocus/Show on the view: the sample doesn't take the
            // focus.
            string parameters = "";
            DXP.Utils.SetParamterValue(ref parameters, "Action", "Document");
            if (ctx.View is IServerDocumentView view)
                DXP.Utils.RunCommand("SCH:Zoom", parameters, view);
            else
                DXP.Utils.RunCommand("SCH:Zoom", parameters);
        }

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer
            ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            // A schematic library is an ISch_Document too; tell them apart by the object ID (docs: Working with
            // schematics, "The current document").
            if (document.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static int[] ParseColors(string value)
        {
            var colors = new List<int>();
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string hex = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part.Substring(2) : part;
                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int color) ||
                    color < 0 || color > 0xFFFFFF)
                    throw new UserException($"Colors: '{part}' is not a BGR colour (6 hex digits, e.g. 0000C8 for red).");
                colors.Add(color);
            }
            if (colors.Count == 0)
                throw new UserException("Colors: give at least one BGR colour, e.g. Colors=0000C8,00C800,C80000.");
            return colors.ToArray();
        }

        private static bool ParseProperty(string value)
        {
            if (value.Equals("Color", StringComparison.OrdinalIgnoreCase))
                return false;
            if (value.Equals("UnderlineColor", StringComparison.OrdinalIgnoreCase))
                return true;
            throw new UserException($"Property must be Color or UnderlineColor, got '{value}'.");
        }

        private static bool ParseBool(string name, string value)
        {
            if (value.Length == 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1")
                return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0")
                return false;
            throw new UserException($"{name} must be true or false, got '{value}'.");
        }

        private static int ParseMax(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive integer, got '{value}'.");
            return max;
        }
    }
}
