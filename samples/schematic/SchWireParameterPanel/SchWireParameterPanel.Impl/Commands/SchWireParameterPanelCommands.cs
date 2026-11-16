using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // IClient.GetDocumentByPath is an extension method in namespace DXP
using SCH;

namespace AdSamples.SchWireParameterPanel
{
    /// <summary>
    /// Wire parameters (ISch_Parameter children of ISch_Wire) made visible and selected, on demand or after every
    /// schematic view update. The SDK lessons: a nested child iterator (sheet iterator for eWire, then each wire's own
    /// iterator for eParameter, both destroyed), ISch_ComplexText.IsHidden / ISch_GraphicalObject.EnableDraw /
    /// Selection on a child object, and, in the shim (WireViewShim.cs), a notification handler plus a per-document
    /// panel view added with IServerDocument.AddView.
    /// </summary>
    internal static class SchWireParameterPanelCommands
    {
        // The shim's AppDomain keys (WireViewShim.cs): repeated here, the implementation cannot reference the shim.
        private const string AutoShowKey = SampleInfo.Name + ".AutoShow";
        private const string AttachedKey = SampleInfo.Name + ".AttachedDocuments";
        private const string EnabledSinceKey = SampleInfo.Name + ".EnabledSince";
        private const string CountsKey = SampleInfo.Name + ".NotificationCounts";
        private const string LogKey = SampleInfo.Name + ".NotificationLog";
        private const string LastErrorKey = SampleInfo.Name + ".NotificationError";

        /// <summary>ShowPanel: opens the sample's panel, which lists the wire parameters of the current sheet (read-only).</summary>
        [HotCommand("ShowPanel", "Open the wire parameter panel")]
        public static string ShowPanel(CommandContext ctx)
        {
            // The per-document views appear on document activation (AutoShow).
            SamplePanel.Show();
            Ui.PanelContent.RefreshCurrent();
            return ctx.IsInteractive ? null : SampleInfo.Name + " panel shown. " + AutoShowStatusLine();
        }

        /// <summary>ListWireParameters ["Max=&lt;n&gt;"]: every wire parameter of the sheet with its hidden / drawn / selected state (read-only).</summary>
        [HotCommand("ListWireParameters", "List the wire parameters of the current sheet")]
        public static string ListWireParameters(CommandContext ctx)
        {
            if (!int.TryParse(ctx.Get("Max", "50"), out int max) || max <= 0)
                throw new UserException("Max must be an integer > 0.");
            ISch_Document sheet = CurrentSheet(SchServer());
            return Describe(sheet, Collect(sheet), max);
        }

