using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DXP; // IClient.OpenDocument/ShowDocument/CloseDocument and IServerDocumentView.GetOwnerDocument are extension methods in namespace DXP
using PCB;

namespace AdSamples.PcbLibMerge
{
    /// <summary>
    /// Merges the footprints of every .PcbLib in a folder into one new PCB library. The SDK lesson: walk a library
    /// with LibraryIterator_Create, clone each footprint with ReplicateWithChildren, move the clone to the target
    /// library's board (SetState_Board), register it there with RegisterComponent, and remove the new library's
    /// default placeholder footprint once the first copy is in.
    /// </summary>
    internal static class PcbLibMergeCommands
    {
        private const string DefaultFileName = "PcbLibMerge.PcbLib";

        // Process, parameters and save kind: the SDK reference does not document them.
        private const string NewLibraryProcess = "WorkspaceManager:OpenObject";
        private const string NewLibraryParameters = "ObjectKind=NewAnything|Kind=DefaultPcbLib";
        private const string SaveKind = "PCB Library File";
        private const string OpenKind = "PCBLib";

        /// <summary>
        /// MergePcbLibs "Folder=&lt;folder&gt;|Out=&lt;folder or .PcbLib path&gt;|Overwrite=true|Duplicates=Rename|Skip|Reference=Center|Keep".
        /// From a menu click without Folder the folder is picked with a dialog.
        /// </summary>
        [HotCommand("MergePcbLibs", "Merge all PcbLib files of a folder into a new PcbLib")]
        public static string MergePcbLibs(CommandContext ctx)
        {
            // --- Parameters ---
            string folder = ctx.Get("Folder");
            if (string.IsNullOrWhiteSpace(folder) && ctx.IsInteractive)
                folder = PickFolder();
            if (string.IsNullOrWhiteSpace(folder))
                throw new UserException("No folder specified. Pass it as a parameter: Folder=<folder with .PcbLib files>.");
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder))
                throw new UserException($"Folder not found: {folder}");

            string target = TargetPath(ctx.Get("Out", Path.GetTempPath()));
            if (File.Exists(target) && !ctx.Flag("Overwrite"))
                throw new UserException($"'{target}' already exists. Pass Overwrite=true to replace it, or choose another Out.");

