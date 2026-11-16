using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DXP; // the IClient.OpenDocument/ShowDocument/CloseDocument extension methods live in namespace DXP
using PCB;

namespace AdSamples.PcbLibRenameFootprintToFileName
{
    /// <summary>
    /// Batch-edits PCB libraries from code: open each .PcbLib of a folder with IClient.OpenDocument, take its
    /// IPCB_Library, rename the current footprint (IPCB_LibComponent.Pattern is the footprint name) to the file name,
    /// save with IServerDocument.DoFileSave and close the document again.
    /// </summary>
    internal static class PcbLibRenameFootprintToFileNameCommands
    {
        // Name of the log file written to the Out folder.
        private const string ResultFileName = "Result_ChangePCBLibComponentName.txt";

        /// <summary>
        /// RenameFootprintsToFileName "Folder=&lt;folder&gt;|MultiFootprint=Skip|Current|Out=&lt;folder&gt;|Open=true".
        /// From a menu click without Folder the folder is picked with a dialog.
        /// </summary>
        [HotCommand("RenameFootprintsToFileName", "Rename each PcbLib footprint in a folder to the file name")]
        public static string RenameFootprintsToFileName(CommandContext ctx)
        {
            string folder = ctx.Get("Folder");
            if (string.IsNullOrWhiteSpace(folder) && ctx.IsInteractive)
                folder = PickFolder();
            if (string.IsNullOrWhiteSpace(folder))
                throw new UserException("No folder specified. Pass it as a parameter: Folder=<folder with .PcbLib files>.");
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder))
                throw new UserException($"Folder not found: {folder}");

            string multi = ctx.Get("MultiFootprint", "Skip");
            bool renameCurrentOfMany = multi.Equals("Current", StringComparison.OrdinalIgnoreCase);
            if (!renameCurrentOfMany && !multi.Equals("Skip", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"MultiFootprint='{multi}': use Skip (default) or Current.");

            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            // Only the folder itself is searched, not its subfolders.
            string[] files = Directory.GetFiles(folder, "*.PcbLib", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".PcbLib", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var log = new List<string>();
            int renamed = 0, unchanged = 0, skipped = 0, failed = 0;
            if (files.Length == 0)
                log.Add($"No PcbLib file found in folder: {folder}");

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                ctx.Log($"[{i + 1}/{files.Length}] {Path.GetFileName(file)}");
                Outcome outcome = RenameOne(client, file, renameCurrentOfMany, out string line);
                log.Add(line);
                switch (outcome)
                {
                    case Outcome.Renamed: renamed++; break;
                    case Outcome.Unchanged: unchanged++; break;
                    case Outcome.Skipped: skipped++; break;
                    default: failed++; break;
                }
            }

            log.Insert(0, $"{folder}: {files.Length} PcbLib files, {renamed} renamed, {unchanged} already named, " +
                          $"{skipped} skipped, {failed} failed.");

            string outFolder = ctx.Get("Out", Path.GetTempPath());
            Directory.CreateDirectory(outFolder);
            string resultPath = Path.GetFullPath(Path.Combine(outFolder, ResultFileName));
            File.WriteAllLines(resultPath, log);

            // A menu click opens the log in AD's text editor. From the command line the log is the returned text, so
            // the file is only opened on request.
            bool open = ctx.Get("Open") == null ? ctx.IsInteractive : ctx.Flag("Open");
            if (open)
            {
                IServerDocument resultDocument = client.OpenDocument("Text", resultPath);
                if (resultDocument != null)
                    client.ShowDocument(resultDocument);
            }

            return string.Join(Environment.NewLine, log) + Environment.NewLine + "Log written to " + resultPath;
        }

