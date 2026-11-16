using System;
using System.Collections.Generic;
using System.IO;
using PCB;

namespace AdSamples.MechanicalLayersEnableAll
{
    /// <summary>
    /// Switches every mechanical layer of the current board on (or off) through IPCB_MechanicalLayer.MechLayerEnabled,
    /// and lists the layers so the change can be checked.
    ///
    /// The SDK lesson: IPCB_Board.MechanicalLayerIterator visits only the mechanical
    /// layers that are *currently enabled*. It is therefore fine for disabling layers, but a loop built on it can never
    /// enable one, because a disabled layer is not among the objects it returns. To reach every mechanical layer,
    /// including the disabled ones, ask the layer stack for the layer object of each mechanical layer id
    /// (IPCB_LayerStack.LayerObject, as the SDK's IPCB_MechanicalLayer example does).
    /// </summary>
    internal static class MechanicalLayersEnableAllCommands
    {
        /// <summary>
        /// EnableAllMechanicalLayers "Enabled=false": sets MechLayerEnabled on every mechanical layer of the board.
        /// Enabled defaults to true.
        /// </summary>
        [HotCommand("EnableAllMechanicalLayers", "Enable (or disable) every mechanical layer of the board")]
        public static string EnableAllMechanicalLayers(CommandContext ctx)
        {
            bool enable = ParseEnabled(ctx.Get("Enabled"));
            IPCB_Board board = CurrentBoard();

            int visited = 0, changed = 0;
            var failures = new List<string>();

            foreach (IPCB_MechanicalLayer layer in MechanicalLayers(board))
            {
                visited++;
                try
                {
                    if (layer.GetState_MechLayerEnabled() == enable)
                        continue;
                    layer.SetState_MechLayerEnabled(enable);
                    changed++;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    failures.Add($"#{visited}: {ex.Message}");
                }
            }

            // Refresh the layer tabs so newly enabled layers appear without reopening the document.
            board.ViewManager_UpdateLayerTabs();

            string boardName = Path.GetFileName(board.GetState_FileName());
            string result = $"{boardName}: {changed} of {visited} mechanical layers {(enable ? "enabled" : "disabled")} " +
                            $"({visited - changed - failures.Count} already {(enable ? "enabled" : "disabled")}).";
            if (failures.Count > 0)
                result += Environment.NewLine + "Failed: " + string.Join("; ", failures);
            return result;
        }

        /// <summary>ListMechanicalLayers: read-only, one line per mechanical layer of the board.</summary>
        [HotCommand("ListMechanicalLayers", "List the mechanical layers and whether each is enabled")]
        public static string ListMechanicalLayers(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            var lines = new List<string>();
            int visited = 0, enabled = 0;

            foreach (IPCB_MechanicalLayer layer in MechanicalLayers(board))
            {
                visited++;
                bool isEnabled = layer.GetState_MechLayerEnabled();
                if (isEnabled)
                    enabled++;
                lines.Add($"  #{visited}: {layer.GetState_LayerName()} - {(isEnabled ? "enabled" : "disabled")}");
            }

            string boardName = Path.GetFileName(board.GetState_FileName());
            lines.Insert(0, $"{boardName}: {visited} mechanical layers, {enabled} enabled.");
            lines.Add($"  (MechanicalLayerIterator visits {CountFromIterator(board)} of them: only the enabled ones.)");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Every mechanical layer of the board, enabled or not: the layer stack's layer object for each id in the
        /// mechanical range. Layers the stack does not know are skipped.
        /// </summary>
        private static IEnumerable<IPCB_MechanicalLayer> MechanicalLayers(IPCB_Board board)
        {
            IPCB_LayerStack stack = board.GetState_LayerStack()
                ?? throw new UserException("The board returned no layer stack.");
            for (TV6_Layer id = TV6_Layer.eV6_Mechanical1; id <= TV6_Layer.eV6_Mechanical16; id++)
            {
                // LayerObject is typed IPCB_LayerObject; MechLayerEnabled lives on the derived IPCB_MechanicalLayer,
                // so the result is narrowed with a type pattern and anything that is not a mechanical layer
                // object (or a null the stack returns for an unknown id) is skipped.
                if (stack.LayerObject(id) is IPCB_MechanicalLayer layer)
                    yield return layer;
            }
        }

        /// <summary>
        /// The iterator technique, kept to show what it does: how many mechanical layers the board's mechanical
        /// layer iterator returns. Layer object iterators have no Destroy method (unlike BoardIterator/SpatialIterator).
        /// </summary>
        private static int CountFromIterator(IPCB_Board board)
        {
            IPCB_LayerObjectIterator iterator = board.MechanicalLayerIterator();
            if (iterator == null)
                return 0;
            int count = 0;
            for (bool more = iterator.First(); more; more = iterator.Next())
                count++;
            return count;
        }

        private static bool ParseEnabled(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            if (bool.TryParse(value, out bool parsed))
                return parsed;
            if (value == "1")
                return true;
            if (value == "0")
                return false;
            throw new UserException($"Enabled must be true or false, not '{value}'.");
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
