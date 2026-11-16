using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PCB;

namespace AdSamples.ComponentMoveUndoGroup
{
    /// <summary>
    /// Changes every component of the current board in one batch and groups the batch so that Altium Designer can
    /// undo it as one step. Two grouping routes are offered: the PCB process messages
    /// (IPCB_Board.DispatchMessage with ProcessStart / ProcessEnd / YieldToRobots), and the documented
    /// IPCB_ServerInterface.PreProcess / PostProcess bracket.
    /// </summary>
    internal static class ComponentMoveUndoGroupCommands
    {
        // Altium.SDK has no PCB message constants, so the numeric IDs are declared here:
        // 7 / 8 bracket the batch into a single undo step, and 10 is accepted without error.
        private const ushort PCBM_ProcessStart = 7;
        private const ushort PCBM_ProcessEnd = 8;
        private const ushort PCBM_YieldToRobots = 10;

        // The PCB editor's own Undo process (Edit > Undo). Used only by the test-support command UndoLast.
        private const string UndoProcess = "PCB:Undo";

        /// <summary>
        /// MoveComponents "Factor=2|Grouping=Messages|PreProcess": multiplies each component's X location by Factor
        /// (default 2). Nothing is changed when any new X would leave the coordinate range.
        /// </summary>
        [HotCommand("MoveComponents", "Multiply every component's X location as one undo step")]
        public static string MoveComponents(CommandContext ctx)
        {
            double factor = ParseFactor(ctx.Get("Factor", "2"));
            string grouping = ctx.Get("Grouping", "Messages");
            bool useMessages;
            if (string.Equals(grouping, "Messages", StringComparison.OrdinalIgnoreCase)) useMessages = true;
            else if (string.Equals(grouping, "PreProcess", StringComparison.OrdinalIgnoreCase)) useMessages = false;
            else throw new UserException($"Unknown Grouping '{grouping}'. Use Grouping=Messages or Grouping=PreProcess.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            // Collect first, change afterwards: the board iterator is not walked while the objects it visits move.
            List<IPCB_Component> components = Components(board);
            if (components.Count == 0)
                throw new UserException("The board has no components.");

            // Check the whole batch before touching anything, so a bad Factor leaves the board unchanged.
            var newX = new int[components.Count];
            int outOfRange = 0;
            for (int i = 0; i < components.Count; i++)
            {
                double x = Math.Round(components[i].GetState_XLocation() * factor);
                if (x < PCBConstant.MinCoord || x > PCBConstant.MaxCoord)
                    outOfRange++;
                else
                    newX[i] = (int)x;
            }
            if (outOfRange > 0)
                throw new UserException($"Factor={Format(factor)} would move {outOfRange} of {components.Count} components outside " +
                                        $"the coordinate range ({PCBConstant.MinCoord}..{PCBConstant.MaxCoord}). Nothing changed.");

            string before = Summary(board, components, 0);

            // Open the group. Messages: the PCB process messages (no PreProcess). PreProcess: the bracket the docs'
            // "Changing objects" section shows; PreProcess starts an undo step.
            if (useMessages)
                board.DispatchMessage(null, null, PCBM_ProcessStart, null);
            else
                pcbServer.PreProcess();

            int moved = 0;
            try
            {
                for (int i = 0; i < components.Count; i++)
                {
                    ctx.Log($"{i + 1}/{components.Count} {components[i].GetState_SourceDesignator()}");
                    // BeginModify saves the state for undo; EndModify completes the change and updates connectivity.
                    components[i].BeginModify();
                    components[i].SetState_XLocation(newX[i]);
                    components[i].EndModify();
                    moved++;
                }
            }
            finally
            {
                // Close the group even when a change fails.
                if (useMessages)
                {
                    board.DispatchMessage(null, null, PCBM_ProcessEnd, null);
                    board.DispatchMessage(null, null, PCBM_YieldToRobots, null);
                }
                else
                {
                    pcbServer.PostProcess();
                }
            }

            return $"Moved {moved} components: X x {Format(factor)}, Grouping={(useMessages ? "Messages" : "PreProcess")}." +
                   Environment.NewLine + "Before: " + before +
                   Environment.NewLine + "After:  " + Summary(board, Components(board), 0) +
                   Environment.NewLine + "One Undo (Ctrl+Z) should restore all of them.";
        }

        /// <summary>
        /// ComponentPositions "List=5": read-back for the test. The component count, the sum of all X locations
        /// (internal units, so before/after/undo can be compared exactly), the X range, and the first List components.
        /// </summary>
        [HotCommand("ComponentPositions", "Summarise the components' X locations")]
        public static string ComponentPositions(CommandContext ctx)
        {
            if (!int.TryParse(ctx.Get("List", "5"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int list) || list < 0)
                throw new UserException("List must be a whole number >= 0.");
            IPCB_Board board = CurrentBoard();
            return Summary(board, Components(board), list);
        }

        /// <summary>
        /// UndoLast: helper. Runs the PCB editor's Undo process once, then returns the
        /// same summary as ComponentPositions, so a test can check through the pipe what one Undo reverted.
        /// </summary>
        [HotCommand("UndoLast", "Undo the last PCB edit (test support)")]
        public static string UndoLast(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            // RunCommand returns nothing, so the board is read again to report what the Undo did.
            DXP.Utils.RunCommand(UndoProcess, "");
            return $"Ran {UndoProcess} once. Now: " + Summary(board, Components(board), 0);
        }

        private static List<IPCB_Component> Components(IPCB_Board board)
        {
            var components = new List<IPCB_Component>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                // FirstPCBObject returns IPCB_Primitive: a type check instead of an unchecked cast.
                for (IPCB_Primitive primitive = iterator.FirstPCBObject(); primitive != null; primitive = iterator.NextPCBObject())
                {
                    if (primitive is IPCB_Component component)
                        components.Add(component);
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return components;
        }

        private static string Summary(IPCB_Board board, List<IPCB_Component> components, int list)
        {
            string name = Path.GetFileName(board.GetState_FileName());
            if (components.Count == 0)
                return $"{name}: no components.";

            long sum = 0;
            int min = int.MaxValue, max = int.MinValue;
            foreach (IPCB_Component component in components)
            {
                int x = component.GetState_XLocation();
                sum += x;
                min = Math.Min(min, x);
                max = Math.Max(max, x);
            }

            string text = $"{name}: {components.Count} components, sum X = {sum}, " +
                          $"X from {Mils(min)} to {Mils(max)}";
            if (list > 0)
                text += "; " + string.Join(", ", components.Take(list)
                    .Select(c => $"{c.GetState_SourceDesignator()} X={Mils(c.GetState_XLocation())}"));
            return text + ".";
        }

        private static double ParseFactor(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) ||
                double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
                throw new UserException($"Factor '{text}' is not a positive number (use a dot as decimal separator).");
            return factor;
        }

        // Coordinates are internal units; EDP.Utils.CoordToMils converts them for display.
        private static string Mils(int coord) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###} mil", EDP.Utils.CoordToMils(coord));

        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
