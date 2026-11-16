using System;
using System.Collections.Generic;
using System.IO;
using DXP; // the IClient.GetCurrentView / GetDocumentByPath and IServerDocumentView.GetOwnerDocument extension methods live in namespace DXP
using PCB;

namespace AdSamples.ConvertToStep
{
    /// <summary>
    /// Replaces every non-generic 3D body model (extruded, cylinder, sphere) of every footprint in the active PCB
    /// library with a generic copy. The SDK lesson: walk the footprints with the library iterator, walk each
    /// footprint's bodies with a group iterator, and swap the model with IPCB_Model.I_ReplicateToGeneric and
    /// IPCB_ComponentBody.Model.
    /// </summary>
    internal static class ConvertToStepCommands
    {
        /// <summary>
        /// ConvertToStep "DryRun=true": converts the bodies of the focused .PcbLib in memory and marks the library
        /// modified. With DryRun the bodies are only counted. Either way the result lists the model types before and
        /// after, read back from the bodies.
        /// </summary>
        [HotCommand("ConvertToStep", "Convert non-generic 3D bodies of the PcbLib to generic")]
        public static string ConvertToStep(CommandContext ctx)
        {
            bool dryRun = ctx.Flag("DryRun");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            // Null when the current view is not a PCB library document.
            IPCB_Library library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("This is not a PCB library document. Open and focus a .PcbLib first.");

            int footprints = 0, bodies = 0, noModel = 0, converted = 0, failed = 0;
            var before = new SortedDictionary<string, int>();
            var after = new SortedDictionary<string, int>();
            var failures = new List<string>();

            IPCB_LibraryIterator libIterator = library.LibraryIterator_Create();
            // Nested Pre/PostProcess pairs are allowed; one pair around the whole run updates the views once.
            if (!dryRun)
                pcbServer.PreProcess();
            try
            {
                for (IPCB_Primitive item = libIterator.FirstPCBObject(); item != null; item = libIterator.NextPCBObject())
                {
                    if (item is not IPCB_LibComponent footprint)
                        continue;
                    footprints++;
                    ctx.Log($"Footprint '{footprint.GetState_Pattern()}'");

                    IPCB_GroupIterator groupIterator = footprint.GroupIterator_Create();
                    try
                    {
                        groupIterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
                        // The cV6_AllLayers layer set; AddFilter_AllLayers is the .NET form used by the
                        // docs' iterator examples.
                        groupIterator.AddFilter_AllLayers();

                        for (IPCB_Primitive child = groupIterator.FirstPCBObject(); child != null; child = groupIterator.NextPCBObject())
                        {
                            if (child is not IPCB_ComponentBody body)
                                continue;
                            bodies++;

                            IPCB_Model model = body.GetModel();
                            if (model == null)
                            {
                                noModel++;
                                continue;
                            }

                            T3DModelType type = model.GetState_ModelType();
                            Count(before, Name(type));
                            if (dryRun || type == T3DModelType.e3DModelType_Generic)
                            {
                                Count(after, Name(type));
                                continue;
                            }

                            IPCB_Model generic = model.I_ReplicateToGeneric();
                            if (generic == null)
                            {
                                failed++;
                                failures.Add($"'{footprint.GetState_Pattern()}': I_ReplicateToGeneric returned null for a {Name(type)} body");
                                Count(after, Name(type));
                                continue;
                            }

                            body.BeginModify();
                            body.SetModel(generic);
                            body.EndModify();

                            // Read back what the body holds now, so the result shows whether the swap took effect.
                            T3DModelType now = body.GetModel()?.GetState_ModelType() ?? type;
                            Count(after, body.GetModel() == null ? "no model" : Name(now));
                            if (now == T3DModelType.e3DModelType_Generic)
                                converted++;
                            else
                            {
                                failed++;
                                failures.Add($"'{footprint.GetState_Pattern()}': the body still has a {Name(now)} model after SetModel");
                            }
                        }
                    }
                    finally
                    {
                        footprint.GroupIterator_Destroy(ref groupIterator);
                    }
                }
            }
            finally
            {
                library.LibraryIterator_Destroy(ref libIterator);
                if (!dryRun)
                    pcbServer.PostProcess();
            }

            string marked = !dryRun && converted > 0 ? " " + MarkModified(library) : "";

            var lines = new List<string>
            {
                $"{(dryRun ? "Dry run" : "Converted")}: {footprints} footprints, {bodies} 3D bodies ({noModel} without a model).",
                $"Model types before: {Format(before)}.",
                $"Model types after: {Format(after)}.",
                dryRun
                    ? $"{Total(before) - Get(before, "Generic")} bodies would be converted. Nothing was changed."
                    : $"{converted} bodies converted to generic, {failed} failed.{marked}"
            };
            lines.AddRange(failures);
            if (!dryRun && converted > 0)
                lines.Add("The library is changed in memory only: save it to keep the change, or close it without saving.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// AddTestBody "Radius=20|Height=50": test setup. Adds a 3D body with a cylinder model
        /// (sizes in mils) at the reference point of the current footprint, so that ConvertToStep has a non-generic
        /// body to convert on any library.
        /// </summary>
        [HotCommand("AddTestBody", "Add a cylinder 3D body to the current footprint (test setup)")]
        public static string AddTestBody(CommandContext ctx)
        {
            int radius = EDP.Utils.MilsToCoord(Number(ctx, "Radius", 20));
            int height = EDP.Utils.MilsToCoord(Number(ctx, "Height", 50));
            if (radius <= 0 || height <= 0)
                throw new UserException("Radius and Height must be positive (mils).");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            IPCB_Library library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("This is not a PCB library document. Open and focus a .PcbLib first.");
            IPCB_LibComponent footprint = library.GetState_CurrentComponent()
                ?? throw new UserException("The PCB library has no current footprint.");
            IPCB_Board board = footprint.GetState_Board() ?? library.GetState_Board()
                ?? throw new UserException("The current footprint has no board.");

            const string identifier = "ConvertToStep_TestCylinder";
            IPCB_ComponentBody body;
            string how;
            pcbServer.PreProcess();
            try
            {
                IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentBodyObject,
                    TDimensionKind.eNoDimension, TObjectCreationMode.eCreate_Default);
                body = primitive as IPCB_ComponentBody;
                if (body == null)
                {
                    if (primitive != null)
                        pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException("PCBObjectFactory did not create a component body.");
                }

                IPCB_Model model = body.ModelFactory_CreateCylinder(radius, height, 0x00C0C0C0)
                    ?? throw new UserException("ModelFactory_CreateCylinder returned null.");
                body.SetState_Layer(TV6_Layer.eV6_Mechanical1);
                body.SetModel(model); // reference remarks: also recalculates the body's outline and heights
                body.SetState_Identifier(identifier);
                body.MoveToXY(footprint.GetState_XLocation(), footprint.GetState_YLocation());

                // IPCB_Group.AddPCBObject remarks say: board first, then footprint. On the
                // library board, IPCB_Board.AddPCBObject already puts the body into the current footprint, and a
                // second IPCB_LibComponent.AddPCBObject then lists it twice in the footprint's group iterator. So the
                // footprint add is made only when the board add did not already do it.
                var bodySet = new PCB.TObjectSet { TObjectId.eComponentBodyObject };
                int countBefore = footprint.GetPrimitiveCount(bodySet);
                body.SetState_Board(board);
                board.AddPCBObject(body);
                bool addedByBoard = footprint.GetPrimitiveCount(bodySet) > countBefore;
                if (!addedByBoard)
                    footprint.AddPCBObject(body);
                how = addedByBoard ? "IPCB_Board.AddPCBObject put it into the footprint" : "added to the footprint with IPCB_LibComponent.AddPCBObject";
                how += $"; footprint now has {footprint.GetPrimitiveCount(bodySet)} bodies (was {countBefore})";
            }
            finally
            {
                pcbServer.PostProcess();
            }

            string type = body.GetModel() == null ? "no model" : Name(body.GetModel().GetState_ModelType());
            return $"Added 3D body '{identifier}' ({type} model, radius {EDP.Utils.CoordToMils(radius)} mil, " +
                   $"height {EDP.Utils.CoordToMils(height)} mil) to footprint '{footprint.GetState_Pattern()}'. " +
                   MarkModified(library) + $" ({how})";
        }

        /// <summary>
        /// Marks the library modified. The library's board file name is assumed to be the .PcbLib
        /// path; the focused document is the fallback (as in ImportStepModelToFootprint).
        /// </summary>
        private static string MarkModified(IPCB_Library library)
        {
            IClient client = DXP.GlobalVars.Client;
            if (client == null)
                return "The document was not marked modified (no client).";
            string fileName = library.GetState_Board()?.GetState_FileName();
            IServerDocument document = string.IsNullOrEmpty(fileName) ? null : client.GetDocumentByPath(fileName);
            document ??= client.GetCurrentView()?.GetOwnerDocument();
            if (document == null)
                return "No document found to mark modified.";
            document.SetModified(true);
            return $"Marked {Path.GetFileName(document.GetFileName())} modified.";
        }

        private static string Name(T3DModelType type) => type switch
        {
            T3DModelType.e3DModelType_Extruded => "Extruded",
            T3DModelType.e3DModelType_Generic => "Generic",
            T3DModelType.e3DModelType_Cylinder => "Cylinder",
            T3DModelType.e3DModelType_Sphere => "Sphere",
            _ => type.ToString()
        };

        private static void Count(IDictionary<string, int> counts, string key) =>
            counts[key] = Get(counts, key) + 1;

        private static int Get(IDictionary<string, int> counts, string key) =>
            counts.TryGetValue(key, out int n) ? n : 0;

        private static int Total(IDictionary<string, int> counts)
        {
            int total = 0;
            foreach (int n in counts.Values)
                total += n;
            return total;
        }

        private static string Format(IDictionary<string, int> counts)
        {
            if (counts.Count == 0)
                return "none";
            var parts = new List<string>();
            foreach (var pair in counts)
                parts.Add($"{pair.Key} {pair.Value}");
            return string.Join(", ", parts);
        }

        private static double Number(CommandContext ctx, string name, double defaultValue)
        {
            string value = ctx.Get(name, null);
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double result))
                throw new UserException($"{name} must be a number in mils (got '{value}').");
            return result;
        }
    }
}