            string duplicates = ctx.Get("Duplicates", "Rename");
            bool skipDuplicates = duplicates.Equals("Skip", StringComparison.OrdinalIgnoreCase);
            if (!skipDuplicates && !duplicates.Equals("Rename", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Duplicates='{duplicates}': use Rename (default) or Skip.");

            string reference = ctx.Get("Reference", "Center");
            bool centerReference = reference.Equals("Center", StringComparison.OrdinalIgnoreCase);
            if (!centerReference && !reference.Equals("Keep", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Reference='{reference}': use Center (default) or Keep.");

            // Only the folder itself, not its subfolders. The output
            // file is skipped when it lies in the same folder, so a second run does not merge its own result.
            // The EndsWith filter is defensive: on Windows the search pattern is also matched against 8.3 short names
            // (see the Directory.GetFiles remarks), so only names that really end in ".PcbLib" are kept.
            string[] files = Directory.GetFiles(folder, "*.PcbLib", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".PcbLib", StringComparison.OrdinalIgnoreCase))
                .Where(f => !string.Equals(Path.GetFullPath(f), target, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
                throw new UserException($"No .PcbLib file found in {folder}. Nothing was created.");

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            string previousFile = client.GetCurrentView()?.GetOwnerDocument()?.GetFileName() ?? "";

            // --- 1. A new, empty PCB library, created by the workspace manager's own process ---
            DXP.Utils.RunCommand(NewLibraryProcess, NewLibraryParameters);

            IServerDocument document = client.GetCurrentView()?.GetOwnerDocument()
                ?? throw new UserException($"{NewLibraryProcess} left no document in focus. Nothing was merged.");
            // Check that the focus moved to a new PCB library, so a failed process cannot make the command merge into,
            // rename and save a library the user already had open.
            if (!string.Equals(document.GetKind(), "PCBLIB", StringComparison.OrdinalIgnoreCase) ||
                (previousFile.Length > 0 && string.Equals(document.GetFileName(), previousFile, StringComparison.OrdinalIgnoreCase)))
                throw new UserException(
                    $"{NewLibraryProcess} did not focus a new PCB library (focused: kind '{document.GetKind()}', " +
                    $"file '{document.GetFileName()}'). Nothing was merged.");

            // The PCB server runs now (it opened the library). Keep the new library and its document for the whole
            // command: opening the sources may change which library is "current", and saving whatever view
            // has the focus at the end would save the wrong document.
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB server is not running after creating the library.");
            IPCB_Library merged = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("The new document is not a PCB library. Nothing was merged.");
            IPCB_Board mergedBoard = merged.GetState_Board()
                ?? throw new UserException("The new PCB library has no board. Nothing was merged.");

            // A new library holds one default footprint. Remember it now instead of assuming
            // that CurrentComponent is still the placeholder after the first copy was registered.
            IPCB_LibComponent placeholder = merged.ComponentCount() == 1 ? merged.GetState_CurrentComponent() : null;
            string placeholderName = placeholder?.GetState_Pattern() ?? "(none)";
            ctx.Log($"New library created; default footprint '{placeholderName}'.");

            // --- 2. Copy every footprint of every source library ---
            var log = new List<string>();
            int librariesRead = 0, copied = 0, renamed = 0, skipped = 0, failed = 0;
            string lastName = null;
            var noPadNames = new List<string>(); // footprints without pads: Set Reference > Center skipped

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                ctx.Log($"[{i + 1}/{files.Length}] {Path.GetFileName(file)}");

                // A library the user already has open is read but not closed afterwards; the sources this command
                // opens are closed again.
                bool wasOpen = client.IsDocumentOpen(file);
                IServerDocument sourceDocument = client.OpenDocument(OpenKind, file);
                if (sourceDocument == null)
                {
                    log.Add($"{Path.GetFileName(file)}: could not be opened.");
                    failed++;
                    continue;
                }

                try
                {
                    IPCB_Library source = pcbServer.GetPCBLibraryByPath(file);
                    if (source == null)
                    {
                        log.Add($"{Path.GetFileName(file)}: no PCB library found for this path.");
                        failed++;
                        continue;
                    }
                    librariesRead++;

                    int fromThisLibrary = 0;
                    IPCB_LibraryIterator iterator = source.LibraryIterator_Create();
                    try
                    {
                        for (var footprint = iterator.FirstPCBObject() as IPCB_LibComponent;
                             footprint != null;
                             footprint = iterator.NextPCBObject() as IPCB_LibComponent)
                        {
                            string name = footprint.GetState_Pattern();

                            // RegisterComponent refuses a name the library already has (ignoring case, per the
                            // reference). Ignoring the result would silently lose such footprints.
                            string newName = name;
                            if (merged.GetComponentByName(name) != null)
                            {
                                if (skipDuplicates)
                                {
                                    log.Add($"{Path.GetFileName(file)}: skipped '{name}' (name already in the merged library).");
                                    skipped++;
                                    continue;
                                }
                                newName = merged.GetUniqueCompName(name);
                            }

                            // ReplicateWithChildren is declared on IPCB_Group and returns IPCB_Primitive: cast it back.
                            if (!(footprint.ReplicateWithChildren() is IPCB_LibComponent copy))
                            {
                                log.Add($"{Path.GetFileName(file)}: ReplicateWithChildren returned no footprint for '{name}'.");
                                failed++;
                                continue;
                            }

                            copy.SetState_Pattern(newName);
                            copy.SetState_Board(mergedBoard);
                            if (!merged.RegisterComponent(copy))
                            {
                                log.Add($"{Path.GetFileName(file)}: RegisterComponent refused '{newName}'.");
                                failed++;
                                continue;
                            }

                            if (newName != name)
                            {
                                log.Add($"{Path.GetFileName(file)}: '{name}' renamed to '{newName}' (name already in the merged library).");
                                renamed++;
                            }
                            copied++;
                            fromThisLibrary++;
                            lastName = newName;

                            // The placeholder goes once the library holds a real footprint.
                            if (placeholder != null)
                            {
                                merged.RemoveComponent(ref placeholder); // sets placeholder to null
                                ctx.Log($"Removed the default footprint '{placeholderName}'.");
                            }

                            AlignCopy(client, document, pcbServer, merged, copy, newName, centerReference, noPadNames);
                        }
                    }
                    finally
                    {
                        source.LibraryIterator_Destroy(ref iterator);
                    }

                    log.Add($"{Path.GetFileName(file)}: {fromThisLibrary} footprints copied.");
                }
                finally
                {
                    if (!wasOpen)
                        client.CloseDocument(sourceDocument);
                }
            }

            // --- 3. Show the last footprint, measure it, zoom to fit and save ---
            // (With Reference=Center AlignCopy already zoomed per footprint; with Keep only here.)
            string position = null;
            if (lastName != null)
            {
                position = DescribePosition(merged, lastName); // also makes lastName the current footprint
                mergedBoard.ViewManager_FullUpdate();
                RunInLibrary(client, document, "PCB:Zoom", "Action=All");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            document.SetFileName(target);
            bool saved = document.DoFileSave(SaveKind);

            var summary = new List<string>
            {
                $"Merged {folder}: {files.Length} PcbLib files, {librariesRead} read, {copied} footprints copied " +
                $"({renamed} renamed, {skipped} skipped as duplicates, {failed} failed).",
                $"New library '{target}' (DoFileSave: {saved}): {merged.ComponentCount()} footprints" +
                (placeholder != null ? $", still holding the default footprint '{placeholderName}' because nothing was copied." : "."),
            };
            if (noPadNames.Count > 0)
                summary.Add($"Set Reference > Center skipped for {noPadNames.Count} footprints without pads (copied unchanged): " +
                            string.Join(", ", noPadNames));
            if (position != null)
                summary.Add(position);
            summary.AddRange(log);
            summary.Add("Footprints in the new library: " + string.Join(", ", FootprintNames(merged)));
            return string.Join(Environment.NewLine, summary);
        }

        /// <summary>
        /// ListFootprints "Lib=&lt;path to .PcbLib&gt;": read-back helper. Lists the footprints of a
        /// library file and closes it again if this command opened it.
        /// </summary>
        [HotCommand("ListFootprints", "List the footprints of a PcbLib file (read-back check)")]
        public static string ListFootprints(CommandContext ctx)
        {
            string file = ctx.Get("Lib");
            if (string.IsNullOrWhiteSpace(file))
                throw new UserException("No library specified. Pass it as a parameter: Lib=<path to .PcbLib>.");
            file = Path.GetFullPath(file);
            if (!File.Exists(file))
                throw new UserException($"File not found: {file}");

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");
            bool wasOpen = client.IsDocumentOpen(file);
            IServerDocument document = client.OpenDocument(OpenKind, file)
                ?? throw new UserException($"Altium Designer could not open {file} as a PCB library.");
            try
            {
                IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                    ?? throw new UserException("The PCB editor is not running.");
                IPCB_Library library = pcbServer.GetPCBLibraryByPath(file)
                    ?? throw new UserException($"No PCB library found for {file}.");
                List<string> names = FootprintNames(library);
                return $"{Path.GetFileName(file)}: {names.Count} footprints (ComponentCount {library.ComponentCount()})" +
                       Environment.NewLine + string.Join(Environment.NewLine, names);
            }
            finally
            {
                if (!wasOpen)
                    client.CloseDocument(document);
            }
        }

        /// <summary>
        /// Per-footprint alignment: make the copy current, assign its XLocation / YLocation the library
        /// board's origin, then (Reference=Center) zoom and run the editor's "Set Reference > Center" process.
        /// Footprints with pads end up centred on the origin; footprints without pads are skipped, see below.
        /// </summary>
        private static void AlignCopy(IClient client, IServerDocument document, IPCB_ServerInterface pcbServer,
            IPCB_Library merged, IPCB_LibComponent copy, string name, bool centerReference, List<string> skippedCenter)
        {
            // Move the footprint the library shows after SetBoardToComponentByName. It should be the registered copy;
            // using CurrentComponent makes sure the change goes to the footprint the processes below act on.
            IPCB_LibComponent current = merged.SetBoardToComponentByName(name)
                ? merged.GetState_CurrentComponent() ?? copy
                : copy;

            // XLocation / YLocation of the footprint; the reference says that
            // assigning them moves the group with MoveToXY. Bracketed like any other PCB change.
            IPCB_Board board = current.GetState_Board();
            if (board != null)
            {
                pcbServer.PreProcess();
                try
                {
                    current.BeginModify();
                    current.SetState_XLocation(board.GetState_XOrigin());
                    current.SetState_YLocation(board.GetState_YOrigin());
                    current.EndModify();
                }
                finally
                {
                    pcbServer.PostProcess();
                }
            }

            // On a footprint without pads (for example a logo made of tracks)
            // Set Reference > Center moves the footprint by the library board's origin offset (XLocation/YLocation
            // go from (0, 0) to (50000, 50000) mil relative to the origin), far away from the origin. Such footprints
            // are therefore copied unchanged. It centres footprints with pads correctly.
            if (centerReference && current.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.ePadObject }) == 0)
            {
                skippedCenter?.Add(name);
                centerReference = false;
            }

            if (centerReference)
            {
                // PCB:SetComponentReference is an editor process: it works on what the library editor shows. A zoom
                // right before it also makes the editor show the footprint just made current.
                // Refresh the panels and view first (ViewManager_FullUpdate, documented as useful after programmatic
                // library changes), then zoom and centre.
                board?.ViewManager_FullUpdate();
                RunInLibrary(client, document, "PCB:Zoom", "Action=All");
                RunInLibrary(client, document, "PCB:SetComponentReference", "Location=CENTER");
            }
        }

        /// <summary>
        /// Read-back for the alignment: the current footprint's bounding rectangle relative to the library board's
        /// origin, in mil. BoundingRectangle returns 0,0,0,0 for footprints that are not the
        /// library's current one, so the caller makes the footprint current first. After
        /// Set Reference > Center the BoundingRectangle values stay the same while XLocation/YLocation change (also in
        /// a later command), so treat the bounding box as unreliable for checking the centring; check it visually.
        /// </summary>
        private static string DescribePosition(IPCB_Library library, string name)
        {
            if (!library.SetBoardToComponentByName(name))
                return $"Footprint '{name}' could not be made current.";
            IPCB_LibComponent current = library.GetState_CurrentComponent();
            IPCB_Board board = library.GetState_Board();
            if (current == null || board == null)
                return $"Footprint '{name}': no current footprint or board to measure.";

            CoordRect r = current.BoundingRectangle();
            int ox = board.GetState_XOrigin(), oy = board.GetState_YOrigin();
            static string Mil(int coord) => (coord / 10000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return $"Last footprint '{name}' relative to the library origin (mil): " +
                   $"BoundingRectangle (may be stale after Set Reference) X {Mil(r.GetLeft() - ox)}..{Mil(r.GetRight() - ox)}, Y {Mil(r.GetBottom() - oy)}..{Mil(r.GetTop() - oy)}, " +
                   $"centre ({Mil((r.GetLeft() + r.GetRight()) / 2 - ox)}, {Mil((r.GetBottom() + r.GetTop()) / 2 - oy)}); " +
                   $"XLocation/YLocation ({Mil(current.GetState_XLocation() - ox)}, {Mil(current.GetState_YLocation() - oy)}).";
        }

        /// <summary>Runs a PCB process with the new library in focus (opening a source may have moved the focus).</summary>
        private static void RunInLibrary(IClient client, IServerDocument document, string process, string parameters)
        {
            IServerDocument focused = client.GetCurrentView()?.GetOwnerDocument();
            if (!ReferenceEquals(focused, document) &&
                !string.Equals(focused?.GetFileName(), document.GetFileName(), StringComparison.OrdinalIgnoreCase))
                client.ShowDocument(document);
            DXP.Utils.RunCommand(process, parameters);
        }

        private static List<string> FootprintNames(IPCB_Library library)
        {
            var names = new List<string>();
            IPCB_LibraryIterator iterator = library.LibraryIterator_Create();
            try
            {
                for (var footprint = iterator.FirstPCBObject() as IPCB_LibComponent;
                     footprint != null;
                     footprint = iterator.NextPCBObject() as IPCB_LibComponent)
                    names.Add(footprint.GetState_Pattern());
            }
            finally
            {
                library.LibraryIterator_Destroy(ref iterator);
            }
            return names;
        }

        /// <summary>Out is a .PcbLib path, or a folder that gets the default file name.</summary>
        private static string TargetPath(string outValue)
        {
            string path = Path.GetFullPath(outValue);
            return string.Equals(Path.GetExtension(path), ".PcbLib", StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.Combine(path, DefaultFileName);
        }

        private static string PickFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Folder with the .PcbLib files to merge",
                UseDescriptionForTitle = true,
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
        }
    }
}
