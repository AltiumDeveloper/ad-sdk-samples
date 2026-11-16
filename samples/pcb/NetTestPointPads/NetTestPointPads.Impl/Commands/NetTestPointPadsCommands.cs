using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.NetTestPointPads
{
    /// <summary>
    /// Creates one pad per net with PCBObjectFactory, flags it as a bottom test point, assigns it to the net and adds
    /// it to the board. CountTestPointPads reads the result back.
    /// </summary>
    internal static class NetTestPointPadsCommands
    {
        /// <summary>
        /// CreateTestPoint "OffsetMm=10|PitchMm=5|RowMm=10". For every net: a pad at the midpoint of the net's first
        /// bottom-layer track (pad on the bottom layer); else at the midpoint of its first track (pad on the
        /// multi-layer); else in a grid above the board outline, starting OffsetMm above its top edge, PitchMm apart,
        /// wrapping to a new row RowMm higher at the outline's right edge. Not saved.
        /// </summary>
        [HotCommand("CreateTestPoint", "Add a bottom test-point pad to every net")]
        public static string CreateTestPoint(CommandContext ctx)
        {
            int offset = EDP.Utils.MMsToCoord(Millimetres(ctx, "OffsetMm", 10));
            int pitch = EDP.Utils.MMsToCoord(Millimetres(ctx, "PitchMm", 5));
            int rowStep = EDP.Utils.MMsToCoord(Millimetres(ctx, "RowMm", 10));

            IPCB_ServerInterface pcbServer = PcbServer();
            IPCB_Board board = CurrentBoard(pcbServer);
            IPCB_BoardOutline outline = board.GetState_BoardOutline()
                ?? throw new UserException("The board has no board outline.");
            CoordRect rect = outline.BoundingRectangle();

            // Collect the nets first, then add pads: adding to the board while the net iterator is still running is
            // not documented as safe.
            List<IPCB_Net> nets = CollectNets(board);
            if (nets.Count == 0)
                throw new UserException("The board has no nets.");

            int x = rect.Lx;
            int y = rect.Hy + offset;
            int onBottomTrack = 0, onFirstTrack = 0, inGrid = 0;
            IPCB_Pad firstPad = null;

            pcbServer.PreProcess();
            try
            {
                foreach (IPCB_Net net in nets)
                {
                    // Size, shape and hole are left at the factory defaults.
                    // No BeginModify/EndModify around the property calls below: the pad is not on the board yet,
                    // so there is nothing for the board or its undo history to save, and AddPCBObject notifies
                    // them of the finished object. This is the form the guide's creation example uses
                    // (docs/guides/pcb-basics.mdx#changing-objects). BeginModify/EndModify are for objects that
                    // are already on a board.
                    var pad = pcbServer.PCBObjectFactory(TObjectId.ePadObject, TDimensionKind.eNoDimension,
                        TObjectCreationMode.eCreate_Default) as IPCB_Pad
                        ?? throw new UserException("PCBObjectFactory did not return a pad.");
                    firstPad ??= pad;

                    pad.SetState_Layer(TV6_Layer.eV6_BottomLayer);
                    pad.SetState_IsTestPoint_Bottom(true);
                    pad.SetState_Net(net);

                    IPCB_Track track = FirstTrack(net, out bool onBottom);
                    if (track != null)
                    {
                        // long: the sum of two coordinates can exceed int on large boards.
                        pad.SetState_XLocation((int)(((long)track.GetState_X1() + track.GetState_X2()) / 2));
                        pad.SetState_YLocation((int)(((long)track.GetState_Y1() + track.GetState_Y2()) / 2));
                        if (onBottom)
                        {
                            onBottomTrack++;
                        }
                        else
                        {
                            pad.SetState_Layer(TV6_Layer.eV6_MultiLayer);
                            onFirstTrack++;
                        }
                    }
                    else
                    {
                        pad.SetState_XLocation(x);
                        pad.SetState_YLocation(y);
                        inGrid++;

                        x += pitch;
                        if (x > rect.Hx)
                        {
                            x = rect.Lx;
                            y += rowStep;
                        }
                    }

                    // AddPCBObject notifies the board (and its undo history) of the new object itself.
                    board.AddPCBObject(pad);
                }
            }
            finally
            {
                pcbServer.PostProcess();
            }

            // The API's redraw of the whole board is ViewManager_FullUpdate.
            board.ViewManager_FullUpdate();

            return $"{Path.GetFileName(board.GetState_FileName())}: created {nets.Count} test-point pads " +
                   $"({onBottomTrack} on a bottom-layer track, {onFirstTrack} on another track (multi-layer), " +
                   $"{inGrid} in the grid above the board outline). First pad: {Describe(firstPad)}. " +
                   "Not saved: close the document without saving to discard the pads.";
        }

        /// <summary>
        /// CountTestPointPads: read-back. Counts the pads flagged as bottom test points by layer,
        /// how many have a net, and how many lie above the board outline (the grid fallback).
        /// </summary>
        [HotCommand("CountTestPointPads", "Count the pads flagged as bottom test points (read-back)")]
        public static string CountTestPointPads(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard(PcbServer());
            CoordRect rect = board.GetState_BoardOutline()?.BoundingRectangle();

            int netCount = CollectNets(board).Count;
            int total = 0, bottom = 0, multi = 0, other = 0, withNet = 0, aboveOutline = 0;
            string first = null;

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                {
                    if (!pad.GetState_IsTestPoint_Bottom())
                        continue;

                    total++;
                    first ??= Describe(pad);
                    switch (pad.GetState_Layer())
                    {
                        case TV6_Layer.eV6_BottomLayer: bottom++; break;
                        case TV6_Layer.eV6_MultiLayer: multi++; break;
                        default: other++; break;
                    }
                    if (pad.GetState_Net() != null)
                        withNet++;
                    if (rect != null && pad.GetState_YLocation() > rect.Hy)
                        aboveOutline++;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            string text = $"{Path.GetFileName(board.GetState_FileName())}: {total} pads flagged as bottom test points " +
                          $"({bottom} bottom layer, {multi} multi-layer, {other} other layer; {withNet} with a net; " +
                          $"{aboveOutline} above the board outline). {netCount} nets on the board.";
            return first == null ? text : text + " First: " + first + ".";
        }

        /// <summary>The nets of the board, collected with a board iterator.</summary>
        private static List<IPCB_Net> CollectNets(IPCB_Board board)
        {
            var nets = new List<IPCB_Net>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eNetObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var net = iterator.FirstPCBObject() as IPCB_Net; net != null; net = iterator.NextPCBObject() as IPCB_Net)
                    nets.Add(net);
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return nets;
        }

        /// <summary>
        /// The net's first bottom-layer track (onBottom = true), else its first track, else null. GetPrimitiveAt counts
        /// from 1. The search returns an explicit result rather than testing the loop variable after the loop.
        /// </summary>
        private static IPCB_Track FirstTrack(IPCB_Net net, out bool onBottom)
        {
            onBottom = false;
            int count = net.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.eTrackObject });
            IPCB_Track first = null;
            for (int i = 1; i <= count; i++)
            {
                if (net.GetPrimitiveAt(i, TObjectId.eTrackObject) is not IPCB_Track track)
                    continue;
                first ??= track;
                if (track.GetState_Layer() == TV6_Layer.eV6_BottomLayer)
                {
                    onBottom = true;
                    return track;
                }
            }
            return first;
        }

        /// <summary>Net, layer, location and the size/hole the factory gave the pad, in mm.</summary>
        private static string Describe(IPCB_Pad pad)
        {
            if (pad == null)
                return "none";
            string Mm(int coord) => EDP.Utils.CoordToMMs(coord).ToString("0.###", CultureInfo.InvariantCulture);
            return $"net {pad.GetState_Net()?.GetState_Name() ?? "(none)"}, layer {pad.GetState_Layer()}, " +
                   $"at ({Mm(pad.GetState_XLocation())}, {Mm(pad.GetState_YLocation())}) mm, " +
                   $"size {Mm(pad.GetState_TopXSize())} x {Mm(pad.GetState_TopYSize())} mm, hole {Mm(pad.GetState_HoleSize())} mm";
        }

        private static double Millimetres(CommandContext ctx, string name, double defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
                throw new UserException($"{name}={text}: give a positive number of millimetres, for example {name}={defaultValue}.");
            return value;
        }

        private static IPCB_ServerInterface PcbServer() =>
            PCB.GlobalVars.PCBServer
            ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");

        private static IPCB_Board CurrentBoard(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetCurrentPCBBoard()
            ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
    }
}
