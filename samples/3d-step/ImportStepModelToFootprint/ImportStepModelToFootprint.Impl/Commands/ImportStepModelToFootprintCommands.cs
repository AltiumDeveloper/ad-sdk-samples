using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using DXP; // the IClient.GetDocumentByPath / GetCurrentView and IServerDocumentView.GetOwnerDocument extension methods live in namespace DXP
using PCB;

namespace AdSamples.ImportStepModelToFootprint
{
    /// <summary>
    /// Attaches a STEP file to the current footprint of a PCB library as a 3D component body. The SDK lesson: a body
    /// comes from PCBObjectFactory(eComponentBodyObject), its model from IPCB_ComponentBody.ModelFactory_FromFilename;
    /// the model is assigned to the body with Model, and the body is added first to the library board and then to the
    /// footprint (IPCB_Group.AddPCBObject remarks).
    /// </summary>
    internal static class ImportStepModelToFootprintCommands
    {
        /// <summary>
        /// ImportStepModel "File=&lt;.step&gt;|RotX=0|RotY=0|RotZ=0|X=0|Y=0|Z=0|Watch=true|Embed=true|false": adds one
        /// body to the current footprint. Angles in degrees, offsets in mils. The library is changed in memory and
        /// marked modified, not saved.
        /// </summary>
        [HotCommand("ImportStepModel", "Attach a STEP model to the current footprint as a 3D body")]
        public static string ImportStepModel(CommandContext ctx)
        {
            string fileValue = ctx.Get("File");
            if (string.IsNullOrWhiteSpace(fileValue))
                throw new UserException("Pass the STEP file: File=<path to a .step or .stp file>.");
            string file = Path.GetFullPath(fileValue.Trim().Trim('"'));
            if (!File.Exists(file))
                throw new UserException($"STEP file not found: {file}");

            double rotX = Number(ctx, "RotX"), rotY = Number(ctx, "RotY"), rotZ = Number(ctx, "RotZ");
            int dx = EDP.Utils.MilsToCoord(Number(ctx, "X"));
            int dy = EDP.Utils.MilsToCoord(Number(ctx, "Y"));
            int dz = EDP.Utils.MilsToCoord(Number(ctx, "Z"));
            // The .NET API names the second argument argWatchDirectory and the
            // reference does not describe it, so it stays True by default and can be changed for experiments.
            bool watch = Bool(ctx, "Watch") ?? true;
            // Embedding is the model's own property (IPCB_Model.Embed). Without Embed= the factory's value is kept.
            bool? embed = Bool(ctx, "Embed");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            IPCB_Library library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("No PCB library is active. Open and focus a .PcbLib with a footprint first.");
            IPCB_LibComponent footprint = library.GetState_CurrentComponent()
                ?? throw new UserException("The PCB library has no current footprint.");
            IPCB_Board board = footprint.GetState_Board() ?? library.GetState_Board()
                ?? throw new UserException("The current footprint has no board.");

            var bodySet = new PCB.TObjectSet { TObjectId.eComponentBodyObject };
            int bodiesBefore = footprint.GetPrimitiveCount(bodySet);
            string identifier = "STEPModel_" + Path.GetFileName(file);

            IPCB_ComponentBody body;
            bool inBoardSetByAdd, inComponentSetByAdd, footprintAddCalled;
            int bodiesAfterBoardAdd;
            pcbServer.PreProcess();
            try
            {
                IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentBodyObject,
                    TDimensionKind.eNoDimension, TObjectCreationMode.eCreate_Default);
                body = primitive as IPCB_ComponentBody;
                if (body == null)
                {
                    pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException("PCBObjectFactory did not create a component body.");
                }

                IPCB_Model model = LoadModel(body, file, watch);
                if (model == null)
                {
                    // No body is added when the model could not be loaded.
                    pcbServer.DestroyPCBObject(ref primitive);
                    throw new UserException($"ModelFactory_FromFilename could not load '{file}' as a 3D model. Nothing was added.");
                }

                // As the reference remarks for SetState describe: set the model's state, then assign it to the body.
                model.SetState(rotX, rotY, rotZ, dz);
                if (embed.HasValue)
                    model.SetEmbed(embed.Value);
                body.SetModel(model); // also recalculates the body's outline and heights from the model
                body.SetState_Identifier(identifier);
                body.MoveToXY(footprint.GetState_XLocation() + dx, footprint.GetState_YLocation() + dy);

                // Board first (IPCB_Group.AddPCBObject remarks). InBoard and InComponent are set by
                // hand only when AddPCBObject left them false; which case happened is reported in the result.
                body.SetState_Board(board);
                board.AddPCBObject(body);
                inBoardSetByAdd = body.GetState_InBoard();
                if (!inBoardSetByAdd)
                    body.SetState_InBoard(true);

                // On a library board, IPCB_Board.AddPCBObject already puts the primitive
                // into the current footprint. Calling IPCB_LibComponent.AddPCBObject as well (as
                // the IPCB_Group.AddPCBObject remarks do) lists the body twice in the footprint. So the footprint
                // add only runs when the board add did not already count the body.
                bodiesAfterBoardAdd = footprint.GetPrimitiveCount(bodySet);
                footprintAddCalled = bodiesAfterBoardAdd == bodiesBefore;
                if (footprintAddCalled)
                    footprint.AddPCBObject(body);
                inComponentSetByAdd = body.GetState_InComponent();
                if (!inComponentSetByAdd)
                    body.SetState_InComponent(true);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            string modified = MarkModified(board.GetState_FileName());
            int bodiesAfter = footprint.GetPrimitiveCount(bodySet);

            var lines = new List<string>
            {
                $"Added 3D body '{identifier}' to footprint '{footprint.GetState_Pattern()}' " +
                $"({bodiesBefore} bodies before, {bodiesAfter} after).",
                $"Reference point {Mils(footprint.GetState_XLocation())}, {Mils(footprint.GetState_YLocation())} mil; " +
                $"offset X={Mils(dx)} Y={Mils(dy)} mil; requested rotation {rotX}/{rotY}/{rotZ} deg, Z={Mils(dz)} mil; Watch={watch}.",
                Describe(body),
                $"Bodies in the footprint after IPCB_Board.AddPCBObject: {bodiesAfterBoardAdd}; " +
                $"IPCB_LibComponent.AddPCBObject called: {footprintAddCalled}.",
                $"InBoard after IPCB_Board.AddPCBObject: {inBoardSetByAdd}; InComponent after adding: " +
                $"{inComponentSetByAdd} (false = set by this command).",
                modified + " Save the library to keep the body, or close it without saving."
            };
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// ListBodies: read-back for ImportStepModel. Lists the 3D bodies of the current footprint with their
        /// identifier, model file, embed flag, rotation, Z offset and extent.
        /// </summary>
        [HotCommand("ListBodies", "List the 3D bodies of the current footprint")]
        public static string ListBodies(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            IPCB_Library library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("No PCB library is active. Open and focus a .PcbLib with a footprint first.");
            IPCB_LibComponent footprint = library.GetState_CurrentComponent()
                ?? throw new UserException("The PCB library has no current footprint.");

            int count = footprint.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
            var lines = new List<string> { $"Footprint '{footprint.GetState_Pattern()}': {count} 3D bodies." };
            // GetPrimitiveAt counts from 1 (reference: IPCB_Group.GetPrimitiveAt).
            for (int i = 1; i <= count; i++)
            {
                if (footprint.GetPrimitiveAt(i, TObjectId.eComponentBodyObject) is IPCB_ComponentBody body)
                    lines.Add($"  {i}: " + Describe(body));
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Whether the .NET wrapper of ModelFactory_FromFilename returns null or throws for an unreadable file
        /// is not documented, so both end in null here.
        /// </summary>
        private static IPCB_Model LoadModel(IPCB_ComponentBody body, string file, bool watch)
        {
            try
            {
                return body.ModelFactory_FromFilename(file, watch);
            }
            catch (COMException)
            {
                return null;
            }
        }

        /// <summary>One line: identifier, model file, embed flag, rotation, Z offset, extent and height.</summary>
        private static string Describe(IPCB_ComponentBody body)
        {
            string text = $"'{body.GetState_Identifier()}'";
            IPCB_Model model = body.GetModel();
            if (model == null)
                text += ", no model";
            else
            {
                model.GetState(out double rx, out double ry, out double rz, out int z);
                text += $", model file '{model.GetFileName()}', Embed={model.GetEmbed()}, " +
                        $"rotation {rx.ToString(CultureInfo.InvariantCulture)}/{ry.ToString(CultureInfo.InvariantCulture)}/" +
                        $"{rz.ToString(CultureInfo.InvariantCulture)} deg, Z={Mils(z)} mil";
            }
            CoordRect r = body.BoundingRectangle();
            if (r != null)
                text += $", extent X {Mils(r.Left)}..{Mils(r.Right)} Y {Mils(r.Bottom)}..{Mils(r.Top)} mil";
            return text + $", overall height {Mils(body.GetOverallHeight())} mil";
        }

        /// <summary>
        /// Marks the document of Board.FileName modified. For a library footprint that name is assumed
        /// to be the .PcbLib path; the focused document is the fallback.
        /// </summary>
        private static string MarkModified(string boardFileName)
        {
            IClient client = DXP.GlobalVars.Client;
            if (client == null)
                return "The document was not marked modified (no client).";

            IServerDocument document = string.IsNullOrEmpty(boardFileName) ? null : client.GetDocumentByPath(boardFileName);
            string via = "by its board file name";
            if (document == null)
            {
                document = client.GetCurrentView()?.GetOwnerDocument();
                via = "as the focused document (GetDocumentByPath('" + boardFileName + "') found none)";
            }
            if (document == null)
                return "No document found to mark modified.";

            document.SetModified(true);
            return $"Marked {Path.GetFileName(document.GetFileName())} modified, found {via}; Modified={document.GetModified()}.";
        }

        private static double Number(CommandContext ctx, string name)
        {
            string value = ctx.Get(name, "0");
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw new UserException($"{name} must be a number (got '{value}').");
            return result;
        }

        private static bool? Bool(CommandContext ctx, string name)
        {
            string value = ctx.Get(name);
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (!bool.TryParse(value.Trim(), out bool result))
                throw new UserException($"{name} must be true or false (got '{value}').");
            return result;
        }

        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
