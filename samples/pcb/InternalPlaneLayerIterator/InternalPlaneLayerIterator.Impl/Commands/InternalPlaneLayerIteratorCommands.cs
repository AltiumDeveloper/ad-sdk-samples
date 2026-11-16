using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.InternalPlaneLayerIterator
{
    /// <summary>
    /// Walks the internal plane layers of the current board with IPCB_Board.InternalPlaneLayerIterator and, for each
    /// plane, runs a board iterator restricted to that layer and to tracks. The plane iterator only yields the planes
    /// that are in the layer stack, so there is no need to filter a general layer iterator.
    /// </summary>
    internal static class InternalPlaneLayerIteratorCommands
    {
        /// <summary>
        /// GetTracks "Units=mm|mil|Max=&lt;n&gt;|Out=&lt;file or folder&gt;|Check=true": a per-plane report of the tracks.
        /// Max limits the tracks listed per layer in the returned text (the counts are always complete); Out writes
        /// every track to a file; Check also counts the plane's tracks without the layer filter, to cross-check it.
        /// </summary>
        [HotCommand("GetTracks", "List the tracks on each internal plane layer")]
        public static string GetTracks(CommandContext ctx)
        {
            bool mils = ParseUnits(ctx.Get("Units", "mm"));
            int max = ParseMax(ctx.Get("Max", "20"));
            bool check = ctx.Flag("Check");

            IPCB_Board board = CurrentBoard();
            string boardName = Path.GetFileName(board.GetState_FileName());
            int xOrigin = board.GetState_XOrigin(), yOrigin = board.GetState_YOrigin();

            var shown = new List<string>();   // returned text, at most Max tracks per layer
            var full = new List<string>();    // file dump, every track
            int planeCount = 0, trackTotal = 0;

            // InternalPlaneLayerIterator returns an IPCB_LayerObjectIterator: the walk (SetBeforeFirst/Next/Layer) is
            // inherited from IPCB_LayerIterator, LayerObject adds the layer object of the current position.
            IPCB_LayerObjectIterator layerIterator = board.InternalPlaneLayerIterator();
            layerIterator.SetBeforeFirst();
            while (layerIterator.Next())
            {
                planeCount++;
                V7_LayerBase layer = layerIterator.Layer();
                string layerName = board.LayerName(layer);

                // The board iterator's layer filter takes a TV6_LayerSet, while Layer() is a V7_LayerBase. In
                // .NET the layer object gives the TV6_Layer value
                // (IPCB_LayerObject.V6_LayerID covers internal planes 1 to 16).
                IPCB_LayerObject layerObject = layerIterator.LayerObject();
                if (layerObject == null)
                {
                    string skipped = $"{layerName}: no layer object, skipped.";
                    shown.Add(skipped);
                    full.Add(skipped);
                    continue;
                }
                TV6_Layer v6Layer = layerObject.V6_LayerID();

                var lines = new List<string>();
                IPCB_BoardIterator iterator = board.BoardIterator_Create();
                try
                {
                    iterator.AddFilter_LayerSet(new TV6_LayerSet { v6Layer });
                    iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eTrackObject });
                    iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                    for (var track = iterator.FirstPCBObject() as IPCB_Track;
                         track != null;
                         track = iterator.NextPCBObject() as IPCB_Track)
                    {
                        lines.Add("    (" + Coord(track.GetState_X1() - xOrigin, mils) + ", " + Coord(track.GetState_Y1() - yOrigin, mils) +
                                  ") - (" + Coord(track.GetState_X2() - xOrigin, mils) + ", " + Coord(track.GetState_Y2() - yOrigin, mils) +
                                  "), width " + Coord(track.GetState_Width(), mils));
                    }
                }
                finally
                {
                    board.BoardIterator_Destroy(ref iterator);
                }
                trackTotal += lines.Count;

                string header = $"{layerName} ({v6Layer}): {lines.Count} track(s)";
                if (check)
                    header += $"; cross-check without layer filter: {CountTracksOnLayer(board, v6Layer)}";
                shown.Add(header);
                full.Add(header);
                full.AddRange(lines);
                for (int i = 0; i < lines.Count && i < max; i++)
                    shown.Add(lines[i]);
                if (lines.Count > max)
                    shown.Add($"    ... {lines.Count - max} more (Max={max})");
            }

            string unit = mils ? "mil" : "mm";
            string summary = planeCount == 0
                ? $"{boardName}: the board has no internal plane layers."
                : $"{boardName}: {planeCount} internal plane layer(s), {trackTotal} track(s) on them " +
                  $"(coordinates in {unit}, relative to the board origin).";
            shown.Insert(0, summary);
            full.Insert(0, summary);

            string text = string.Join(Environment.NewLine, shown);
            string written = WriteReport(ctx.Get("Out"), boardName, full);
            return written == null ? text : text + Environment.NewLine + "Report written to " + written;
        }

        /// <summary>
        /// AddTestPlaneTrack "AddPlane=true": test setup. Places a 10 mil track from (100, 100) to
        /// (600, 100) mil, relative to the board origin, on the first internal plane layer. When the board has no
        /// internal plane and AddPlane=true, it first adds the first free internal plane to the layer stack.
        /// </summary>
        [HotCommand("AddTestPlaneTrack", "Place a track on an internal plane (test setup)")]
        public static string AddTestPlaneTrack(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;

            string planeNote = "";
            TV6_Layer? plane = FirstPlaneLayer(board);
            if (plane == null)
            {
                if (!ctx.Flag("AddPlane"))
                    throw new UserException("The board has no internal plane layers. Pass AddPlane=true to add one first.");

                IPCB_LayerStack_V7 stack = board.GetState_LayerStack_V7()
                    ?? throw new UserException("The board has no layer stack.");
                IPCB_InternalPlane_V7 free = stack.FirstAvailableInternalPlane()
                    ?? throw new UserException("Every internal plane is already in the layer stack.");
                TV6_Layer newPlane = free.V6_LayerID();

                pcbServer.PreProcess();
                try
                {
                    stack.InsertLayer(newPlane);
                }
                finally
                {
                    pcbServer.PostProcess();
                }

                plane = FirstPlaneLayer(board)
                    ?? throw new UserException($"InsertLayer({newPlane}) did not add an internal plane to the layer stack.");
                planeNote = $"Added {newPlane} to the layer stack. ";
            }

            var track = pcbServer.PCBObjectFactory(TObjectId.eTrackObject, TDimensionKind.eNoDimension,
                                                   TObjectCreationMode.eCreate_Default) as IPCB_Track
                ?? throw new UserException("PCBObjectFactory did not create a track.");

            int x = board.GetState_XOrigin(), y = board.GetState_YOrigin();
            pcbServer.PreProcess();
            try
            {
                track.SetState_X1(x + EDP.Utils.MilsToCoord(100));
                track.SetState_Y1(y + EDP.Utils.MilsToCoord(100));
                track.SetState_X2(x + EDP.Utils.MilsToCoord(600));
                track.SetState_Y2(y + EDP.Utils.MilsToCoord(100));
                track.SetState_Width(EDP.Utils.MilsToCoord(10));
                track.SetState_Layer(plane.Value);
                board.AddPCBObject(track);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            return planeNote + $"Placed a track (100, 100) - (600, 100) mil, width 10 mil, on {plane.Value} " +
                   $"(track reads layer {track.GetState_Layer()}). Close the document without saving to discard it.";
        }

        /// <summary>The TV6_Layer of the first internal plane in the layer stack, or null when there is none.</summary>
        private static TV6_Layer? FirstPlaneLayer(IPCB_Board board)
        {
            IPCB_LayerObjectIterator layerIterator = board.InternalPlaneLayerIterator();
            layerIterator.SetBeforeFirst();
            while (layerIterator.Next())
            {
                IPCB_LayerObject layerObject = layerIterator.LayerObject();
                if (layerObject != null)
                    return layerObject.V6_LayerID();
            }
            return null;
        }

        /// <summary>
        /// Counts the tracks whose Layer is the given layer, walking all tracks without a layer filter. Used by
        /// Check=true to confirm that the TV6_LayerSet filter selects the plane's tracks.
        /// </summary>
        private static int CountTracksOnLayer(IPCB_Board board, TV6_Layer layer)
        {
            int count = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eTrackObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var track = iterator.FirstPCBObject() as IPCB_Track; track != null; track = iterator.NextPCBObject() as IPCB_Track)
                {
                    if (track.GetState_Layer() == layer)
                        count++;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return count;
        }

        /// <summary>Coordinates are int values in internal units (10,000 per mil); EDP.Utils converts them.</summary>
        private static string Coord(int value, bool mils) =>
            mils
                ? EDP.Utils.CoordToMils(value).ToString("0.###", CultureInfo.InvariantCulture)
                : EDP.Utils.CoordToMMs(value).ToString("0.####", CultureInfo.InvariantCulture);

        private static bool ParseUnits(string units)
        {
            switch ((units ?? "mm").Trim().ToLowerInvariant())
            {
                case "mm": return false;
                case "mil": case "mils": return true;
                default: throw new UserException($"Units must be mm or mil, not '{units}'.");
            }
        }

        private static int ParseMax(string max)
        {
            if (!int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                throw new UserException($"Max must be a number of tracks (0 or more), not '{max}'.");
            return n;
        }

        /// <summary>Writes the full report when Out is given (a file, or a folder for a default file name); returns the path.</summary>
        private static string WriteReport(string path, string boardName, List<string> lines)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string fileName = "InternalPlaneTracks_" + Path.GetFileNameWithoutExtension(boardName) + ".txt";
            if (Directory.Exists(path))
                path = Path.Combine(path, fileName);
            path = Path.GetFullPath(path);
            File.WriteAllLines(path, lines);
            return path;
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