        /// <summary>
        /// ShowWireParameters ["Select=true|false"]: the view's EndUpdate work, run on demand. For every
        /// parameter of every wire: IsHidden = false, EnableDraw = true, Selection = true (unless Select=false), then
        /// GraphicallyInvalidate. Returns how many wires and parameters there are and how many were changed.
        /// </summary>
        [HotCommand("ShowWireParameters", "Unhide and select every wire parameter of the current sheet")]
        public static string ShowWireParameters(CommandContext ctx)
        {
            bool select = !string.Equals(ctx.Get("Select", "true"), "false", StringComparison.OrdinalIgnoreCase);
            bool fromView = string.Equals(ctx.Get("Source", ""), "EndUpdate", StringComparison.OrdinalIgnoreCase);

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer;
            ISch_Document sheet = schServer?.GetCurrentSchDocument();
            if (sheet == null || sheet.GetState_ObjectId() == TObjectId.eSchLib)
            {
                // From the view's EndUpdate it simply exits when there is no current sheet.
                if (fromView)
                    return null;
                CurrentSheet(schServer); // throws the user-facing message
            }

            ISch_RobotManager robot = schServer.GetState_RobotManager();
            List<WireParameter> all = Collect(sheet);
            int unhidden = 0, drawn = 0, selected = 0;
            foreach (WireParameter item in all)
            {
                ISch_Parameter parameter = item.Parameter;
                bool hide = parameter.GetState_IsHidden();
                bool draw = !parameter.GetState_EnableDraw();
                bool sel = select && !parameter.GetState_Selection();

                // Only touch what differs, so a second run changes nothing: if this work raises an EndUpdate, the run
                // it triggers changes nothing and the round ends (the shim also guards re-entry).
                if (!hide && !draw && !sel)
                    continue;

                if (hide)
                {
                    // Bracket the change with SCHMBeginModify / SCHMEndModify robot-manager messages, as the schematic
                    // guide shows for modifying an object.
                    robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                    parameter.SetState_IsHidden(false);
                    robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                    unhidden++;
                }
                if (draw)
                {
                    parameter.SetState_EnableDraw(true);
                    drawn++;
                }
                if (sel)
                {
                    parameter.SetState_Selection(true);
                    selected++;
                }
                parameter.GraphicallyInvalidate();
            }

            if (unhidden > 0)
                DXP.GlobalVars.Client.GetDocumentByPath(sheet.GetState_DocumentName())?.SetModified(true);

            int wires = all.Select(p => p.WireIndex).Distinct().Count();
            string text = $"{FileName(sheet)}: {all.Count} wire parameter(s) on {wires} wire(s); " +
                          $"unhidden {unhidden}, draw enabled {drawn}, selected {selected}" + (select ? "" : " (Select=false)") + ".";
            if (!fromView)
                Ui.PanelContent.RefreshCurrent();
            return text;
        }

        /// <summary>
        /// AutoShow ["State=on|off|status"]: the automatic behaviour, off by default. On: the shim registers a
        /// notification handler, adds the holder view to every schematic document that is activated (and to the focused
        /// one now), and the view runs ShowWireParameters on its EndUpdate view code. Off: unregisters the handler.
        /// </summary>
        [HotCommand("AutoShow", "Show whether automatic mode (document view + EndUpdate handler) is on")]
        public static string AutoShow(CommandContext ctx)
        {
            string state = ctx.Get("State", "status").ToLowerInvariant();
            string action;
            switch (state)
            {
                case "status":
                    return AutoShowStatus();
                case "on":
                case "off":
                    var setAutoShow = AppDomain.CurrentDomain.GetData(AutoShowKey) as Func<bool, string>
                        ?? throw new UserException(SampleInfo.Name + " shim is not loaded: redeploy the extension and restart Altium Designer.");
                    action = setAutoShow(state == "on");
                    break;
                default:
                    throw new UserException($"State must be on, off or status, got '{state}'.");
            }
            Ui.PanelContent.RefreshCurrent();
            return $"AutoShow {state}: {action}.{Environment.NewLine}{AutoShowStatus()}";
        }

        // ---- shared with the panel ------------------------------------------------------------------------------

        internal static string AutoShowStatusLine()
        {
            bool on = AppDomain.CurrentDomain.GetData(EnabledSinceKey) is DateTime;
            return "AutoShow is " + (on ? "on" : "off") + ".";
        }

        /// <summary>The panel's text: the sheet's wire parameters, or why there are none.</summary>
        internal static string DescribeCurrentSheet()
        {
            ISch_Document sheet = SCH.GlobalVars.SchServer?.GetCurrentSchDocument();
            if (sheet == null || sheet.GetState_ObjectId() == TObjectId.eSchLib)
                return "No schematic sheet is active. Focus a .SchDoc and press Refresh.";
            return Describe(sheet, Collect(sheet), 200);
        }

