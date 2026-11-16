using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.CreateXSignal
{
    /// <summary>
    /// Creates an xSignal (pin pair) between two component pins with IPCB_PinPairsManager.CreateFromPinsDescriptors.
    /// ListXSignals and DeleteXSignal are helpers, so the result can be checked and removed again.
    /// </summary>
    internal static class CreateXSignalCommands
    {
        // The default pins 'U1-16|U4-23', used by the menu item;
        // on most boards those pins don't exist, so pass Pins=.
        private const string DefaultPins = "U1-16,U4-23";

        /// <summary>CreateXSignal "Pins=U1-16,U4-23": creates the pin pair and reports its name and node count.</summary>
        [HotCommand("CreateXSignal", "Create an xSignal between two pins")]
        public static string CreateXSignal(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_PinPairsManager manager = Manager(board);

            // Pin descriptors are "<designator>-<pad name>" separated by '|'. In the Key=Value|Key=Value parameter
            // string '|' separates parameters, so the pins are written with ',' and converted here.
            string pins = ctx.Get("Pins", DefaultPins);
            if (string.IsNullOrWhiteSpace(pins))
                throw new UserException("Pass the pins: Pins=U1-16,U4-23.");
            string descriptors = pins.Replace(',', '|');

            int before = manager.GetState_PinPairsCount();
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            IPCB_PinPair pinPair;
            pcbServer.PreProcess();
            try
            {
                // Documented to return null for an empty descriptor. In practice, designators that are
                // not on the board still give a pin pair ('_PP1', no signal path), and the same pins twice give two.
                pinPair = manager.CreateFromPinsDescriptors(descriptors);

                // A new pin pair is out of date until its signal path is calculated, and
                // NodeCount does not recalculate it on its own.
                manager.InvalidateAll();
            }
            finally
            {
                pcbServer.PostProcess();
            }

            int after = manager.GetState_PinPairsCount();
            if (pinPair == null)
                throw new UserException($"Could not create an xSignal from '{pins}' (CreateFromPinsDescriptors returned null; {after} xSignals on the board).");

            return $"Created xSignal '{pinPair.GetState_Name()}' between {pins}, Node Count : {pinPair.GetState_NodeCount()}, " +
                   $"xSignals on the board: {before} -> {after}. Close the document without saving to discard it.";
        }

        /// <summary>ListXSignals: one line per pin pair of the board with its node count and length.</summary>
        [HotCommand("ListXSignals", "List the board's xSignals")]
        public static string ListXSignals(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_PinPairsManager manager = Manager(board);
            manager.InvalidateAll();

            int count = manager.GetState_PinPairsCount();
            var lines = new List<string> { $"{Path.GetFileName(board.GetState_FileName())}: {count} xSignals." };
            for (int i = 0; i < count; i++)
            {
                IPCB_PinPair pinPair = manager.GetState_PinPairs(i);
                if (pinPair == null)
                    continue;
                lines.Add($"    {pinPair.GetState_Name()}, Node Count : {pinPair.GetState_NodeCount()}, Length : {Length(pinPair.GetState_Length())}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>DeleteXSignal "Name=CCLK_PP1": removes the pin pair with that name (exact, case-sensitive match).</summary>
        [HotCommand("DeleteXSignal", "Delete an xSignal by name")]
        public static string DeleteXSignal(CommandContext ctx)
        {
            string name = ctx.Get("Name");
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("Pass the xSignal name: Name=<name> (see ListXSignals).");

            IPCB_Board board = CurrentBoard();
            IPCB_PinPairsManager manager = Manager(board);

            IPCB_PinPair target = null;
            int before = manager.GetState_PinPairsCount();
            for (int i = 0; i < before && target == null; i++)
            {
                IPCB_PinPair pinPair = manager.GetState_PinPairs(i);
                if (pinPair != null && string.Equals(pinPair.GetState_Name(), name, StringComparison.Ordinal))
                    target = pinPair;
            }
            if (target == null)
                throw new UserException($"No xSignal named '{name}' on the board ({before} xSignals).");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            pcbServer.PreProcess();
            try
            {
                manager.Delete(target);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            return $"Deleted xSignal '{name}'. xSignals on the board: {before} -> {manager.GetState_PinPairsCount()}.";
        }

        /// <summary>Pin pair lengths are long values in internal units (10,000 per mil); -1 means no signal path.</summary>
        private static string Length(long value) =>
            value < 0
                ? $"{value} (no signal path)"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.###} mil ({1})", value / (double)EDP.EDPConstant.InternalUnits, value);

        private static IPCB_PinPairsManager Manager(IPCB_Board board) =>
            board.GetState_PinPairsManager()
            ?? throw new UserException("The board has no pin pair (xSignal) manager.");

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
