using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PCB;

namespace AdSamples.ReplicateCopper
{
    /// <summary>
    /// Copies the free copper of the Top and Multi layers (arcs, pads, vias, tracks, fills, regions, polygons) and the
    /// components on those layers, and moves each copy by an X offset. The lesson: IPCB_Primitive.Replicate (single
    /// primitives) versus IPCB_Group.ReplicateWithChildren (polygons, components), IPCB_Board.AddPCBObject, and
    /// IPCB_Primitive.MoveByXY inside BeginModify/EndModify.
    /// </summary>
    internal static class ReplicateCopperCommands
    {
        private const double DefaultOffsetMM = 40;
        private const double MaxOffsetMM = 1000; // keeps MMsToCoord well inside the int coordinate range

        /// <summary>
        /// ReplicateCopper "OffsetMM=40": replicates the objects and returns how many of each kind were copied, plus
        /// the shift measured on the first copy (its bounding rectangle's Left minus the original's).
        /// </summary>
        [HotCommand("ReplicateCopper", "Replicate Top and Multi layer copper by an X offset")]
        public static string ReplicateCopper(CommandContext ctx)
        {
            double offsetMM = ParseOffset(ctx.Get("OffsetMM"));
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            // Collect first, change afterwards. Adding each copy to the board while the iterator is still walking it
            // could make a copy on the Top or Multi layer be visited again or disturb the walk.
            List<IPCB_Primitive> originals = CollectFreeCopper(board);
            if (originals.Count == 0)
                return $"{BoardName(board)}: no free Top/Multi layer objects to replicate.";

            int dx = EDP.Utils.MMsToCoord(offsetMM);
            var copiedByKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int failed = 0;
            string measured = null;

            pcbServer.PreProcess();
            board.BeginModify();
            try
            {
                for (int i = 0; i < originals.Count; i++)
                {
                    IPCB_Primitive original = originals[i];
                    TObjectId kind = original.GetState_ObjectID();

                    // Polygons and components are groups: copy them with ReplicateWithChildren, which gives a
                    // component copy that carries its child primitives (a component and its copy have the same
                    // number of children).
                    IPCB_Primitive copy = kind == TObjectId.ePolyObject || kind == TObjectId.eComponentObject
                        ? ((IPCB_Group)original).ReplicateWithChildren()
                        : original.Replicate();
                    if (copy == null)
                    {
                        failed++;
                        continue;
                    }

                    // The copy belongs to no board until AddPCBObject.
                    board.AddPCBObject(copy);

                    copy.BeginModify();
                    copy.MoveByXY(dx, 0);
                    copy.EndModify();

                    if (measured == null)
                    {
                        double shift = EDP.Utils.CoordToMMs(copy.BoundingRectangle().Left - original.BoundingRectangle().Left);
                        measured = $"first copy ({KindName(kind)}) moved {Mm(shift)} mm";
                    }

                    string name = KindName(kind);
                    copiedByKind[name] = copiedByKind.TryGetValue(name, out int n) ? n + 1 : 1;
                    if ((i + 1) % 500 == 0)
                        ctx.Log($"{i + 1} of {originals.Count} replicated");
                }
            }
            finally
            {
                board.EndModify();
                pcbServer.PostProcess();
            }

            board.GraphicallyInvalidate();

            int copied = copiedByKind.Values.Sum();
            string text = $"{BoardName(board)}: replicated {copied} of {originals.Count} objects by {Mm(offsetMM)} mm in X " +
                          $"({FormatCounts(copiedByKind)}); {measured ?? "no copy measured"}.";
            if (failed > 0)
                text += $" {failed} objects returned no copy.";
            return text + " Close the document without saving to discard the copies.";
        }

        /// <summary>
        /// CountCopper: helper. Counts the objects ReplicateCopper would copy, by kind, so a run
        /// can be checked by counting before and after (the counts double).
        /// </summary>
        [HotCommand("CountCopper", "Count the free Top and Multi layer objects ReplicateCopper copies (test aid)")]
        public static string CountCopper(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            List<IPCB_Primitive> objects = CollectFreeCopper(board);
            var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (IPCB_Primitive p in objects)
            {
                string name = KindName(p.GetState_ObjectID());
                byKind[name] = byKind.TryGetValue(name, out int n) ? n + 1 : 1;
            }
            return $"{BoardName(board)}: {objects.Count} objects ({FormatCounts(byKind)}).";
        }

        /// <summary>
        /// The filter: eight object kinds on the Top and Multi layers, free objects only (eProcessFree, so
        /// the pads and tracks inside components are not visited on their own), board-outline regions skipped.
        /// </summary>
        private static List<IPCB_Primitive> CollectFreeCopper(IPCB_Board board)
        {
            var result = new List<IPCB_Primitive>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet
                {
                    TObjectId.eArcObject, TObjectId.ePadObject, TObjectId.eViaObject, TObjectId.eTrackObject,
                    TObjectId.eFillObject, TObjectId.eComponentObject, TObjectId.eRegionObject, TObjectId.ePolyObject,
                });
                iterator.AddFilter_LayerSet(new TV6_LayerSet { TV6_Layer.eV6_TopLayer, TV6_Layer.eV6_MultiLayer });
                iterator.AddFilter_Method(TIterationMethod.eProcessFree);

                for (IPCB_Primitive p = iterator.FirstPCBObject(); p != null; p = iterator.NextPCBObject())
                {
                    // GetState_ViewableObjectID is on IPCB_Primitive, no cast to IPCB_Region is needed. A board region (the board shape) must not be copied.
                    if (p.GetState_ObjectID() == TObjectId.eRegionObject &&
                        p.GetState_ViewableObjectID() == TViewableObjectID.eViewableObject_BoardRegion)
                        continue;
                    result.Add(p);
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return result;
        }

        private static double ParseOffset(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultOffsetMM;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mm) ||
                double.IsNaN(mm) || Math.Abs(mm) > MaxOffsetMM)
                throw new UserException($"OffsetMM must be a number of millimetres between -{MaxOffsetMM} and {MaxOffsetMM} (got '{value}').");
            return mm;
        }

        private static string KindName(TObjectId kind) => kind switch
        {
            TObjectId.eArcObject => "arcs",
            TObjectId.ePadObject => "pads",
            TObjectId.eViaObject => "vias",
            TObjectId.eTrackObject => "tracks",
            TObjectId.eFillObject => "fills",
            TObjectId.eComponentObject => "components",
            TObjectId.eRegionObject => "regions",
            TObjectId.ePolyObject => "polygons",
            _ => kind.ToString(),
        };

        private static string FormatCounts(SortedDictionary<string, int> counts) =>
            counts.Count == 0 ? "none" : string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"));

        private static string Mm(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string BoardName(IPCB_Board board) => Path.GetFileName(board.GetState_FileName());
    }
}