        internal static string AutoShowStatus()
        {
            var text = new StringBuilder();
            object since = AppDomain.CurrentDomain.GetData(EnabledSinceKey);
            text.AppendLine(since is DateTime t ? $"AutoShow is on (since {t:HH:mm:ss})." : "AutoShow is off.");
            if (AppDomain.CurrentDomain.GetData(AutoShowKey) == null)
                text.AppendLine("The shim's switches are missing: the deployed shim is not this sample's (redeploy and restart AD).");

            string[] attached = (AppDomain.CurrentDomain.GetData(AttachedKey) as Func<string[]>)?.Invoke() ?? Array.Empty<string>();
            text.AppendLine($"Views attached: {attached.Length}" + (attached.Length > 0 ? " (" + string.Join(", ", attached.Select(Path.GetFileName)) + ")" : ""));

            if (AppDomain.CurrentDomain.GetData(CountsKey) is ConcurrentDictionary<string, int> counts && !counts.IsEmpty)
            {
                text.AppendLine("Notifications received (source:type/code = count):");
                foreach (var pair in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
                    text.AppendLine($"  {pair.Key} = {pair.Value}");
            }
            else
                text.AppendLine("Notifications received: none.");

            if (AppDomain.CurrentDomain.GetData(LogKey) is ConcurrentQueue<string> log && !log.IsEmpty)
            {
                text.AppendLine("Last notifications:");
                foreach (string line in log.ToArray().Reverse().Take(10))
                    text.AppendLine("  " + line);
            }
            if (AppDomain.CurrentDomain.GetData(LastErrorKey) is string error)
                text.AppendLine("Last handler error: " + error);
            return text.ToString().TrimEnd();
        }

        // ---- helpers --------------------------------------------------------------------------------------------

        private sealed record WireParameter(int WireIndex, ISch_Parameter Parameter);

        /// <summary>
        /// The nested loop: a sheet iterator for wires, and for each wire its own iterator for its child
        /// parameters. Both iterators are destroyed.
        /// </summary>
        private static List<WireParameter> Collect(ISch_Document sheet)
        {
            var result = new List<WireParameter>();
            ISch_Iterator wires = sheet.SchIterator_Create() ?? throw new UserException("Could not create an iterator on the sheet.");
            try
            {
                wires.AddFilter_ObjectSet(new TObjectSet { TObjectId.eWire });
                int index = 0;
                for (ISch_BasicContainer obj = wires.FirstSchObject(); obj != null; obj = wires.NextSchObject(), index++)
                {
                    var wire = (ISch_Wire)obj; // the filter guarantees wires
                    ISch_Iterator children = wire.SchIterator_Create();
                    if (children == null)
                        continue;
                    try
                    {
                        children.AddFilter_ObjectSet(new TObjectSet { TObjectId.eParameter });
                        for (ISch_BasicContainer child = children.FirstSchObject(); child != null; child = children.NextSchObject())
                            result.Add(new WireParameter(index, (ISch_Parameter)child));
                    }
                    finally
                    {
                        wire.SchIterator_Destroy(ref children);
                    }
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref wires);
            }
            return result;
        }

        private static string Describe(ISch_Document sheet, List<WireParameter> all, int max)
        {
            var text = new StringBuilder();
            int wires = all.Select(p => p.WireIndex).Distinct().Count();
            text.Append($"{FileName(sheet)}: {all.Count} wire parameter(s) on {wires} wire(s)");
            text.Append($" (hidden {all.Count(p => p.Parameter.GetState_IsHidden())}, selected {all.Count(p => p.Parameter.GetState_Selection())})");
            text.AppendLine(all.Count > max ? $", first {max}:" : all.Count > 0 ? ":" : ".");
            foreach (WireParameter item in all.Take(max))
            {
                ISch_Parameter p = item.Parameter;
                text.AppendLine($"  wire #{item.WireIndex}: {p.GetState_Name()}={p.GetState_Text()} " +
                                $"hidden={p.GetState_IsHidden()} draw={p.GetState_EnableDraw()} selected={p.GetState_Selection()}");
            }
            return text.ToString().TrimEnd();
        }

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer?.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static string FileName(ISch_Document sheet) => Path.GetFileName(sheet.GetState_DocumentName());
    }
}
