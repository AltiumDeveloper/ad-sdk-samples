using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.TrackPairDistance
{
    /// <summary>
    /// Measures the distance between every unique pair of tracks on the current board with IPCB_Board.PrimPrimDistance
    /// and reports the smallest distances.
    /// </summary>
    internal static class TrackPairDistanceCommands
    {
        private const int DefaultMaxTracks = 500;

        /// <summary>
        /// ClearanceCheck "Tracks=0|MaxTracks=500|SameLayerOnly=false". The pair count grows with the square of the
        /// track count, and every pair is one COM call on AD's main thread, so the command measures at most Tracks
        /// of the board's tracks (0 = all) and refuses to measure more than MaxTracks of them (0 = no limit)
        /// instead of blocking AD for minutes.
        /// </summary>
        [HotCommand("ClearanceCheck", "Measure the distance between every pair of tracks")]
        public static string ClearanceCheck(CommandContext ctx)
        {
            int maxTracks = ParseCount(ctx.Get("MaxTracks", DefaultMaxTracks.ToString(CultureInfo.InvariantCulture)), "MaxTracks");
            int trackLimit = ParseCount(ctx.Get("Tracks", "0"), "Tracks");
            bool sameLayerOnly = ctx.Flag("SameLayerOnly");

            IPCB_Board board = CurrentBoard();
            List<IPCB_Track> collected = CollectTracks(board);
            string boardName = Path.GetFileName(board.GetState_FileName());

            // Tracks limits how many of the collected tracks take part in the measurement (the first n in iteration
            // order); MaxTracks then guards the pair count of that subset, not of the whole board.
            List<IPCB_Track> tracks = trackLimit > 0 && collected.Count > trackLimit
                ? collected.GetRange(0, trackLimit)
                : collected;
            string scope = tracks.Count == collected.Count
                ? $"{collected.Count} tracks"
                : $"{tracks.Count} of {collected.Count} tracks";

            if (maxTracks > 0 && tracks.Count > maxTracks)
                throw new UserException(
                    $"{boardName} has {collected.Count} tracks; {tracks.Count} would be measured " +
                    $"({PairCount(tracks.Count)} pairs), more than MaxTracks={maxTracks}. " +
                    "Measure fewer tracks (Tracks=<n>), or pass a higher MaxTracks (0 = no limit).");
            if (tracks.Count < 2)
                return $"{boardName}: {scope} measured, no pair to measure.";

            // The SDK must be called on AD's main thread (docs: runtime-and-hosting#the-main-thread), so this
            // loop stays on the calling thread and reports progress through ctx.Log.
            long measured = 0, touching = 0, notMeasured = 0;
            Pair? closest = null, closestApart = null;
            for (int i = 0; i < tracks.Count - 1; i++)
            {
                IPCB_Track first = tracks[i];
                TV6_Layer firstLayer = first.GetState_Layer();
                for (int j = i + 1; j < tracks.Count; j++)
                {
                    IPCB_Track second = tracks[j];
                    if (sameLayerOnly && second.GetState_Layer() != firstLayer)
                        continue;

                    // Internal units (10,000 per mil). PCBConstant.MaxCoord means no distance was returned for the
                    // pair. The reference presents that as a null primitive or one that is not on this board, but it
                    // occurs for ordinary primitives of this same board, and often, so every
                    // pair has to be tested for it.
                    int distance = board.PrimPrimDistance(first, second);
                    if (distance >= PCBConstant.MaxCoord)
                    {
                        notMeasured++;
                        continue;
                    }

                    measured++;
                    var pair = new Pair(i, j, distance);
                    if (distance <= 0)
                        touching++;
                    else if (closestApart == null || distance < closestApart.Value.Distance)
                        closestApart = pair;
                    if (closest == null || distance < closest.Value.Distance)
                        closest = pair;
                }

                if ((i + 1) % 50 == 0)
                    ctx.Log($"ClearanceCheck: {i + 1} of {tracks.Count} tracks done, {measured} pairs measured.");
            }

            var report = new List<string>
            {
                $"{boardName}: {scope} measured, {measured} pairs measured" +
                (sameLayerOnly ? " (same layer only)" : "") +
                $", {touching} touching or overlapping (distance <= 0)" +
                (notMeasured > 0 ? $", {notMeasured} not measured (MaxCoord)." : ".")
            };
            if (closest != null)
                report.Add("Smallest distance: " + Describe(board, tracks, closest.Value));
            report.Add(closestApart != null
                ? "Smallest distance between tracks that do not touch: " + Describe(board, tracks, closestApart.Value)
                : "No pair of tracks is apart (every measured pair touches).");
            return string.Join(Environment.NewLine, report);
        }

        private readonly struct Pair
        {
            public Pair(int first, int second, int distance) { First = first; Second = second; Distance = distance; }
            public int First { get; }
            public int Second { get; }
            public int Distance { get; }
        }

        /// <summary>
        /// Every track of the board, on all layers. The COM references live only for this command call (no statics);
        /// the iterator is destroyed before the pairwise loop.
        /// </summary>
        private static List<IPCB_Track> CollectTracks(IPCB_Board board)
        {
            var tracks = new List<IPCB_Track>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eTrackObject });
                // AddFilter_AllLayers is the .NET form of AddFilter_LayerSet(cV6_AllLayers).
                iterator.AddFilter_AllLayers();
                // eProcessAll also visits tracks inside group objects such as component footprints.
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var track = iterator.FirstPCBObject() as IPCB_Track; track != null; track = iterator.NextPCBObject() as IPCB_Track)
                    tracks.Add(track);
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return tracks;
        }

        private static string Describe(IPCB_Board board, List<IPCB_Track> tracks, Pair pair) =>
            $"{Coord(pair.Distance)} ({pair.Distance} internal units)" + Environment.NewLine +
            "    " + DescribeTrack(board, tracks[pair.First]) + Environment.NewLine +
            "    " + DescribeTrack(board, tracks[pair.Second]);

        /// <summary>Layer, net and end points; coordinates relative to the board origin (internal values are absolute).</summary>
        private static string DescribeTrack(IPCB_Board board, IPCB_Track track)
        {
            int ox = board.GetState_XOrigin(), oy = board.GetState_YOrigin();
            TV6_Layer layer = track.GetState_Layer();
            string layerName = board.GetState_LayerStack()?.LayerObject(layer)?.GetState_LayerName();
            if (string.IsNullOrEmpty(layerName))
                layerName = layer.ToString();
            string net = track.GetState_Net()?.GetState_Name() ?? "No Net";
            return $"Track on {layerName}, net {net}: ({Coord(track.GetState_X1() - ox)}, {Coord(track.GetState_Y1() - oy)}) - " +
                   $"({Coord(track.GetState_X2() - ox)}, {Coord(track.GetState_Y2() - oy)}), width {Coord(track.GetState_Width())}";
        }

        private static string Coord(int value) => EDP.Utils.CoordUnitToString(value, TUnit.eMetric);

        private static long PairCount(int n) => (long)n * (n - 1) / 2;

        private static int ParseCount(string text, string name)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
                throw new UserException($"{name} must be a whole number >= 0 (0 = no limit), not '{text}'.");
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