        /// <summary>
        /// ListFootprints "Lib=&lt;path to .PcbLib&gt;": read-back helper. Opens the library (unless it
        /// is open already), lists its footprint names and the current footprint, and closes it again if it opened it.
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
                    // Not found by path: show it and take the current library.
                    client.ShowDocument(document);
                    library = CurrentLibraryIfItIs(pcbServer, file)
                        ?? throw new UserException($"No PCB library found for {file}.");
                }

                var names = new List<string>();
                IPCB_LibraryIterator iterator = library.LibraryIterator_Create();
                iterator.SetState_FilterAll();
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

                string current = library.GetState_CurrentComponent()?.GetState_Pattern() ?? "(none)";
                return $"{Path.GetFileName(file)}: {names.Count} footprints (ComponentCount {library.ComponentCount()}), " +
                       $"current \"{current}\"" + Environment.NewLine + string.Join(Environment.NewLine, names);
            }
            finally
            {
                if (!wasOpen)
                    client.CloseDocument(document);
            }
        }

        private enum Outcome { Renamed, Unchanged, Skipped, Failed }

        private static Outcome RenameOne(IClient client, string file, bool renameCurrentOfMany, out string line)
        {
            // A library the user already has open would be closed by CloseDocument below, together with any unsaved
            // edits. Leave it alone instead.
            if (client.IsDocumentOpen(file))
            {
                line = $"Skipped {file}: it is open in Altium Designer. Close it and run again.";
                return Outcome.Skipped;
            }

            IServerDocument document = client.OpenDocument("PcbLib", file);
            if (document == null)
            {
                line = $"Failed to open {file}.";
                return Outcome.Failed;
            }

            try
            {
                // Show the document, then take the current library; the library is still looked up by path when the current one is another file, so the rename can
                // never hit the wrong library.
                client.ShowDocument(document);
                // Read the PCB server after opening: the PCB editor may only be loaded by the first PCB document.
                IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
                if (pcbServer == null)
                {
                    line = $"Failed to get the PCB editor after opening {file}.";
                    return Outcome.Failed;
                }
                IPCB_Library library = CurrentLibraryIfItIs(pcbServer, file) ?? pcbServer.GetPCBLibraryByPath(file);
                if (library == null)
                {
                    line = $"Failed to get the PCB library of {file}.";
                    return Outcome.Failed;
                }

                int count = library.ComponentCount();
                if (count == 0)
                {
                    line = $"Skipped {file}: the library has no footprint.";
                    return Outcome.Skipped;
                }

                // CurrentComponent is the footprint that is active in the library editor, i.e. an arbitrary one
                // when the library holds several. So it is only renamed with MultiFootprint=Current.
                if (count > 1 && !renameCurrentOfMany)
                {
                    line = $"Skipped {file}: the library has {count} footprints (pass MultiFootprint=Current to rename the current one).";
                    return Outcome.Skipped;
                }

                IPCB_LibComponent footprint = library.GetState_CurrentComponent();
                if (footprint == null)
                {
                    line = $"Failed to find the current footprint in {file}.";
                    return Outcome.Failed;
                }

                string newName = Path.GetFileNameWithoutExtension(file);
                string oldName = footprint.GetState_Pattern();
                if (oldName == newName)
                {
                    line = $"Footprint \"{oldName}\" in {file} already has the file name; not saved.";
                    return Outcome.Unchanged;
                }

                // The current footprint's name differs from newName here, so a footprint named newName is another one.
                IPCB_LibComponent sameName = library.GetComponentByName(newName);
                if (sameName != null && sameName.GetState_Pattern() == newName)
                {
                    line = $"Skipped {file}: another footprint is already named \"{newName}\".";
                    return Outcome.Skipped;
                }

                // In the .NET API the footprint name is Pattern (IPCB_LibComponent has no Name). The change is
                // bracketed like any other PCB object change (docs: PCB basics).
                pcbServer.PreProcess();
                try
                {
                    footprint.BeginModify();
                    footprint.SetState_Pattern(newName);
                    footprint.EndModify();
                }
                finally
                {
                    pcbServer.PostProcess();
                }
                library.RefreshView();

                string readBack = footprint.GetState_Pattern();
                // DoFileSave returns false when the save failed.
                if (!document.DoFileSave("PcbLib"))
                {
                    line = $"Failed to save {file} after renaming footprint \"{oldName}\" to \"{readBack}\".";
                    return Outcome.Failed;
                }

                line = $"Renamed footprint \"{oldName}\" to \"{readBack}\" in {file}.";
                return Outcome.Renamed;
            }
            finally
            {
                client.CloseDocument(document);
            }
        }

        /// <summary>The current PCB library when it is the given file, otherwise null.</summary>
        private static IPCB_Library CurrentLibraryIfItIs(IPCB_ServerInterface pcbServer, string file)
        {
            IPCB_Library current = pcbServer.GetCurrentPCBLibrary();
            string currentFile = current?.GetState_Board()?.GetState_FileName();
            return currentFile != null && string.Equals(Path.GetFullPath(currentFile), file, StringComparison.OrdinalIgnoreCase)
                ? current
                : null;
        }

        private static string PickFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Folder with the .PcbLib files to rename",
                UseDescriptionForTitle = true,
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
        }
    }
}
