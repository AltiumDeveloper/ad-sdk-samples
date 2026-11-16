using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DXP; // the IClient.OpenDocument/ShowDocument/CloseDocument extension methods live in namespace DXP
using PCB;

namespace AdSamples.PcbLibAssignStepModels
{
    /// <summary>
    /// Batch-assigns STEP models to the footprints of PCB libraries by name. The SDK lessons: open a library from code
    /// (IClient.OpenDocument), walk its footprints (IPCB_Library.GetComponent), collect a footprint's 3D bodies with a
    /// group iterator before removing them (IPCB_Group.RemovePCBObject), build a new body from a STEP file
    /// (PCBObjectFactory + IPCB_ComponentBody.ModelFactory_FromFilename + IPCB_Model.SetState), add it, then save
    /// (IServerDocument.DoFileSave) and close the library.
    /// </summary>
    internal static class PcbLibAssignStepModelsCommands
    {
        // The footprint name is shortened by one character per round, for 5 rounds:
        // the full name and up to four shorter prefixes are tried.
        private const int MaxRounds = 5;

        /// <summary>
        /// AssignStepModels "Libs=a.PcbLib;b.PcbLib|StepFolder=&lt;folder&gt;|RotX=0|RotY=0|RotZ=0|Z=0|Subfolders|KeepExisting".
        /// Libraries are changed and saved in place: run it on copies.
        /// </summary>
        [HotCommand("AssignStepModels", "Assign STEP models to PcbLib footprints by name prefix")]
        public static string AssignStepModels(CommandContext ctx)
        {
            List<string> libs = LibraryList(ctx.Get("Libs"));

            string folderValue = ctx.Get("StepFolder");
            if (string.IsNullOrWhiteSpace(folderValue))
                throw new UserException("No STEP folder specified. Pass StepFolder=<folder with .step/.stp files>.");
            string stepFolder = Path.GetFullPath(folderValue.Trim().Trim('"'));
            if (!Directory.Exists(stepFolder))
                throw new UserException($"STEP folder not found: {stepFolder}");

            double rotX = Number(ctx, "RotX"), rotY = Number(ctx, "RotY"), rotZ = Number(ctx, "RotZ");
            // IPCB_Model.SetState takes the Z offset in internal units (10,000 per mil); the parameter is in mils.
            int dz = EDP.Utils.MilsToCoord(Number(ctx, "Z"));
            bool subfolders = ctx.Flag("Subfolders");
            bool keepExisting = ctx.Flag("KeepExisting");

            // The STEP list is a scan of the folder.
            SearchOption scope = subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            List<string> stepFiles = Directory.EnumerateFiles(stepFolder, "*.*", scope)
                .Where(f => f.EndsWith(".step", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".stp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (stepFiles.Count == 0)
                throw new UserException($"No .step or .stp file found in {stepFolder}" + (subfolders ? " or its subfolders." : "."));

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            var settings = new Settings(stepFiles, rotX, rotY, rotZ, dz, keepExisting);
            var report = new List<string>
            {
                $"{stepFiles.Count} STEP files in {stepFolder}{(subfolders ? " (with subfolders)" : "")}; " +
                $"rotation {Fmt(rotX)}/{Fmt(rotY)}/{Fmt(rotZ)} deg, Z offset {Mils(dz)} mil; " +
                (keepExisting ? "existing bodies kept." : "existing bodies removed on a match.")
            };

            int assigned = 0, unmatched = 0, failed = 0;
            for (int i = 0; i < libs.Count; i++)
            {
                ctx.Log($"[{i + 1}/{libs.Count}] {Path.GetFileName(libs[i])}");
                LibraryResult result = ProcessLibrary(ctx, client, libs[i], settings);
                report.AddRange(result.Lines);
                assigned += result.Assigned;
                unmatched += result.Unmatched;
                failed += result.Failed;
            }

            report.Insert(1, $"{libs.Count} libraries: {assigned} footprints got a model, {unmatched} had no match, {failed} failed.");
            return string.Join(Environment.NewLine, report);
        }

        /// <summary>
        /// ListBodies "Lib=&lt;path to .PcbLib&gt;": read-back helper. Lists every footprint with its 3D
        /// bodies (model file, rotation, Z offset, extent relative to the footprint reference point). Opens the library
        /// when it is not open and closes it again without saving.
        /// </summary>
        [HotCommand("ListBodies", "List the 3D bodies of each footprint of a PcbLib (read-back)")]
        public static string ListBodies(CommandContext ctx)
        {
            string value = ctx.Get("Lib");
            if (string.IsNullOrWhiteSpace(value))
                throw new UserException("No library specified. Pass Lib=<path to .PcbLib>.");
            string file = Path.GetFullPath(value.Trim().Trim('"'));
            if (!File.Exists(file))
                throw new UserException($"File not found: {file}");

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");
            bool wasOpen = client.IsDocumentOpen(file);
            IServerDocument document = client.OpenDocument("PcbLib", file)
                ?? throw new UserException($"Altium Designer could not open {file} as a PCB library.");
            try
            {
                IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                    ?? throw new UserException("The PCB editor is not running.");
                IPCB_Library library = pcbServer.GetPCBLibraryByPath(file);
                if (library == null)
                {
                    client.ShowDocument(document);
                    library = CurrentLibraryIfItIs(pcbServer, file)
                        ?? throw new UserException($"No PCB library found for {file}.");
                }

                int count = library.ComponentCount();
                var lines = new List<string> { $"{Path.GetFileName(file)}: {count} footprints." };
                for (int j = 0; j < count; j++)
                {
                    IPCB_LibComponent footprint = library.GetComponent(j);
                    if (footprint == null)
                        continue;
                    List<IPCB_ComponentBody> bodies = CollectBodies(footprint);
                    lines.Add($"{footprint.GetState_Pattern()}: {bodies.Count} 3D bodies");
                    foreach (IPCB_ComponentBody body in bodies)
                        lines.Add("  " + Describe(body, footprint));
                }
                return string.Join(Environment.NewLine, lines);
            }
            finally
            {
                if (!wasOpen)
                    client.CloseDocument(document);
            }
        }

        private sealed record Settings(List<string> StepFiles, double RotX, double RotY, double RotZ, int Dz, bool KeepExisting);

        private sealed class LibraryResult
        {
            public readonly List<string> Lines = new();
            public int Assigned, Unmatched, Failed;
        }

        private static LibraryResult ProcessLibrary(CommandContext ctx, IClient client, string file, Settings settings)
        {
            var result = new LibraryResult();
            if (!File.Exists(file))
            {
                result.Lines.Add($"{file}: file not found.");
                result.Failed++;
                return result;
            }
            // CloseDocument below would close a library the user has open, together with any unsaved edits.
            if (client.IsDocumentOpen(file))
            {
                result.Lines.Add($"{file}: skipped, it is open in Altium Designer. Close it and run again.");
                result.Failed++;
                return result;
            }

            IServerDocument document = client.OpenDocument("PcbLib", file);
            if (document == null)
            {
                result.Lines.Add($"{file}: Altium Designer could not open it as a PCB library.");
                result.Failed++;
                return result;
            }

            try
            {
                // Showing the library makes it the current view: PreProcess/PostProcess act on the current view's
                // document, and the library is taken from GetCurrentPCBLibrary after ShowDocument.
                client.ShowDocument(document);
                IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer; // the PCB editor may load with the first PCB document
                IPCB_Library library = pcbServer == null ? null
                    : CurrentLibraryIfItIs(pcbServer, file) ?? pcbServer.GetPCBLibraryByPath(file);
                if (library == null)
                {
                    result.Lines.Add($"{file}: no PCB library found after opening it.");
                    result.Failed++;
                    return result;
                }

                result.Lines.Add($"{file}: {library.ComponentCount()} footprints");
                int changed = 0;
                pcbServer.PreProcess();
                try
                {
                    int count = library.ComponentCount();
                    for (int j = 0; j < count; j++)
                    {
                        IPCB_LibComponent footprint = library.GetComponent(j);
                        if (footprint == null)
                            continue;
                        string line = AssignOne(pcbServer, library, footprint, settings, out bool? ok);
                        result.Lines.Add("  " + line);
                        ctx.Log(line);
                        if (ok == true) { result.Assigned++; changed++; }
                        else if (ok == null) result.Unmatched++;
                        else result.Failed++;
                    }
                }
                finally
                {
                    pcbServer.PostProcess();
                }

                // Only a library with a change is saved, and the save result is checked.
                if (changed == 0)
                    result.Lines.Add("  nothing changed, not saved.");
                else if (document.DoFileSave("PcbLib"))
                    result.Lines.Add($"  saved ({changed} footprints changed).");
                else
                {
                    result.Lines.Add($"  DoFileSave failed: the {changed} changes are lost when the library closes.");
                    result.Failed++;
                }
                return result;
            }
            finally
            {
                client.CloseDocument(document);
            }
        }

        /// <summary>
        /// One footprint: find a STEP file, then replace the footprint's bodies with one made from it.
        /// ok = true (assigned), null (no match), false (the file matched but could not be used).
        /// </summary>
        private static string AssignOne(IPCB_ServerInterface pcbServer, IPCB_Library library, IPCB_LibComponent footprint,
            Settings settings, out bool? ok)
        {
            // IPCB_LibComponent has no Name in the .NET API: the footprint name is Pattern.
            string name = footprint.GetState_Pattern() ?? "";
            string stepFile = FindStepFile(name, settings.StepFiles, out string prefix);
            if (stepFile == null)
            {
                ok = null;
                return $"{name}: no match.";
            }

            // Collect first, remove later: removing children while a group iterator walks them changes the list
            // under the iterator.
            List<IPCB_ComponentBody> existing = CollectBodies(footprint);

            // The footprint is deliberately NOT made the library's CurrentComponent: a
            // body removed with RemovePCBObject from the current footprint comes back after the save (the library
            // board still holds it, and IPCB_Board.RemovePCBObject does not remove primitives from a library board),
            // while removing it from a footprint that is not current persists. Adding with the footprint's
            // AddPCBObject alone persists too.

            IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentBodyObject,
                TDimensionKind.eNoDimension, TObjectCreationMode.eCreate_Default);
            if (primitive is not IPCB_ComponentBody body)
            {
                if (primitive != null)
                    pcbServer.DestroyPCBObject(ref primitive);
                ok = false;
                return $"{name}: PCBObjectFactory did not create a component body.";
            }

            // Load the model before touching the existing bodies, so a bad file leaves the footprint as it was.
            IPCB_Model model = LoadModel(body, stepFile);
            if (model == null)
            {
                pcbServer.DestroyPCBObject(ref primitive);
                ok = false;
                return $"{name}: '{Path.GetFileName(stepFile)}' matched but could not be loaded as a 3D model; footprint unchanged.";
            }

            int removed = 0;
            if (!settings.KeepExisting)
            {
                foreach (IPCB_ComponentBody old in existing)
                {
                    footprint.RemovePCBObject(old);
                    removed++;
                }
            }

            // Set the model's state, then assign it: assigning recalculates the body's outline and heights from the
            // model (IPCB_ComponentBody.Model remarks), so no SetState_FromModel call is needed before SetModel.
            model.SetState(settings.RotX, settings.RotY, settings.RotZ, settings.Dz);
            // Read back into locals, so a rounding difference does not carry over to the next footprint.
            model.GetState(out double rx, out double ry, out double rz, out int z);
            body.SetModel(model);

            // A new body sits at the board origin (coordinate 0,0), not at the footprint's reference point:
            // the unmoved body ends up 50000 mil from the reference point of a library footprint. Move it there.
            body.MoveByXY(footprint.GetState_XLocation(), footprint.GetState_YLocation());
            footprint.AddPCBObject(body);

            ok = true;
            string match = prefix.Length == name.Length ? "full name" : $"prefix '{prefix}'";
            return $"{name}: '{Path.GetFileName(stepFile)}' ({match}); removed {removed} of {existing.Count} bodies; " +
                   $"model state read back {Fmt(rx)}/{Fmt(ry)}/{Fmt(rz)} deg, Z {Mils(z)} mil.";
        }

        /// <summary>
        /// The match: a STEP file whose name starts with the footprint name (case-insensitive); if none,
        /// the name shortened by one character, up to MaxRounds rounds. Files are tried in sorted order, the first
        /// match wins. The file name is compared without its extension.
        /// </summary>
        private static string FindStepFile(string name, List<string> stepFiles, out string prefix)
        {
            for (int round = 0; round < MaxRounds && round < name.Length; round++)
            {
                prefix = name.Substring(0, name.Length - round);
                foreach (string file in stepFiles)
                {
                    if (Path.GetFileNameWithoutExtension(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            prefix = "";
            return null;
        }

        private static List<IPCB_ComponentBody> CollectBodies(IPCB_LibComponent footprint)
        {
            var bodies = new List<IPCB_ComponentBody>();
            IPCB_GroupIterator iterator = footprint.GroupIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
                for (IPCB_Primitive p = iterator.FirstPCBObject(); p != null; p = iterator.NextPCBObject())
                {
                    if (p is IPCB_ComponentBody body)
                        bodies.Add(body);
                }
            }
            finally
            {
                footprint.GroupIterator_Destroy(ref iterator);
            }
            return bodies;
        }

        /// <summary>
        /// The second argument (argWatchDirectory) is not described in the reference; it is passed as false.
        /// Whether the .NET wrapper returns null or throws for an unreadable file is not documented: both end in null.
        /// </summary>
        private static IPCB_Model LoadModel(IPCB_ComponentBody body, string file)
        {
            try
            {
                return body.ModelFactory_FromFilename(file, false);
            }
            catch (COMException)
            {
                return null;
            }
        }

        private static string Describe(IPCB_ComponentBody body, IPCB_LibComponent footprint)
        {
            string text = $"'{body.GetState_Identifier()}'";
            IPCB_Model model = body.GetModel();
            if (model == null)
                text += ", no model";
            else
            {
                model.GetState(out double rx, out double ry, out double rz, out int z);
                text += $", model file '{model.GetFileName()}', rotation {Fmt(rx)}/{Fmt(ry)}/{Fmt(rz)} deg, Z {Mils(z)} mil";
            }
            CoordRect r = body.BoundingRectangle();
            if (r != null)
            {
                int x0 = footprint.GetState_XLocation(), y0 = footprint.GetState_YLocation();
                text += $", extent from reference point X {Mils(r.Left - x0)}..{Mils(r.Right - x0)} " +
                        $"Y {Mils(r.Bottom - y0)}..{Mils(r.Top - y0)} mil";
            }
            return text;
        }

        private static List<string> LibraryList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new UserException("No libraries specified. Pass Libs=<.PcbLib path>[;<.PcbLib path>...].");
            List<string> libs = value.Split(';')
                .Select(s => s.Trim().Trim('"'))
                .Where(s => s.Length > 0)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (libs.Count == 0)
                throw new UserException("No libraries specified. Pass Libs=<.PcbLib path>[;<.PcbLib path>...].");
            return libs;
        }

        /// <summary>The current PCB library when it is the given file, otherwise null.</summary>
        private static IPCB_Library CurrentLibraryIfItIs(IPCB_ServerInterface pcbServer, string file)
        {
            IPCB_Library current = pcbServer.GetCurrentPCBLibrary();
            string currentFile = current?.GetState_Board()?.GetState_FileName();
            return !string.IsNullOrEmpty(currentFile) &&
                   string.Equals(Path.GetFullPath(currentFile), file, StringComparison.OrdinalIgnoreCase)
                ? current
                : null;
        }

        private static double Number(CommandContext ctx, string name)
        {
            string value = ctx.Get(name, "0");
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw new UserException($"{name} must be a number (got '{value}').");
            return result;
        }

        private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
