using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SCH;

namespace AdSamples.IterateNetLabel
{
    /// <summary>
    /// Walks the net labels of the current schematic sheet with a filtered ISch_Iterator: the basic pattern for
    /// visiting one kind of schematic object. Change the TObjectId in the filter to visit wires, ports, components...
    /// </summary>
    internal static class IterateNetLabelCommands
    {
        /// <summary>IterateNetLabel ["Max=&lt;n&gt;"]: lists the DisplayString of every net label (at most Max lines).</summary>
        [HotCommand("IterateNetLabel", "List the net labels on the current schematic sheet")]
        public static string IterateNetLabel(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max", "200"));

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");

            var labels = new List<string>();
            ISch_Iterator iterator = sheet.SchIterator_Create();
            try
            {
                // The filter takes a set of object kinds, given as an SCH.TObjectSet.
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eNetLabel });

                // FirstSchObject/NextSchObject return ISch_BasicContainer. The filter guarantees net labels, so the
                // cast is safe here (a type test would not help: it succeeds for every SDK object).
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                {
                    var netLabel = (ISch_NetLabel)obj;
                    // DisplayString is the text as shown on the sheet, after special-string evaluation.
                    labels.Add(netLabel.GetState_DisplayString());
                }
            }
            finally
            {
                // Destroy the iterator also when the loop throws (docs: Interfaces at run time).
                sheet.SchIterator_Destroy(ref iterator);
            }

            string file = Path.GetFileName(sheet.GetState_DocumentName());
            if (labels.Count == 0)
                return $"{file}: no net labels.";

            var text = new StringBuilder();
            text.Append($"{file}: {labels.Count} net label(s)");
            text.AppendLine(labels.Count > max ? $", first {max} shown:" : ":");
            for (int i = 0; i < labels.Count && i < max; i++)
                text.AppendLine("  " + labels[i]);
            return text.ToString().TrimEnd();
        }

        private static int ParseMax(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive integer, got '{value}'.");
            return max;
        }
    }
}
