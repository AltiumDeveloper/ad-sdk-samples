using System;
using System.Text;
using PCB;

namespace AdSamples.HidePcbLayer
{
    /// <summary>
    /// Hides or shows one electrical layer of the current board's view, and lists the display state of each layer.
    /// The SDK lesson: walk the V7 layer stack (FirstLayer / NextLayer), identify a layer by its TV6_Layer ID, and
    /// change IPCB_LayerObject_V7.IsDisplayed, which takes the board whose view it reads or changes.
    /// </summary>
    internal static class HidePcbLayerCommands
    {
        /// <summary>
        /// HideLayer "Layer=&lt;name&gt;|Show=true": hides the layer (default: the top layer, TV6_Layer.eV6_TopLayer),
        /// or shows it again with Show=true.
        /// </summary>
        [HotCommand("HideLayer", "Hide (or show) a layer of the board view")]
        public static string HideLayer(CommandContext ctx)
        {
            string layerParam = ctx.Get("Layer", "");
            bool show = ctx.Flag("Show");

            IPCB_Board board = CurrentBoard();
            IPCB_LayerStack_V7 stack = board.GetState_LayerStack_V7()
                ?? throw new UserException("The board has no layer stack.");

            IPCB_LayerObject_V7 layer = FindLayer(stack, layerParam)
                ?? throw new UserException(string.IsNullOrWhiteSpace(layerParam)
                    ? "The layer stack has no top layer."
                    : $"No electrical layer '{layerParam}' in the layer stack. Pass a layer name as ListLayerDisplay "
                      + "shows it (for example 'Top Layer'), or a TV6_Layer name (for example eV6_BottomLayer).");

            bool before = layer.GetState_IsDisplayed(board);
            // IsDisplayed is an indexed property in the SDK: the board is the index, because the display state
            // belongs to the board's view, not to the layer object.
            layer.SetState_IsDisplayed(board, show);
            // Redraw the view so the change shows.
            board.Update_PCBGraphicalView(false, true);
            bool after = layer.GetState_IsDisplayed(board);

            return $"{Describe(layer)}: {(after ? "displayed" : "hidden")} (was {(before ? "displayed" : "hidden")}).";
        }

        /// <summary>ListLayerDisplay: one line per electrical layer of the stack with its display state.</summary>
        [HotCommand("ListLayerDisplay", "List the layers of the stack and whether each is displayed")]
        public static string ListLayerDisplay(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_LayerStack_V7 stack = board.GetState_LayerStack_V7()
                ?? throw new UserException("The board has no layer stack.");

            var sb = new StringBuilder();
            int count = 0, hidden = 0;
            // The V7 walk visits the electrical layers only (signal layers and internal planes).
            for (IPCB_LayerObject_V7 layer = stack.FirstLayer(); layer != null; layer = stack.NextLayer(layer))
            {
                bool displayed = layer.GetState_IsDisplayed(board);
                count++;
                if (!displayed) hidden++;
                sb.AppendLine($"  {Describe(layer)}: {(displayed ? "displayed" : "hidden")}");
            }

            string file = System.IO.Path.GetFileName(board.GetState_FileName());
            return $"{file}: {count} electrical layers, {hidden} hidden" + Environment.NewLine + sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Finds the layer to change. Empty: the layer whose ID is eV6_TopLayer (the .NET V7_LayerBase has no
        /// IsTopLayer test). Otherwise the layer's name, then a TV6_Layer name
        /// ("eV6_BottomLayer", or "Bottom Layer" without the prefix and spaces).
        /// </summary>
        private static IPCB_LayerObject_V7 FindLayer(IPCB_LayerStack_V7 stack, string layerParam)
        {
            TV6_Layer? wantedId = null;
            string wantedName = layerParam?.Trim() ?? "";
            if (wantedName.Length == 0)
                wantedId = TV6_Layer.eV6_TopLayer;
            else if (TryParseLayerId(wantedName, out TV6_Layer parsed))
                wantedId = parsed;

            // Names win over IDs: a user may have named a layer like another layer's default name.
            if (wantedName.Length > 0)
            {
                for (IPCB_LayerObject_V7 layer = stack.FirstLayer(); layer != null; layer = stack.NextLayer(layer))
                    if (string.Equals(layer.GetState_LayerName(), wantedName, StringComparison.OrdinalIgnoreCase))
                        return layer;
            }
            if (wantedId == null)
                return null;
            for (IPCB_LayerObject_V7 layer = stack.FirstLayer(); layer != null; layer = stack.NextLayer(layer))
                if (layer.LayerId() == wantedId.Value)
                    return layer;
            return null;
        }

        private static bool TryParseLayerId(string text, out TV6_Layer id)
        {
            string compact = text.Replace(" ", "");
            if (!compact.StartsWith("eV6_", StringComparison.OrdinalIgnoreCase))
                compact = "eV6_" + compact;
            // Reject plain numbers: Enum.TryParse would accept any integer.
            return Enum.TryParse(compact, true, out id) && Enum.IsDefined(typeof(TV6_Layer), id);
        }

        private static string Describe(IPCB_LayerObject_V7 layer) => $"{layer.GetState_LayerName()} ({layer.LayerId()})";

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
