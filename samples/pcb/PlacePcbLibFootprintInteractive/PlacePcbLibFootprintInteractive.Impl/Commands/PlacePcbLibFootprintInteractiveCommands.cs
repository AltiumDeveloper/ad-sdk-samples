using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DXP; // IClient.GetServerModuleByName is a typed helper (extension method) in DXP
using PCB;
using SCH; // the typed ILibraryServer / ILibraryDataReader / ILibraryData helpers (extension methods) live in SCH

namespace AdSamples.PlacePcbLibFootprintInteractive
{
    /// <summary>
    /// Hands a footprint of a .PcbLib to the PCB editor's own interactive placement. The SDK lesson: get the
    /// Integrated Library server (SCH.ILibraryServer) from DXP.IClient.GetServerModuleByName("IntegratedLibrary"),
    /// register the library with its ILibraryDataReader to get GUIDs for the library and its footprints, then run
    /// the process IntegratedLibrary:PlaceComponent with those GUIDs.
    /// </summary>
    internal static class PlacePcbLibFootprintInteractiveCommands
    {
        private const string LibraryServerName = "IntegratedLibrary";
        private const string PlaceProcess = "IntegratedLibrary:PlaceComponent";

        // Guard against an iteration that never returns null (First/Next are stateful COM calls).
        private const int MaxEntries = 100000;

        /// <summary>
        /// PlaceLibComponent "PcbLibPath=&lt;.PcbLib&gt;|FootprintName=&lt;name&gt;". From a menu click without
        /// PcbLibPath the library is picked with a file dialog.
        /// </summary>
        [HotCommand("PlaceLibComponent", "Start interactive placement of a footprint from a .PcbLib")]
        public static string PlaceLibComponent(CommandContext ctx)
        {
            // IntegratedLibrary:PlaceComponent needs a PCB document to place into.
            IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            string libPath = LibraryPath(ctx);
            string footprintName = ctx.Get("FootprintName");

            ILibraryDataReader reader = DataReader();
            ILibraryData library = Register(reader, libPath);
            string libraryGuid = library.GUID();
            try
            {
                if (string.IsNullOrWhiteSpace(footprintName))
                    throw new UserException("Pass FootprintName=<name>. " + Describe(Footprints(library), libPath));

                ILibraryComponentData footprint = Find(library, footprintName, out List<string> seen)
                    ?? throw new UserException($"Footprint '{footprintName}' does not exist. " + Describe(seen, libPath));

                string parameters = $"LibraryGUID={libraryGuid}|ComponentGUID={footprint.GUID()}";
                int before = CountComponents(board);
                var clock = Stopwatch.StartNew();

                // RunCommand reports nothing back. Whether it returns as soon as placement mode starts or only when
                // the user ends the placement is what the elapsed time and component counts below make visible.
                DXP.Utils.RunCommand(PlaceProcess, parameters);

                clock.Stop();
                // Re-query the board: the placement may have changed the focused document.
                IPCB_Board after = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard();
                string count = after == null ? "no PCB document is active now" : $"{before} -> {CountComponents(after)}";
                return $"Ran {PlaceProcess} for footprint '{footprint.Name()}' of {Path.GetFileName(libPath)} " +
                       $"({parameters}) on {Path.GetFileName(board.GetState_FileName())}. " +
                       $"RunCommand returned after {clock.ElapsedMilliseconds} ms; components on the board: {count}. " +
                       "In placement mode, click to drop the footprint, right-click or Escape to stop. " +
                       "The library is now unregistered again.";
            }
            finally
            {
                // Unregister right after RunCommand, also when the footprint was not found.
                reader.UnregisterLibrary(libraryGuid);
            }
        }

