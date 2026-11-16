using System;
using System.IO;
using PCB;

namespace AdSamples.HighlightPcbLayer
{
    /// <summary>
    /// Highlights one PCB layer: turns on single-layer mode (all other layers dimmed or hidden) and makes the layer the
    /// current layer of the board's view, then redraws the view. This changes view state only, not the design.
    /// </summary>
    internal static class HighlightPcbLayerCommands
    {
        /// <summary>
        /// HighlightLayer "Layer=&lt;name&gt;|Via=Process|Property|Off=true".
        /// Layer: a TV6_Layer name (TopLayer, eV6_BottomLayer, Mid Layer 1) or the layer's name on this board;
        /// default Top Layer. Via: how single-layer mode is switched, the
        /// IPCB_SystemOptions.SingleLayerMode property (default) or the PCB:SetupPreferences process call, which has
        /// no effect. Off: turn single-layer mode off instead and leave the current layer
        /// unchanged.
        /// </summary>
        [HotCommand("HighlightLayer", "Turn on single-layer mode and make a layer current")]
        public static string HighlightLayer(CommandContext ctx)
        {
            bool off = ctx.Flag("Off");
            bool viaProperty = ParseVia(ctx.Get("Via", "Property"));

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            // Resolve the layer before changing anything, so a bad name leaves the view as it was.
            TV6_Layer layer = off ? TV6_Layer.eV6_NoLayer : ResolveLayer(board, ctx.Get("Layer", "TopLayer"));

            SetSingleLayerMode(pcbServer, !off, viaProperty);

            if (!off)
            {
                // In .NET IPCB_Board.CurrentLayer is read-only; the layer is set with
                // the V7 setter. It has no effect when the board has no view (docs: PCB basics).
                board.SetState_CurrentLayerV7(new V7_Layer(layer));
            }

            // No rebuild of the graphics, redraw the view.
            board.Update_PCBGraphicalView(false, true);

            // Read back what AD reports, so the result can be checked without looking at the screen.
            bool singleLayerMode = pcbServer.GetState_SystemOptions().GetState_SingleLayerMode();
            V7_LayerBase current = board.GetState_CurrentLayerV7();
            string currentText = current == null ? "none" : $"{board.LayerName(current)} ({board.GetState_CurrentLayer()})";
            string route = viaProperty ? "IPCB_SystemOptions.SingleLayerMode" : "process PCB:SetupPreferences";
            string boardName = Path.GetFileName(board.GetState_FileName());

            if (!off && (current == null || current.GetID() != new V7_Layer(layer).GetID()))
            {
                throw new UserException(
                    $"{boardName}: the current layer did not change to {layer} (it reads {currentText}). " +
                    "Setting the current layer only works on a board open in a PCB editor view, and only for a layer of " +
                    "the board's layer stack.");
            }

            // The state is read back, so a route that silently did nothing is visible in the result rather than
            // assumed to have worked (the process route never changes the mode).
            string mismatch = singleLayerMode == !off
                ? string.Empty
                : $" WARNING: {route} did not change the mode (asked for {(off ? "OFF" : "ON")}).";

            return $"{boardName}: single-layer mode {(singleLayerMode ? "ON" : "OFF")} (set {(off ? "OFF" : "ON")} via {route}), " +
                   $"current layer {currentText}.{mismatch}";
        }

        /// <summary>
        /// The process call RunCommand('PCB:SetupPreferences', 'SingleLayerMode=ON') returns without error but
        /// leaves IPCB_SystemOptions.SingleLayerMode unchanged, so the property is the default route and
        /// Via=Process is kept only to let the two be compared.
        /// </summary>
        private static void SetSingleLayerMode(IPCB_ServerInterface pcbServer, bool on, bool viaProperty)
        {
            if (viaProperty)
                pcbServer.GetState_SystemOptions().SetState_SingleLayerMode(on);
            else
                DXP.Utils.RunCommand("PCB:SetupPreferences", on ? "SingleLayerMode=ON" : "SingleLayerMode=OFF");
        }

        private static bool ParseVia(string via)
        {
            if (string.Equals(via, "Property", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(via, "Process", StringComparison.OrdinalIgnoreCase))
                return false;
            throw new UserException($"Unknown Via '{via}'. Use Via=Process or Via=Property.");
        }

        /// <summary>
        /// A TV6_Layer from a name: the enum name with or without the eV6_ prefix and spaces ("Top Layer", "TopLayer",
        /// "eV6_TopLayer"), or else the name the board shows for the layer (IPCB_Board.LayerName), which also finds
        /// layers the user renamed in the layer stack.
        /// </summary>
        private static TV6_Layer ResolveLayer(IPCB_Board board, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("Empty Layer. Pass a layer name such as Layer=TopLayer, or leave Layer out for Top Layer.");
            string key = name.Replace(" ", "");
            if (!key.StartsWith("eV6_", StringComparison.OrdinalIgnoreCase))
                key = "eV6_" + key;
            // Enum.TryParse also accepts numbers, so only defined names count.
            if (Enum.TryParse(key, true, out TV6_Layer parsed) && Enum.IsDefined(typeof(TV6_Layer), parsed)
                && !char.IsDigit(name.Trim()[0]))
            {
                // eV6_NoLayer is a valid enum name but not a layer that can be made current.
                if (parsed == TV6_Layer.eV6_NoLayer)
                    throw new UserException("Layer=NoLayer is not a layer. Pass a layer name such as Layer=TopLayer.");
                return parsed;
            }

            foreach (TV6_Layer candidate in Enum.GetValues(typeof(TV6_Layer)))
            {
                if (candidate == TV6_Layer.eV6_NoLayer)
                    continue;
                if (string.Equals(board.LayerName(new V7_Layer(candidate)), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            throw new UserException($"Unknown layer '{name}'. Pass a layer name such as Layer=TopLayer or Layer=Bottom Layer.");
        }
    }
}
