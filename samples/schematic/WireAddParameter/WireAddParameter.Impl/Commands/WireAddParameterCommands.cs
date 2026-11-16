using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // IClient.GetDocumentByPath is an extension method in namespace DXP
using SCH;

namespace AdSamples.WireAddParameter
{
    /// <summary>
    /// Parameters as child objects of a schematic wire. The SDK lesson: an ISch_Parameter made by
    /// ISch_ServerInterface.SchObjectFactory becomes a child of any container with ISch_BasicContainer.AddSchObject,
    /// is found again with a child iterator limited to the first level (TIterationDepth.eIterateFirstLevel), and is
    /// taken off with RemoveSchObject. The wire comes from the right-click hit test (ISch_Document.PopupMenuHitTest)
    /// when the command runs from the schematic context menu, or from the selection otherwise.
    /// </summary>
    internal static class WireAddParameterCommands
    {
        // The default name and value.
        private const string DefaultName = "NewParamName";
        private const string DefaultValue = "NewParamValue";

        /// <summary>
        /// AddParameterToWire ["Name=&lt;text&gt;|Value=&lt;text&gt;|Wire=popup|selected|Index=&lt;n&gt;"]: adds a visible
        /// parameter (name shown) at the wire's location and returns the wire's parameters read back.
        /// </summary>
        [HotCommand("AddParameterToWire", "Add a parameter to the wire under the right-click cursor or the selected wire")]
        public static string AddParameterToWire(CommandContext ctx)
        {
            string name = ctx.Get("Name", DefaultName);
            string value = ctx.Get("Value", DefaultValue);
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("Name must not be empty.");

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            (ISch_Wire wire, string which) = ResolveWire(ctx, sheet);
            ISch_RobotManager robot = schServer.GetState_RobotManager();

            var parameter = schServer.SchObjectFactory(TObjectId.eParameter, TObjectCreationMode.eCreate_Default) as ISch_Parameter
                ?? throw new UserException("SchObjectFactory(eParameter) returned no parameter.");

            // Properties first, then add the parameter to its owner. The robot-manager
            // messages tell the editor (undo, inspector) about the change.
            parameter.SetState_Name(name);
            parameter.SetState_Text(value);
            parameter.SetState_IsHidden(false);
            parameter.SetState_ShowName(true);
            parameter.SetState_Location(wire.GetState_Location());

            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            processControl.PreProcess(sheet, "");
            try
            {
                robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                wire.AddSchObject(parameter);
                robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, parameter);
                robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }

            MarkModified(sheet);
            wire.GraphicallyInvalidate();

            List<string> readBack = ReadParameters(wire).Select(p => p.Name + "=" + p.Value).ToList();
            return $"{FileName(sheet)}: added parameter {name}={value} to {which}. " +
                   $"The wire now has {readBack.Count} parameter(s): {Join(readBack)}";
        }

        /// <summary>GetParameterFromWire ["Wire=popup|selected|Index=&lt;n&gt;"]: the wire's parameters as Name=Value (read-only).</summary>
        [HotCommand("GetParameterFromWire", "List the parameters of the wire under the right-click cursor or the selected wire")]
        public static string GetParameterFromWire(CommandContext ctx)
        {
            ISch_Document sheet = CurrentSheet(SchServer());
            (ISch_Wire wire, string which) = ResolveWire(ctx, sheet);

            // Returned as text, comma-joined.
            List<string> pairs = ReadParameters(wire).Select(p => p.Name + "=" + p.Value).ToList();
            return $"{FileName(sheet)}: {which} has {pairs.Count} parameter(s): {Join(pairs)}";
        }