        /// <summary>
        /// ListLibFootprints "PcbLibPath=&lt;.PcbLib&gt;": read-only. Shows what RegisterLibrary
        /// returns for a .PcbLib: the library GUID and kind, and the name and GUID of each entry.
        /// </summary>
        [HotCommand("ListLibFootprints", "List the footprints of a .PcbLib (read-only)")]
        public static string ListLibFootprints(CommandContext ctx)
        {
            string libPath = LibraryPath(ctx);
            ILibraryDataReader reader = DataReader();
            int registeredBefore = reader.Count();
            ILibraryData library = Register(reader, libPath);
            string libraryGuid = library.GUID();
            var lines = new List<string>();
            try
            {
                lines.Add($"{Path.GetFileName(libPath)}: registered as '{library.Name()}', kind {library.Kind()}, " +
                          $"GUID {libraryGuid}, file '{library.FilePath()}'. Registered libraries: {registeredBefore} -> {reader.Count()}.");
                int n = 0;
                ILibraryComponentData entry = library.First();
                while (entry != null)
                {
                    if (++n > MaxEntries)
                        throw new UserException($"ILibraryData.Next did not end after {MaxEntries} entries.");
                    lines.Add($"    {entry.Name()} : GUID {entry.GUID()}");
                    entry = library.Next();
                }
                lines[0] += $" {n} footprints.";
            }
            finally
            {
                reader.UnregisterLibrary(libraryGuid);
            }
            lines.Add($"Unregistered. Registered libraries now: {reader.Count()}.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// The IDispatch of GetServerModuleByName('IntegratedLibrary') is cast to ILibraryServer. In .NET
        /// the cast always succeeds (all SDK interfaces share the IDispatch GUID); a wrong object would only fail
        /// when GetDataReader is called.
        /// </summary>
        private static ILibraryDataReader DataReader()
        {
            DXP.IServerModule module = DXP.GlobalVars.Client?.GetServerModuleByName(LibraryServerName)
                ?? throw new UserException($"The {LibraryServerName} server is not loaded.");
            var server = (ILibraryServer)module;
            return server.GetDataReader()
                ?? throw new UserException($"The {LibraryServerName} server returned no data reader.");
        }

        private static ILibraryData Register(ILibraryDataReader reader, string libPath) =>
            reader.RegisterLibrary(libPath)
            ?? throw new UserException($"RegisterLibrary returned nothing for '{libPath}'.");

        /// <summary>PcbLibPath= is required; a menu click without it asks for the file.</summary>
        private static string LibraryPath(CommandContext ctx)
        {
            string libPath = ctx.Get("PcbLibPath");
            if (string.IsNullOrWhiteSpace(libPath) && ctx.IsInteractive)
                libPath = PickLibrary();
            if (string.IsNullOrWhiteSpace(libPath))
                throw new UserException("Pass PcbLibPath=<full path to a .PcbLib>.");
            libPath = libPath.Trim();
            if (!File.Exists(libPath))
                throw new UserException($"PcbLibPath '{libPath}' does not exist.");
            return Path.GetFullPath(libPath);
        }

        private static string PickLibrary()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Choose the PCB library",
                Filter = "PCB libraries (*.PcbLib)|*.PcbLib|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }

        /// <summary>Walks First/Next until the name matches (exact, case-sensitive).</summary>
        private static ILibraryComponentData Find(ILibraryData library, string name, out List<string> seen)
        {
            seen = new List<string>();
            ILibraryComponentData entry = library.First();
            while (entry != null)
            {
                string entryName = entry.Name();
                if (name != null && entryName == name)
                    return entry;
                seen.Add(entryName);
                if (seen.Count > MaxEntries)
                    throw new UserException($"ILibraryData.Next did not end after {MaxEntries} entries.");
                entry = library.Next();
            }
            return null;
        }

        private static List<string> Footprints(ILibraryData library)
        {
            Find(library, null, out List<string> all); // a null name matches nothing: collects every entry
            return all;
        }

        private static string Describe(List<string> names, string libPath)
        {
            const int Shown = 20;
            string list = names.Count == 0 ? "none" : string.Join(", ", names.GetRange(0, Math.Min(Shown, names.Count)));
            return $"{Path.GetFileName(libPath)} has {names.Count} footprints: {list}" +
                   (names.Count > Shown ? ", ..." : "") + ".";
        }

        private static int CountComponents(IPCB_Board board)
        {
            int count = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (IPCB_Primitive p = iterator.FirstPCBObject(); p != null; p = iterator.NextPCBObject())
                    count++;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return count;
        }
    }
}
