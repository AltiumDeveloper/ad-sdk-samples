using System;
using System.IO;
using DXP; // IServerDocumentView.GetOwnerDocument is an extension method in namespace DXP
using PCB;

namespace AdSamples.CreatePcbLib
{
    /// <summary>
    /// Creates a new PCB library, saves it, and adds a footprint holding one extruded 3D component body. The SDK
    /// lesson: create the document with the workspace manager's process, then build the footprint with
    /// CreatePCBLibComp / RegisterComponent, create the body with PCBObjectFactory and give it an extruded model
    /// (ModelFactory_CreateExtruded) whose outline is the body's main contour.
    /// </summary>
    internal static class CreatePcbLibCommands
    {
        // Process, parameters and save kind: the SDK reference does not document them.
        private const string NewLibraryProcess = "WorkspaceManager:OpenObject";
        private const string NewLibraryParameters = "ObjectKind=NewAnything|Kind=DefaultPcbLib";
        private const string SaveKind = "PCB Library File";

        // The geometry, written with named units: 1000000 / 2000000 / 100000
        // internal units (10,000 per mil): a 100 mil square from (100, 100) to (200, 200) mil, 10 mil high.
        private static readonly int BodyLow = EDP.Utils.MilsToCoord(100);
        private static readonly int BodyHigh = EDP.Utils.MilsToCoord(200);
        private static readonly int BodyHeight = EDP.Utils.MilsToCoord(10);

        // The colour format of BodyColor3D and argColor is not documented.
        private const uint BodyColor = 255;
        private const uint ModelColor = 0;
        private const string BodyIdentifier = "Test";

        /// <summary>
        /// CreatePcbLib "Out=&lt;folder&gt;|Name=&lt;file name&gt;|Pattern=&lt;footprint name&gt;|Overwrite=true":
        /// creates Out\Name (default: temp folder, test.PcbLib) with the footprint Pattern (default TestComp).
        /// </summary>
        [HotCommand("CreatePcbLib", "Create a PcbLib with a footprint and an extruded 3D body")]
        public static string CreatePcbLib(CommandContext ctx)
        {
            string pattern = ctx.Get("Pattern", "TestComp");
            if (string.IsNullOrWhiteSpace(pattern))
                throw new UserException("The footprint name is empty. Pass it as a parameter: Pattern=<footprint name>.");

            string fileName = ctx.Get("Name", "test.PcbLib");
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new UserException($"'{fileName}' is not a valid file name. Pass Name=<file name>.PcbLib.");
            if (!string.Equals(Path.GetExtension(fileName), ".PcbLib", StringComparison.OrdinalIgnoreCase))
                fileName += ".PcbLib";

            string target = Path.Combine(Path.GetFullPath(ctx.Get("Out", Path.GetTempPath())), fileName);
            if (File.Exists(target) && !ctx.Flag("Overwrite"))
                throw new UserException($"'{target}' already exists. Pass Overwrite=true to replace it, or choose another Out/Name.");

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // Remember the focused document, so the command can tell whether the process really focused a new one.
            string previousFile = client.GetCurrentView()?.GetOwnerDocument()?.GetFileName() ?? "";

            // 1. A new, empty PCB library, created by the workspace manager's own process.
            DXP.Utils.RunCommand(NewLibraryProcess, NewLibraryParameters);

            IServerDocument document = client.GetCurrentView()?.GetOwnerDocument()
                ?? throw new UserException($"{NewLibraryProcess} left no document in focus. Nothing was created.");
            // Check the kind and that the focus moved, so a failed process does not
            // rename and save a PCB library the user already had open.
            if (!string.Equals(document.GetKind(), "PCBLIB", StringComparison.OrdinalIgnoreCase) ||
                (previousFile.Length > 0 && string.Equals(document.GetFileName(), previousFile, StringComparison.OrdinalIgnoreCase)))
                throw new UserException(
                    $"{NewLibraryProcess} did not focus a new PCB library (focused: kind '{document.GetKind()}', " +
                    $"file '{document.GetFileName()}'). Nothing was created.");

            // 2. Name the new document and save it. This happens before the footprint is added.
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            document.SetFileName(target);
            bool saved = document.DoFileSave(SaveKind);
            ctx.Log($"New library saved as {target}: {saved}.");

            // 3. The PCB server runs now (it opened the library). Re-query the current library and check it is ours.
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB server is not running after creating the library.");
            IPCB_Library library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("The focused document is not a PCB library. Nothing was added.");
            string libraryFile = library.GetState_Board()?.GetState_FileName();
            if (!string.Equals(libraryFile, target, StringComparison.OrdinalIgnoreCase))
                throw new UserException(
                    $"The current PCB library is '{libraryFile}', not the new '{target}'. Nothing was added.");

            // 4. A new footprint, registered with the library. RegisterComponent returns false for a refused name.
            int before = library.ComponentCount();
            IPCB_LibComponent footprint = pcbServer.CreatePCBLibComp()
                ?? throw new UserException("CreatePCBLibComp returned no footprint.");
            footprint.SetState_Pattern(pattern);
            if (!library.RegisterComponent(footprint))
                throw new UserException(
                    $"RegisterComponent refused the footprint '{pattern}' ({before} footprints in the library; " +
                    $"a free name would be '{library.GetUniqueCompName(pattern)}').");

            // 5. The 3D body, added to the footprint inside the PCB change bracket.
            IPCB_ComponentBody body;
            pcbServer.PreProcess();
            try
            {
                body = CreateExtrudedBody(pcbServer);
                footprint.AddPCBObject(body);
            }
            finally
            {
                pcbServer.PostProcess();
            }

            // Mark the document of the current board modified; in a library that is the library
            // document itself, which is what we hold already.
            document.SetModified(true);

            // 6. Show the new footprint and zoom to it.
            bool shown = library.SetBoardToComponentByName(pattern);
            DXP.Utils.RunCommand("PCB:Zoom", "Action=All");

            return Summary(target, saved, library, footprint, body, before, shown);
        }

