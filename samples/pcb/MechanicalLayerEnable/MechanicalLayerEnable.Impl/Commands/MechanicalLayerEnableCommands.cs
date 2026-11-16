using System;
using System.IO;
using PCB;

namespace AdSamples.MechanicalLayerEnable
{
    /// <summary>
    /// Enables a mechanical layer of the current board. It makes the board-level calls (get the layer
    /// from the master layer stack, mark it used and displayed, make it current) and adds the call that really
    /// enables a mechanical layer, IPCB_MechanicalLayer.SetState_MechLayerEnabled.
    /// </summary>
    internal static class MechanicalLayerEnableCommands
    {
        /// <summary>
        /// SetLayerVisible "Layer=3|Current=V7|Enable=True": enables mechanical layer n (1..16) and makes it current.
        /// Enable=False makes only the board-level calls.
        /// </summary>
        [HotCommand("SetLayerVisible", "Enable a mechanical layer and make it current")]
        public static string SetLayerVisible(CommandContext ctx)
        {
            TV6_Layer v6 = MechanicalLayerFromParameter(ctx);
            string current = (ctx.Get("Current", "V7") ?? "V7").Trim();
            if (!current.Equals("V7", StringComparison.OrdinalIgnoreCase)
                && !current.Equals("Layer", StringComparison.OrdinalIgnoreCase)
                && !current.Equals("None", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Current must be V7, Layer or None, not '{current}'.");
            string enableText = (ctx.Get("Enable", "True") ?? "True").Trim();
            if (!bool.TryParse(enableText, out bool enable))
                throw new UserException($"Enable must be True or False, not '{enableText}'.");

            IPCB_ServerInterface pcbServer = PCBServer();
            IPCB_Board board = CurrentBoard(pcbServer);
            var layer = new V7_Layer(v6);

            string before = Describe(board, v6, layer);
            string created;
            string enabled;

            pcbServer.PreProcess();
            try
            {
                // In .NET the master layer stack is IPCB_Board.MasterStack. For a mechanical layer CreateLayer
                // returns the board's existing IPCB_MechanicalLayer (IsInLayerStack = False) and does not enable a
                // disabled one.
                IPCB_MasterLayerStack masterStack = board.GetState_MasterStack()
                    ?? throw new UserException("The board has no master layer stack.");
                IPCB_LayerObject layerObject = masterStack.CreateLayer(layer);
                created = layerObject == null
                    ? "CreateLayer returned no layer object"
                    : $"CreateLayer returned {(layerObject is IPCB_MechanicalLayer ? "an IPCB_MechanicalLayer" : "an IPCB_LayerObject")}"
                      + $" '{layerObject.GetState_LayerName()}', IsInLayerStack = {layerObject.IsInLayerStack()}";

                // A mechanical layer is enabled through its layer object, not through the board's LayerIsUsed
                // (LayerIsUsed becomes True, MechLayerEnabled stays False).
                if (!enable)
                    enabled = "MechLayerEnabled not set (Enable=False: LayerIsUsed only)";
                else if (layerObject is IPCB_MechanicalLayer mechanical)
                {
                    mechanical.SetState_MechLayerEnabled(true);
                    enabled = "SetState_MechLayerEnabled(true) called";
                }
                else
                    enabled = "MechLayerEnabled not set: CreateLayer returned no IPCB_MechanicalLayer";

                // The two board properties are indexed by a V7_LayerBase, not by a TV6_Layer.
                board.SetState_LayerIsUsed(layer, true);
                board.SetState_LayerIsDisplayed(layer, true);

                // Making the layer current. The reference documents CurrentLayerV7 as the setter of the current layer
                // (IPCB_Board.CurrentLayer is read-only). Current=Layer runs SetState_Layer instead, which the board
                // inherits from IPCB_Primitive; SetState_Layer does not change the current layer.
                if (current.Equals("V7", StringComparison.OrdinalIgnoreCase))
                    board.SetState_CurrentLayerV7(layer);
                else if (current.Equals("Layer", StringComparison.OrdinalIgnoreCase))
                    board.SetState_Layer(v6);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            // Redraw, so a layer that became displayed shows up in the view.
            board.ViewManager_FullUpdate();

            string after = Describe(board, v6, layer);
            string how = current.Equals("V7", StringComparison.OrdinalIgnoreCase) ? "SetState_CurrentLayerV7"
                : current.Equals("Layer", StringComparison.OrdinalIgnoreCase) ? "SetState_Layer"
                : "nothing (Current=None)";
            return $"{Path.GetFileName(board.GetState_FileName())}: {v6}, current layer set with {how}.\r\n"
                   + $"{created}.\r\n"
                   + $"{enabled}.\r\n"
                   + $"Before: {before}\r\n"
                   + $"After:  {after}";
        }

        /// <summary>GetLayerState "Layer=3": reports the state of mechanical layer n without changing anything.</summary>
        [HotCommand("GetLayerState", "Show the state of a mechanical layer")]
        public static string GetLayerState(CommandContext ctx)
        {
            TV6_Layer v6 = MechanicalLayerFromParameter(ctx);
            IPCB_Board board = CurrentBoard(PCBServer());
            return $"{Path.GetFileName(board.GetState_FileName())}: {v6}: {Describe(board, v6, new V7_Layer(v6))}";
        }

        /// <summary>One line with everything the command touches, read back from the board.</summary>
        private static string Describe(IPCB_Board board, TV6_Layer v6, V7_Layer layer)
        {
            // Mechanical layers are not in the layer stack, but IPCB_LayerStack.LayerObject also returns their
            // layer object, an IPCB_MechanicalLayer, which carries the layer's "enabled" flag.
            string enabled = "n/a";
            IPCB_LayerStack stack = board.GetState_LayerStack();
            if (stack?.LayerObject(v6) is IPCB_MechanicalLayer mech)
                enabled = mech.GetState_MechLayerEnabled().ToString();

            return $"Name = '{board.LayerName(layer)}', MechLayerEnabled = {enabled}, "
                   + $"LayerIsUsed = {board.GetState_LayerIsUsed(layer)}, "
                   + $"LayerIsDisplayed = {board.GetState_LayerIsDisplayed(layer)}, "
                   + $"CurrentLayer = {board.GetState_CurrentLayer()}";
        }

        /// <summary>
        /// Layer=n, 1..16 (default 3, eV6_Mechanical3). TV6_Layer has only 16 mechanical values,
        /// numbered consecutively from eV6_Mechanical1.
        /// </summary>
        private static TV6_Layer MechanicalLayerFromParameter(CommandContext ctx)
        {
            string text = ctx.Get("Layer", "3");
            if (!int.TryParse(text, out int n) || n < 1 || n > 16)
                throw new UserException($"Layer must be a mechanical layer number from 1 to 16, not '{text}'.");
            return TV6_Layer.eV6_Mechanical1 + (n - 1);
        }

        private static IPCB_ServerInterface PCBServer() =>
            PCB.GlobalVars.PCBServer
            ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");

        private static IPCB_Board CurrentBoard(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetCurrentPCBBoard()
            ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
    }
}
