using System;
using System.Globalization;
using System.IO;
using System.Text;
using PCB;

namespace AdSamples.ComponentBodyOpacity
{
    /// <summary>
    /// Probes the 3D state of the current board: its Z range, the camera of its main graphical view, one entry of the
    /// board's exported parameter string, and the 3D opacity of a component body. The read-only part is one command;
    /// the two writes are separate commands.
    /// </summary>
    internal static class ComponentBodyOpacityCommands
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>BoardInfo3D: read-only report of the board Z range, the 3D camera and ROUTETOOLPATHLAYER.</summary>
        [HotCommand("BoardInfo3D", "Report the board Z range, the 3D camera and ROUTETOOLPATHLAYER")]
        public static string BoardInfo3D(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            var text = new StringBuilder();
            text.AppendLine(Path.GetFileName(board.GetState_FileName()) + ":");

            // The units of the two values are not documented; they are printed as returned.
            board.GetState_ZTopBottom(out float zTop, out float zBottom);
            text.AppendLine($"ZTop = {F(zTop)}, ZBottom = {F(zBottom)}");

            IPCB_GraphicalView view = board.GetState_MainGraphicalView();
            text.AppendLine(view == null ? "Camera: the board has no main graphical view." : "Camera: " + DescribeCamera(view));

            // Export_ToParameters (inherited from IPCB_Primitive) writes the board's settings as a "Key=Value|..."
            // string. DXP.Utils.GetParamterValue (sic) picks one value out of it.
            string parameters = string.Empty;
            board.Export_ToParameters(ref parameters);
            string value = string.Empty;
            bool found = DXP.Utils.GetParamterValue(parameters, "ROUTETOOLPATHLAYER", ref value);
            text.AppendLine(found
                ? $"ROUTETOOLPATHLAYER value is {value}"
                : "ROUTETOOLPATHLAYER value is Do Not Use (not in the exported parameters)");

            // The same setting through the typed property, for comparison with the exported string.
            V7_LayerBase layer = board.GetState_RouteToolPathLayer();
            text.AppendLine($"RouteToolPathLayer property: {board.LayerName(layer)} (ID {layer.GetID()})");
            text.Append($"Exported parameter string: {parameters.Length} characters");
            return text.ToString();
        }

        /// <summary>
        /// SetCamera "LookAtX=..|LookAtY=..|LookAtZ=..|QuatX=..|QuatY=..|QuatZ=..|QuatW=..|Zoom=..": writes the camera of
        /// the main graphical view and reads it back. Every value left out keeps its current value, so a run without
        /// parameters writes the camera it just read.
        /// </summary>
        [HotCommand("SetCamera", "Set the 3D camera of the main view (default: current camera)")]
        public static string SetCamera(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_GraphicalView view = board.GetState_MainGraphicalView()
                ?? throw new UserException("The board has no main graphical view.");

            view.GetState_Camera(out float lookAtX, out float lookAtY, out float lookAtZ,
                                 out float quatX, out float quatY, out float quatZ, out float quatW, out double zoom);
            string before = DescribeCamera(view);

            // Each value given as a parameter replaces the current one.
            lookAtX = Single(ctx, "LookAtX", lookAtX);
            lookAtY = Single(ctx, "LookAtY", lookAtY);
            lookAtZ = Single(ctx, "LookAtZ", lookAtZ);
            quatX = Single(ctx, "QuatX", quatX);
            quatY = Single(ctx, "QuatY", quatY);
            quatZ = Single(ctx, "QuatZ", quatZ);
            quatW = Single(ctx, "QuatW", quatW);
            zoom = Double(ctx, "Zoom", zoom);

            // IPCB_GraphicalView.Camera is a write-only indexed property: the seven floats are its index arguments
            // and the assigned value is the zoom.
            view.SetState_Camera(lookAtX, lookAtY, lookAtZ, quatX, quatY, quatZ, quatW, zoom);

            return $"Camera before: {before}\nCamera after:  {DescribeCamera(view)}";
        }

        /// <summary>
        /// SetBodyOpacity "Opacity=1": sets BodyOpacity3D of the first component body of the board and reports the
        /// value before and after. The documented range is 0 (transparent) to 1 (opaque).
        /// </summary>
        [HotCommand("SetBodyOpacity", "Set the 3D opacity of the first component body")]
        public static string SetBodyOpacity(CommandContext ctx)
        {
            string raw = ctx.Get("Opacity", "1");
            if (!float.TryParse(raw, NumberStyles.Float, Inv, out float opacity))
                throw new UserException($"Opacity '{raw}' is not a number. Pass Opacity=<0..1>, for example Opacity=0.5.");

            IPCB_Board board = CurrentBoard();
            IPCB_ComponentBody body = null;

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // Iterate all layers: which layer a body sits on is not
                // assumed here.
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                body = iterator.FirstPCBObject() as IPCB_ComponentBody;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            if (body == null)
                throw new UserException("The board has no component bodies. Open a board with 3D bodies.");

            float before = body.GetBodyOpacity3D();

            // Bracket the change like any other PCB change.
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            pcbServer.PreProcess();
            try
            {
                body.BeginModify();
                body.SetBodyOpacity3D(opacity);
                body.EndModify();
            }
            finally
            {
                pcbServer.PostProcess();
            }

            float after = body.GetBodyOpacity3D();
            return $"Component body {DescribeBody(body)}:\n" +
                   $"BodyOpacity3D before change : {F(before)}\n" +
                   $"BodyOpacity3D after change : {F(after)} (requested {F(opacity)})";
        }

        private static string DescribeCamera(IPCB_GraphicalView view)
        {
            bool ok = view.GetState_Camera(out float lookAtX, out float lookAtY, out float lookAtZ,
                                           out float quatX, out float quatY, out float quatZ, out float quatW,
                                           out double zoom);
            return $"LookAt=({F(lookAtX)}, {F(lookAtY)}, {F(lookAtZ)}) " +
                   $"Quat=({F(quatX)}, {F(quatY)}, {F(quatZ)}, {F(quatW)}) Zoom={zoom.ToString("R", Inv)}" +
                   (ok ? string.Empty : " (GetState_Camera returned false)");
        }

        private static string DescribeBody(IPCB_ComponentBody body)
        {
            IPCB_Component component = body.GetState_Component();
            string owner = component == null ? "free body" : "of " + component.GetState_Name()?.GetState_Text();
            return $"'{body.GetState_Identifier()}' ({owner})";
        }

        // "R" prints a float so that parsing it back gives the same value: BoardInfo3D output can be pasted into
        // SetCamera parameters.
        private static string F(float value) => value.ToString("R", Inv);

        private static float Single(CommandContext ctx, string name, float current)
        {
            string raw = ctx.Get(name);
            if (string.IsNullOrEmpty(raw)) return current;
            if (!float.TryParse(raw, NumberStyles.Float, Inv, out float value))
                throw new UserException($"{name} '{raw}' is not a number.");
            return value;
        }

        private static double Double(CommandContext ctx, string name, double current)
        {
            string raw = ctx.Get(name);
            if (string.IsNullOrEmpty(raw)) return current;
            if (!double.TryParse(raw, NumberStyles.Float, Inv, out double value))
                throw new UserException($"{name} '{raw}' is not a number.");
            return value;
        }

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
