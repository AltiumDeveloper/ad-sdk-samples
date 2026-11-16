using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.PickTrackCoordinates
{
    /// <summary>
    /// Lets the user click tracks on the current board and reports their start and end points in mils, relative to
    /// the board origin. The SDK lesson: IPCB_Board.ChooseLocation returns a clicked point in internal units, and
    /// IPCB_Board.GetObjectAtXYAskUserIfAmbiguous turns that point into a primitive of the requested object types.
    /// </summary>
    internal static class PickTrackCoordinatesCommands
    {
        /// <summary>
        /// ShowTrackCoord "Max=&lt;n&gt;|Pick=true|At=&lt;x,y&gt;".
        /// From the menu: prompts for clicks until Escape, a click away from a track, or Max picks (default: no limit).
        /// From the command line: ChooseLocation would block Altium Designer until someone clicks, so the command
        /// prompts only with Pick=true (default Max=1), or skips the prompt with At=x,y (mils, relative to
        /// the board origin).
        /// </summary>
        [HotCommand("ShowTrackCoord", "Pick tracks and show their coordinates")]
        public static string ShowTrackCoord(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            int originX = board.GetState_XOrigin();
            int originY = board.GetState_YOrigin();

            string at = ctx.Get("At");
            if (!string.IsNullOrWhiteSpace(at))
            {
                // Unattended path: the point is given, only the hit test runs.
                (int x, int y) = ParsePoint(at, originX, originY);
                IPCB_Track track = TrackAt(board, x, y, out string other);
                return track != null
                    ? Describe(track, originX, originY)
                    : $"No track at {at}" + (other != null ? $" (found {other})." : ".");
            }

            if (!ctx.IsInteractive && !ctx.Flag("Pick"))
                throw new UserException(
                    "ShowTrackCoord waits for a click in the PCB editor, which would block Altium Designer on the pipe. " +
                    "Pass Pick=true to prompt anyway (someone must click), or At=x,y (mils from the board origin) to test a point.");

            int max = ParseMax(ctx.Get("Max"), ctx.IsInteractive ? int.MaxValue : 1);
            var picks = new List<string>();
            string stopReason = $"stopped after {max} pick(s)";

            while (picks.Count < max)
            {
                int x = 0, y = 0;
                // Returns false on Escape, and without prompting when the board has no view. Ignoring the result
                // would query the point (0,0).
                if (!board.ChooseLocation(ref x, ref y, "Choose a track"))
                {
                    stopReason = "no location chosen (Escape, or the board has no view)";
                    break;
                }

                IPCB_Track track = TrackAt(board, x, y, out string other);
                if (track == null)
                {
                    // The loop ends when there is no track under the click.
                    stopReason = other != null ? $"clicked {other}, not a track" : "clicked away from a track";
                    break;
                }

                string line = Describe(track, originX, originY);
                picks.Add(line);
                ctx.Log(line);
            }

            string head = picks.Count == 0
                ? $"No track picked: {stopReason}."
                : $"{picks.Count} track(s) picked, {stopReason}:";
            return picks.Count == 0 ? head : head + Environment.NewLine + string.Join(Environment.NewLine, picks);
        }

        /// <summary>
        /// ListTracks "Max=&lt;n&gt;": test helper. Lists the first tracks of the board with the
        /// same coordinates ShowTrackCoord reports, plus each track's midpoint, which can be passed to ShowTrackCoord
        /// as At=x,y to check the pick path without a human.
        /// </summary>
        [HotCommand("ListTracks", "List track coordinates (test helper)")]
        public static string ListTracks(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            int originX = board.GetState_XOrigin();
            int originY = board.GetState_YOrigin();
            int max = ParseMax(ctx.Get("Max"), 5);

            var lines = new List<string>();
            int total = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eTrackObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var track = iterator.FirstPCBObject() as IPCB_Track; track != null; track = iterator.NextPCBObject() as IPCB_Track)
                {
                    total++;
                    if (lines.Count >= max)
                        continue;
                    int midX = (track.GetState_X1() + track.GetState_X2()) / 2;
                    int midY = (track.GetState_Y1() + track.GetState_Y2()) / 2;
                    lines.Add(Describe(track, originX, originY) +
                              $", mid At={Mils(midX - originX)},{Mils(midY - originY)}");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            lines.Insert(0, $"{Path.GetFileName(board.GetState_FileName())}: {total} tracks, first {lines.Count} listed.");
            return string.Join(Environment.NewLine, lines);
        }

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB server is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("Open a .PcbDoc and make it the active document first.");
        }

        /// <summary>
        /// The track at (x, y) in internal units, or null. If several objects overlap, AD asks the user to choose.
        /// The result is type-checked, not cast blindly; <paramref name="other"/> names a
        /// non-track primitive if one is returned.
        /// </summary>
        private static IPCB_Track TrackAt(IPCB_Board board, int x, int y, out string other)
        {
            IPCB_Primitive primitive = board.GetObjectAtXYAskUserIfAmbiguous(x, y,
                new PCB.TObjectSet { TObjectId.eTrackObject }, PCBConstant.V6AllLayersSet, TEditingAction.eEditAction_Select);
            other = primitive != null && !(primitive is IPCB_Track) ? primitive.GetState_ObjectID().ToString() : null;
            return primitive as IPCB_Track;
        }

        /// <summary>Start and end point in mils relative to the board origin, plus layer and width.</summary>
        private static string Describe(IPCB_Track track, int originX, int originY) =>
            $"x1 = {Mils(track.GetState_X1() - originX)} y1 = {Mils(track.GetState_Y1() - originY)} " +
            $"x2 = {Mils(track.GetState_X2() - originX)} y2 = {Mils(track.GetState_Y2() - originY)} " +
            $"(layer {track.GetState_Layer()}, width {Mils(track.GetState_Width())} mil)";

        // EDP.Utils.CoordToMils converts internal units to mils (10,000 per mil). Invariant culture so the pipe output
        // doesn't depend on the Windows locale.
        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>"x,y" in mils relative to the board origin, to absolute internal units.</summary>
        private static (int X, int Y) ParsePoint(string text, int originX, int originY)
        {
            string[] parts = text.Split(',');
            if (parts.Length != 2 ||
                !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double xMil) ||
                !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double yMil))
                throw new UserException($"At must be 'x,y' in mils, for example At=1000,250.5 (got '{text}').");

            // 10,000 internal units per mil (EDP.EDPConstant.InternalUnits).
            return ((int)Math.Round(xMil * EDP.EDPConstant.InternalUnits) + originX,
                    (int)Math.Round(yMil * EDP.EDPConstant.InternalUnits) + originY);
        }

        private static int ParseMax(string text, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive number (got '{text}').");
            return max;
        }
    }
}
