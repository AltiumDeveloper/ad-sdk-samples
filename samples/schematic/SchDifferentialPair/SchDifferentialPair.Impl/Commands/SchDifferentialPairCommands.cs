using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.SchDifferentialPair
{
    /// <summary>
    /// Places a parameter set named DIFFPAIR, holding one hidden string parameter DifferentialPair=TRUE, on the
    /// current schematic sheet. The SDK lesson: a container object (the parameter set) is created with
    /// ISch_ServerInterface.SchObjectFactory, a child object (the parameter) is created the same way and added to it
    /// with ISch_BasicContainer.AddSchObject, and the finished container is added to the sheet.
    /// </summary>
    internal static class SchDifferentialPairCommands
    {
        // Default location 2400, 4000 mil (internal 24000000, 40000000). Schematic coordinates use the same
        // factor as EDP.Utils.MilsToCoord (README Notes).
        private const string DefaultX = "2400";
        private const string DefaultY = "4000";

        /// <summary>
        /// PlaceDiffPairDirective ["X=&lt;mil&gt;|Y=&lt;mil&gt;|Name=&lt;set name&gt;|Value=&lt;text&gt;"]: adds a parameter set
        /// with one hidden DifferentialPair parameter to the current sheet and returns what was read back from it.
        /// </summary>
        [HotCommand("PlaceDiffPairDirective", "Place a DIFFPAIR parameter set on the current sheet")]
        public static string PlaceDiffPairDirective(CommandContext ctx)
        {
            double xMil = ParseMil("X", ctx.Get("X", DefaultX));
            double yMil = ParseMil("Y", ctx.Get("Y", DefaultY));
            string setName = ctx.Get("Name", "DIFFPAIR");
            string value = ctx.Get("Value", "TRUE");
            if (setName.Length == 0)
                throw new UserException("Name must not be empty.");

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            int setsBefore = FindParameterSets(sheet).Count;

            // PreProcess/PostProcess bracket the change and the new object is announced with
            // SCHMPrimitiveRegistration, as the docs example IProcessControl.PreProcess does for a new port.
            // AddSchObject plus this message is enough (README Notes).
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            ISch_ParameterSet set;
            processControl.PreProcess(sheet, "");
            try
            {
                set = schServer.SchObjectFactory(TObjectId.eParameterSet, TObjectCreationMode.eCreate_Default) as ISch_ParameterSet
                    ?? throw new UserException("SchObjectFactory did not create a parameter set.");
                set.SetState_Location(new DXP.Point(new SPoint { X = EDP.Utils.MilsToCoord(xMil), Y = EDP.Utils.MilsToCoord(yMil) }));
                set.SetState_Name(setName);

                ISch_Parameter parameter = schServer.SchObjectFactory(TObjectId.eParameter, TObjectCreationMode.eCreate_Default) as ISch_Parameter
                    ?? throw new UserException("SchObjectFactory did not create a parameter.");
                parameter.SetState_Name("DifferentialPair");
                parameter.SetState_Text(value);
                parameter.SetState_ParamType(TParameterType.eParameterType_String);
                parameter.SetState_IsHidden(true);
                parameter.SetState_ShowName(false);

                // The parameter goes into the set, then the set onto the sheet.
                set.AddSchObject(parameter);
                sheet.AddSchObject(set);
                schServer.GetState_RobotManager().SendMessage(sheet, SCHConstant.BroadCast,
                    SCHConstant.SCHMPrimitiveRegistration, set);
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }

            // Redraw so the new set shows without waiting for the next repaint.
            set.GraphicallyInvalidate();

            int setsAfter = FindParameterSets(sheet).Count;
            return $"{Path.GetFileName(sheet.GetState_DocumentName())}: placed parameter set {Describe(set)}. " +
                   $"Parameter sets on the sheet {setsBefore} -> {setsAfter}. " +
                   "The change is in memory only: save the sheet to keep it, or undo it.";
        }

        /// <summary>ListParameterSets ["Max=&lt;n&gt;"]: the parameter sets of the current sheet and their parameters (read-only).</summary>
        [HotCommand("ListParameterSets", "List the parameter sets on the current sheet")]
        public static string ListParameterSets(CommandContext ctx)
        {
            if (!int.TryParse(ctx.Get("Max", "200"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException("Max must be a positive whole number.");

            ISch_Document sheet = CurrentSheet(SchServer());
            List<ISch_ParameterSet> sets = FindParameterSets(sheet);

            var text = new StringBuilder();
            text.AppendLine($"{Path.GetFileName(sheet.GetState_DocumentName())}: {sets.Count} parameter set(s).");
            for (int i = 0; i < sets.Count && i < max; i++)
                text.AppendLine($"  #{i + 1} {Describe(sets[i])}");
            if (sets.Count > max)
                text.AppendLine($"  ... {sets.Count - max} more (raise Max to see them).");
            return text.ToString().TrimEnd();
        }

        /// <summary>The parameter sets placed on the sheet, in iterator order.</summary>
        private static List<ISch_ParameterSet> FindParameterSets(ISch_Document sheet)
        {
            var sets = new List<ISch_ParameterSet>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eParameterSet });
                // The filter guarantees parameter sets; a type test would not help (it succeeds for every SDK object).
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    sets.Add((ISch_ParameterSet)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return sets;
        }

        /// <summary>A parameter set is a container: its parameters are iterated like the objects of a sheet.</summary>
        private static List<ISch_Parameter> Parameters(ISch_ParameterSet set)
        {
            var parameters = new List<ISch_Parameter>();
            ISch_Iterator iterator = set.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the parameter set.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eParameter });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    parameters.Add((ISch_Parameter)obj);
            }
            finally
            {
                set.SchIterator_Destroy(ref iterator);
            }
            return parameters;
        }

        private static string Describe(ISch_ParameterSet set)
        {
            DXP.Point location = set.GetState_Location();
            var text = new StringBuilder();
            text.Append($"'{set.GetState_Name()}' at ({EDP.Utils.CoordToMils(location.X):0.##}, " +
                        $"{EDP.Utils.CoordToMils(location.Y):0.##}) mil [internal {location.X}, {location.Y}], parameters:");
            List<ISch_Parameter> parameters = Parameters(set);
            if (parameters.Count == 0)
                text.Append(" none");
            foreach (ISch_Parameter p in parameters)
                text.Append($" {p.GetState_Name()}='{p.GetState_Text()}' (type {p.GetState_ParamType()}, " +
                            $"hidden {p.GetState_IsHidden()}, showName {p.GetState_ShowName()});");
            return text.ToString().TrimEnd(';');
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
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static double ParseMil(string name, string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mil) || mil < 0)
                throw new UserException($"{name} must be a non-negative number of mils, got '{value}'.");
            return mil;
        }
    }
}
