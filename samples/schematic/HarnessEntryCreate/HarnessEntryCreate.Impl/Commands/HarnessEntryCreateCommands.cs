using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.HarnessEntryCreate
{
    /// <summary>
    /// Adds a harness entry to a harness connector on the current schematic sheet. The SDK lesson: a child object
    /// is created with ISch_ServerInterface.SchObjectFactory and added to its owner (here the connector, not the
    /// sheet) with ISch_BasicContainer.AddSchObject; the owner's change is announced to the editor through the
    /// robot manager (docs: Working with schematics, "Changing objects" and "Creating objects").
    /// </summary>
    internal static class HarnessEntryCreateCommands
    {
        // The reference gives no unit for DistanceFromTop (README Notes).
        private const int DefaultDistanceFromTop = 20;

        /// <summary>
        /// CreateHarnessEntry ["DistanceFromTop=&lt;n&gt;|Name=&lt;text&gt;|Side=Left|Right|Top|Bottom"]: adds a harness
        /// entry to the first harness connector of the current sheet and returns what was read back from it.
        /// </summary>
        [HotCommand("CreateHarnessEntry", "Add a harness entry to the first harness connector on the sheet")]
        public static string CreateHarnessEntry(CommandContext ctx)
        {
            int distance = ParseInt("DistanceFromTop", ctx.Get("DistanceFromTop", DefaultDistanceFromTop.ToString(CultureInfo.InvariantCulture)));
            string name = ctx.Get("Name", "");
            TLeftRightSide? side = ParseSide(ctx.Get("Side", ""));

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);

            ISch_HarnessConnector connector = FindConnectors(sheet).FirstOrDefault()
                ?? throw new UserException($"{Path.GetFileName(sheet.GetState_DocumentName())} has no harness connector. " +
                                           "Focus a sheet with a harness connector.");
            int entriesBefore = Entries(connector).Count;

            // PreProcess/PostProcess bracket the change, as the docs example IProcessControl.PreProcess does for a
            // new object; the connector is the object that changes, so it gets the SCHMBeginModify/SCHMEndModify
            // pair. Which robot messages AD expects for a new child object is not documented (README Notes).
            ISch_RobotManager robot = schServer.GetState_RobotManager();
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            ISch_HarnessEntry entry;
            processControl.PreProcess(sheet, "");
            try
            {
                entry = schServer.SchObjectFactory(TObjectId.eHarnessEntry, TObjectCreationMode.eCreate_Default) as ISch_HarnessEntry
                    ?? throw new UserException("SchObjectFactory did not create a harness entry.");

                robot.SendMessage(connector, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                try
                {
                    // Add the entry to its connector, then position it.
                    connector.AddSchObject(entry);
                    entry.SetState_DistanceFromTop(distance);
                    if (name.Length > 0)
                        entry.SetState_Name(name);
                    if (side.HasValue)
                        entry.SetState_Side(side.Value);
                    // Recalculates the entry's size from its properties.
                    entry.SetState_xSizeySize();
                }
                finally
                {
                    robot.SendMessage(connector, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                }
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }

            // Redraw the connector so the new entry shows without waiting for the next repaint.
            connector.GraphicallyInvalidate();

            int entriesAfter = Entries(connector).Count;
            return $"{Path.GetFileName(sheet.GetState_DocumentName())}: added a harness entry to connector " +
                   $"{DescribeConnector(connector)}; read back: {DescribeEntry(entry)}. Entries on the connector " +
                   $"{entriesBefore} -> {entriesAfter}. The change is in memory only: save the sheet to keep it, or undo it.";
        }

        /// <summary>ListHarnessEntries: the harness connectors of the current sheet with their entries (read-only).</summary>
        [HotCommand("ListHarnessEntries", "List the harness connectors and their entries on the sheet")]
        public static string ListHarnessEntries(CommandContext ctx)
        {
            ISch_Document sheet = CurrentSheet(SchServer());
            List<ISch_HarnessConnector> connectors = FindConnectors(sheet);

            string file = Path.GetFileName(sheet.GetState_DocumentName());
            if (connectors.Count == 0)
                return $"{file}: no harness connectors.";

            var text = new StringBuilder();
            text.AppendLine($"{file}: {connectors.Count} harness connector(s).");
            for (int i = 0; i < connectors.Count; i++)
            {
                List<ISch_HarnessEntry> entries = Entries(connectors[i]);
                text.AppendLine($"#{i + 1} {DescribeConnector(connectors[i])}: {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}");
                foreach (ISch_HarnessEntry entry in entries)
                    text.AppendLine("    " + DescribeEntry(entry));
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>The harness connectors placed on the sheet, in iterator order.</summary>
        private static List<ISch_HarnessConnector> FindConnectors(ISch_Document sheet)
        {
            var connectors = new List<ISch_HarnessConnector>();
            // Check the iterator for null before adding the filter to it.
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eHarnessConnector });
                // The filter guarantees connectors; a type test would not help (it succeeds for every SDK object).
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    connectors.Add((ISch_HarnessConnector)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return connectors;
        }

        /// <summary>The entries of one connector: a connector is a container, iterated like a sheet.</summary>
        private static List<ISch_HarnessEntry> Entries(ISch_HarnessConnector connector)
        {
            var entries = new List<ISch_HarnessEntry>();
            ISch_Iterator iterator = connector.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the harness connector.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eHarnessEntry });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    entries.Add((ISch_HarnessEntry)obj);
            }
            finally
            {
                connector.SchIterator_Destroy(ref iterator);
            }
            return entries;
        }

        private static string DescribeConnector(ISch_HarnessConnector connector)
        {
            DXP.Point location = connector.GetState_Location();
            string type = connector.GetState_SchHarnessConnectorType()?.GetState_Text() ?? "";
            return $"'{type}' at ({EDP.Utils.CoordToMils(location.X):0.##}, {EDP.Utils.CoordToMils(location.Y):0.##}) mil";
        }

        private static string DescribeEntry(ISch_HarnessEntry entry) =>
            $"Name='{entry.GetState_Name()}' Side={entry.GetState_Side()} DistanceFromTop={entry.GetState_DistanceFromTop()}";

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer
            ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            // A schematic library is an ISch_Document too; tell them apart by the object ID (docs: Working with
            // schematics, "The current document").
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static int ParseInt(string name, string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result < 0)
                throw new UserException($"{name} must be a non-negative integer, got '{value}'.");
            return result;
        }

        private static TLeftRightSide? ParseSide(string value)
        {
            if (value.Length == 0)
                return null; // keep the factory default
            if (Enum.TryParse("e" + value + "Side", ignoreCase: true, out TLeftRightSide side) && Enum.IsDefined(side))
                return side;
            throw new UserException($"Side must be Left, Right, Top or Bottom, got '{value}'.");
        }
    }
}
