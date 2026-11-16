using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DXP; // the IClient.OpenDocument/ShowDocument extension methods live in namespace DXP
using PCB;

namespace AdSamples.XSignalLengths
{
    /// <summary>
    /// Lists the xSignals (pin pairs) of each xSignal class of the current board with their node count and lengths.
    /// An xSignal class is an IPCB_ObjectClass whose MemberKind is eClassMemberKind_Signal; its members are the names
    /// of the pin pairs held by the board's IPCB_PinPairsManager.
    /// </summary>
    internal static class XSignalLengthsCommands
    {
        private const string DefaultTestClass = "XSignalLengths_Test";

        /// <summary>
        /// GetXSignalInfo "Out=&lt;file&gt;|Open=true": returns the report; Out also writes it to a file, Open shows the
        /// file in Altium Designer.
        /// </summary>
        [HotCommand("GetXSignalInfo", "Report the lengths of the xSignals in each xSignal class")]
        public static string GetXSignalInfo(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_PinPairsManager pinPairs = board.GetState_PinPairsManager()
                ?? throw new UserException("The board has no pin pair (xSignal) manager.");

            // Recalculates the signal path of every out-of-date pin pair. Reading Length does that on its own, but
            // NodeCount does not, so the call is needed.
            pinPairs.InvalidateAll();
            int pinPairCount = pinPairs.GetState_PinPairsCount();

            var report = new List<string>();
            int classCount = 0, rows = 0;

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // Classes are board-level objects. Like the docs' example for object classes, the filter uses all
                // layers, which cannot miss a class.
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eClassObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                // FirstPCBObject returns IPCB_Primitive: cast to IPCB_ObjectClass2 for DisplayName.
                for (var objectClass = iterator.FirstPCBObject() as IPCB_ObjectClass2;
                     objectClass != null;
                     objectClass = iterator.NextPCBObject() as IPCB_ObjectClass2)
                {
                    if (objectClass.GetState_MemberKind() != TClassMemberKind.eClassMemberKind_Signal)
                        continue;

                    classCount++;
                    report.Add("xSignal Class : " + objectClass.GetState_DisplayName());

                    // A class stores member names, so membership is tested by the pin pair's Name. A super class
                    // (<All xSignals>) answers true for every name.
                    for (int i = 0; i < pinPairCount; i++)
                    {
                        IPCB_PinPair pinPair = pinPairs.GetState_PinPairs(i);
                        if (pinPair == null || !objectClass.IsMember(pinPair.GetState_Name()))
                            continue;

                        rows++;
                        report.Add($"    xSignal : {pinPair.GetState_Name()}, Node Count : {pinPair.GetState_NodeCount()}, " +
                                   $"Signal Length : {Length(pinPair.GetState_Length())}, " +
                                   $"Routed Length : {Length(pinPair.GetState_RoutedLength())}, " +
                                   $"Unrouted Length : {Length(pinPair.GetState_UnroutedLength())}");
                    }
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            string boardName = Path.GetFileName(board.GetState_FileName());
            string summary = classCount == 0
                ? $"{boardName}: no xSignal classes ({pinPairCount} xSignals on the board)."
                : $"{boardName}: {classCount} xSignal classes, {rows} class members listed, {pinPairCount} xSignals on the board.";
            report.Insert(0, summary);

            string text = string.Join(Environment.NewLine, report);
            string written = WriteReport(ctx, boardName, report);
            return written == null ? text : text + Environment.NewLine + "Report written to " + written;
        }

        /// <summary>
        /// AddTestXSignal "Pins=&lt;U1-1|U2-3&gt;|Class=&lt;name&gt;": setup helper. Creates an xSignal
        /// between the given pins (default: two pads of the first net that connects two components) and adds it to an
        /// xSignal class, created if needed. Gives GetXSignalInfo something to report on boards without xSignals.
        /// </summary>
        [HotCommand("AddTestXSignal", "Create an xSignal in a test class (test setup)")]
        public static string AddTestXSignal(CommandContext ctx)
        {
            string className = ctx.Get("Class", DefaultTestClass);
            IPCB_Board board = CurrentBoard();
            IPCB_PinPairsManager pinPairs = board.GetState_PinPairsManager()
                ?? throw new UserException("The board has no pin pair (xSignal) manager.");

            // Pin descriptors are "<designator>-<pad name>" separated by '|'. In the Key=Value|Key=Value parameter
            // string '|' separates parameters, so the pins are written with ',' and converted here.
            string pins = ctx.Get("Pins");
            pins = string.IsNullOrWhiteSpace(pins) ? FindTwoConnectedPins(board) : pins.Replace(',', '|');

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            IPCB_PinPair pinPair;
            pcbServer.PreProcess();
            try
            {
                pinPair = pinPairs.CreateFromPinsDescriptors(pins)
                    ?? throw new UserException($"Could not create an xSignal from '{pins}'.");

                // AssignPinPairClass takes a DXP.IInterfaceList; the PCB contour utilities are the documented
                // source of an empty list.
                DXP.IInterfaceList list = pcbServer.PCBContourUtilities().CreateInterfaceList();
                list.Add(pinPair);
                // If this throws, the pin pair created above stays on the board without the test class. That is
                // acceptable for a test-setup command: close the document without saving to discard it.
                pinPairs.AssignPinPairClass(list, className);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            return $"Created xSignal '{pinPair.GetState_Name()}' between {pins.Replace('|', ',')} in class '{className}'. " +
                   "Close the document without saving to discard it.";
        }

        /// <summary>Two pads of the first net that has pads on two different components, as "U1-1|U2-3".</summary>
        private static string FindTwoConnectedPins(IPCB_Board board)
        {
            var firstPadByNet = new Dictionary<string, (string Descriptor, string Designator)>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                {
                    IPCB_Net net = pad.GetState_Net();
                    IPCB_Component component = pad.GetState_Component();
                    if (net == null || component == null)
                        continue;

                    string designator = component.GetState_SourceDesignator();
                    string descriptor = pad.GetState_PinDescriptorString();
                    if (!firstPadByNet.TryGetValue(net.GetState_Name(), out var first))
                        firstPadByNet[net.GetState_Name()] = (descriptor, designator);
                    else if (first.Designator != designator)
                        return first.Descriptor + "|" + descriptor;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            throw new UserException("No net connects pads of two components. Pass the pins: Pins=U1-1,U2-3.");
        }

        /// <summary>Writes the report when Out or Open is given; returns the path, or null.</summary>
        private static string WriteReport(CommandContext ctx, string boardName, List<string> report)
        {
            string path = ctx.Get("Out");
            bool open = ctx.Flag("Open");
            if (string.IsNullOrEmpty(path) && !open)
                return null;

            string fileName = "XSignalLengths_" + Path.GetFileNameWithoutExtension(boardName) + ".txt";
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), fileName);
            else if (Directory.Exists(path))
                path = Path.Combine(path, fileName);
            path = Path.GetFullPath(path);
            File.WriteAllLines(path, report);

            if (open)
            {
                DXP.IClient client = DXP.GlobalVars.Client;
                client.ShowDocument(client.OpenDocument("Text", path));
            }
            return path;
        }

        /// <summary>
        /// Pin pair lengths are long values in internal units (10,000 per mil, EDP.EDPConstant.InternalUnits); -1 means the pin pair has no signal
        /// path. The raw value is kept next to the mils so the unit can be checked.
        /// </summary>
        private static string Length(long value) =>
            value < 0
                ? $"{value} (no signal path)"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.###} mil ({1})", value / (double)EDP.EDPConstant.InternalUnits, value);

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