        /// <summary>
        /// A component body with an extruded model. The body's main contour (it is a region) is the outline that is
        /// extruded; the model gives the extrusion its Z range.
        /// </summary>
        private static IPCB_ComponentBody CreateExtrudedBody(IPCB_ServerInterface pcbServer)
        {
            var body = pcbServer.PCBObjectFactory(TObjectId.eComponentBodyObject, TDimensionKind.eNoDimension,
                           TObjectCreationMode.eCreate_Default) as IPCB_ComponentBody
                ?? throw new UserException("PCBObjectFactory did not create a component body.");

            // The outline first: the reference (IPCB_ComponentBody.ModelFactory_CreateExtruded) says the call extrudes
            // the body's outline only when it has an area, otherwise the model gets just the Z range.
            // A closed square: five points, the last one repeats the first.
            IPCB_Contour contour = body.GetMainContour()
                ?? throw new UserException("The new component body has no main contour.");
            int[,] points = { { BodyLow, BodyLow }, { BodyHigh, BodyLow }, { BodyHigh, BodyHigh }, { BodyLow, BodyHigh }, { BodyLow, BodyLow } };
            contour.SetState_Count(points.GetLength(0));
            for (int i = 0; i < points.GetLength(0); i++)
            {
                contour.SetState_PointX(i, points[i, 0]);
                contour.SetState_PointY(i, points[i, 1]);
            }

            // Z range from 0 (standoff) to BodyHeight, in internal units.
            IPCB_Model model = body.ModelFactory_CreateExtruded(0, BodyHeight, ModelColor)
                ?? throw new UserException("ModelFactory_CreateExtruded returned no model.");
            model.SetState(0, 0, 0, 0); // no rotation (degrees), no Z offset

            // ModelFactory_CreateExtruded already sets both heights (reference remarks); they are set
            // again explicitly so the body's properties are visible in the code.
            body.SetOverallHeight(BodyHeight);
            body.SetModel(model);
            body.SetStandoffHeight(0);
            body.SetBodyColor3D(BodyColor);
            body.SetState_Identifier(BodyIdentifier);
            return body;
        }

        /// <summary>Reads the result back from the library, so the outcome is checkable from the text alone.</summary>
        private static string Summary(string target, bool saved, IPCB_Library library, IPCB_LibComponent footprint,
            IPCB_ComponentBody body, int before, bool shown)
        {
            int bodies = footprint.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
            IPCB_Contour contour = body.GetMainContour();
            // Read back after SetBoardToComponentByName, the contour points are board coordinates:
            // they come back offset by the library board's origin (1270 mm), so the outline is reported relative to
            // XOrigin/YOrigin.
            IPCB_Board board = library.GetState_Board();
            int ox = board?.GetState_XOrigin() ?? 0, oy = board?.GetState_YOrigin() ?? 0;
            string outline = contour == null || contour.GetState_Count() == 0
                ? "none"
                : $"{contour.GetState_Count()} points, ({EDP.Utils.CoordToMMs(contour.GetState_PointX(0) - ox):0.###}, " +
                  $"{EDP.Utils.CoordToMMs(contour.GetState_PointY(0) - oy):0.###}) mm to " +
                  $"({EDP.Utils.CoordToMMs(contour.GetState_PointX(2) - ox):0.###}, {EDP.Utils.CoordToMMs(contour.GetState_PointY(2) - oy):0.###}) mm " +
                  $"from the library origin, which is at ({EDP.Utils.CoordToMMs(ox):0.###}, {EDP.Utils.CoordToMMs(oy):0.###}) mm absolute";
            IPCB_LibComponent current = library.GetState_CurrentComponent();

            return $"Created '{target}' (DoFileSave: {saved}) with footprint '{footprint.GetState_Pattern()}'.\n" +
                   $"Footprints: {before} before, {library.ComponentCount()} after; current footprint " +
                   $"'{current?.GetState_Pattern() ?? "none"}' (SetBoardToComponentByName: {shown}).\n" +
                   $"Component bodies in the footprint: {bodies}. Body '{body.GetState_Identifier()}': layer {body.GetState_Layer()}, " +
                   $"overall height {EDP.Utils.CoordToMMs(body.GetOverallHeight()):0.###} mm, standoff " +
                   $"{EDP.Utils.CoordToMMs(body.GetStandoffHeight()):0.###} mm, colour {body.GetBodyColor3D()}, " +
                   $"model {(body.GetModel() != null ? "set" : "missing")}, outline {outline}.\n" +
                   "The file was saved before the footprint was added: save the library to keep it, or close it without saving.";
        }
    }
}