        /// <summary>
        /// RemoveParameterFromWire ["Name=&lt;text&gt;|Wire=popup|selected|Index=&lt;n&gt;"]: removes all the wire's
        /// parameters, or only those called Name (case-insensitive), and returns what is left.
        /// </summary>
        [HotCommand("RemoveParameterFromWire", "Remove the parameters of the wire under the right-click cursor or the selected wire")]
        public static string RemoveParameterFromWire(CommandContext ctx)
        {
            string only = ctx.Get("Name", null);
            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            (ISch_Wire wire, string which) = ResolveWire(ctx, sheet);
            ISch_RobotManager robot = schServer.GetState_RobotManager();

            // Collect first, remove afterwards. Calling RemoveSchObject and then NextSchObject on the
            // same iterator, which changes the list the iterator walks.
            List<ParameterInfo> targets = ReadParameters(wire)
                .Where(p => only == null || string.Equals(p.Name, only, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (targets.Count == 0)
                return $"{FileName(sheet)}: {which} has no parameter{(only == null ? "s" : " called " + only)}, nothing removed.";

            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            processControl.PreProcess(sheet, "");
            try
            {
                robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                foreach (ParameterInfo target in targets)
                    wire.RemoveSchObject(target.Parameter);
                robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }

            MarkModified(sheet);
            wire.GraphicallyInvalidate();

            List<string> left = ReadParameters(wire).Select(p => p.Name + "=" + p.Value).ToList();
            return $"{FileName(sheet)}: removed {targets.Count} parameter(s) from {which} " +
                   $"({Join(targets.Select(p => p.Name).ToList())}). Left: {left.Count}{(left.Count > 0 ? " (" + Join(left) + ")" : "")}.";
        }

        /// <summary>ListWires ["Max=&lt;n&gt;"]: the wires of the current sheet with index, location, selection and parameter count (test helper).</summary>
        [HotCommand("ListWires", "List the wires of the current sheet with their parameter counts (test helper)")]
        public static string ListWires(CommandContext ctx)
        {
            int max = ParseInt("Max", ctx.Get("Max", "20"), 1);
            ISch_Document sheet = CurrentSheet(SchServer());
            List<ISch_Wire> wires = Wires(sheet);

            var text = new StringBuilder();
            text.Append($"{FileName(sheet)}: {wires.Count} wire(s), {wires.Count(w => w.GetState_Selection())} selected");
            text.AppendLine(wires.Count > max ? $", first {max} shown:" : ":");
            for (int i = 0; i < Math.Min(max, wires.Count); i++)
            {
                ISch_Wire wire = wires[i];
                Point location = wire.GetState_Location();
                text.AppendLine($"  #{i}: location=({location.X}, {location.Y}) selected={wire.GetState_Selection()} " +
                                $"parameters={ReadParameters(wire).Count}");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>SelectWire "Index=&lt;n&gt;": selects wire n of ListWires and deselects the others (test helper; the document is not changed).</summary>
        [HotCommand("SelectWire", "Select one wire of the current sheet by its ListWires index (test helper)")]
        public static string SelectWire(CommandContext ctx)
        {
            ISch_Document sheet = CurrentSheet(SchServer());
            List<ISch_Wire> wires = Wires(sheet);
            if (wires.Count == 0)
                throw new UserException($"{FileName(sheet)} has no wires.");
            int index = ParseInt("Index", ctx.Get("Index", "0"), 0);
            if (index >= wires.Count)
                throw new UserException($"Index {index} is out of range: {FileName(sheet)} has {wires.Count} wire(s) (0..{wires.Count - 1}).");

            for (int i = 0; i < wires.Count; i++)
            {
                if (wires[i].GetState_Selection() != (i == index))
                {
                    wires[i].SetState_Selection(i == index);
                    wires[i].GraphicallyInvalidate();
                }
            }
            Point location = wires[index].GetState_Location();
            return $"{FileName(sheet)}: selected wire #{index} at ({location.X}, {location.Y}); " +
                   $"{wires.Count(w => w.GetState_Selection())} wire(s) selected now.";
        }

        private sealed record ParameterInfo(ISch_Parameter Parameter, string Name, string Value);

        /// <summary>
        /// The wire chosen by Wire=: "popup" = the first wire of ISch_Document.PopupMenuHitTest (what
        /// the context menu items pass), "selected" (default) = selected wire number Index (0-based).
        /// </summary>
        private static (ISch_Wire Wire, string Description) ResolveWire(CommandContext ctx, ISch_Document sheet)
        {
            string source = ctx.Get("Wire", "selected");
            if (source.Equals("popup", StringComparison.OrdinalIgnoreCase))
            {
                // The first eWire among the objects under the right-click
                // position. Overlapping wires: the first one hit wins.
                ISch_HitTest hitTest = sheet.PopupMenuHitTest();
                int count = hitTest?.GetState_HitTestCount() ?? 0;
                for (int i = 0; i < count; i++)
                {
                    ISch_GraphicalObject hit = hitTest.GetState_HitObject(i);
                    if (hit != null && hit.GetState_ObjectId() == TObjectId.eWire)
                        return ((ISch_Wire)hit, "the wire under the right-click cursor");
                }
                throw new UserException($"No wire under the right-click position (hit objects: {count}). " +
                                        "Right-click a wire, or select one and use Wire=selected.");
            }
            if (!source.Equals("selected", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Wire must be popup or selected, got '{source}'.");

            int index = ParseInt("Index", ctx.Get("Index", "0"), 0);
            List<ISch_Wire> selected = Wires(sheet).Where(w => w.GetState_Selection()).ToList();
            if (selected.Count == 0)
                throw new UserException($"No wire is selected on {FileName(sheet)}. Select a wire, or right-click one and use the context menu.");
            if (index >= selected.Count)
                throw new UserException($"Index {index} is out of range: {selected.Count} wire(s) selected (0..{selected.Count - 1}).");
            return (selected[index], $"selected wire {index + 1} of {selected.Count}");
        }

        /// <summary>The wire's own parameters: a child iterator, first level only.</summary>
        private static List<ParameterInfo> ReadParameters(ISch_Wire wire)
        {
            var result = new List<ParameterInfo>();
            ISch_Iterator iterator = wire.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the wire.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eParameter });
                iterator.SetState_IterationDepth(TIterationDepth.eIterateFirstLevel);
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                {
                    var parameter = (ISch_Parameter)obj; // the filter guarantees parameters
                    result.Add(new ParameterInfo(parameter, parameter.GetState_Name(), parameter.GetState_Text()));
                }
            }
            finally
            {
                wire.SchIterator_Destroy(ref iterator);
            }
            return result;
        }

        /// <summary>All wires of the sheet, in iterator order (the ListWires / SelectWire index).</summary>
        private static List<ISch_Wire> Wires(ISch_Document sheet)
        {
            var wires = new List<ISch_Wire>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eWire });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    wires.Add((ISch_Wire)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return wires;
        }

        /// <summary>
        /// Marks the sheet's own server document as modified, not Client.CurrentView.OwnerDocument,
        /// which is the focused view's document and not necessarily the sheet that owns the wire.
        /// </summary>
        private static void MarkModified(ISch_Document sheet)
        {
            IServerDocument document = DXP.GlobalVars.Client.GetDocumentByPath(sheet.GetState_DocumentName());
            document?.SetModified(true);
        }

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer
            ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static string FileName(ISch_Document sheet) => Path.GetFileName(sheet.GetState_DocumentName());

        private static string Join(List<string> items) => items.Count == 0 ? "(none)" : string.Join(", ", items);

        private static int ParseInt(string name, string value, int min)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min)
                throw new UserException($"{name} must be an integer >= {min}, got '{value}'.");
            return n;
        }
    }
}
