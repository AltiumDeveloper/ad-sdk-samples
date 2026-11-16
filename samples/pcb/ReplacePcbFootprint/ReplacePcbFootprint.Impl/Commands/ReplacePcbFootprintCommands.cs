using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PCB;

namespace AdSamples.ReplacePcbFootprint
{
    /// <summary>
    /// Renames the footprint pattern of components and copies the height and description of the new footprint from a
    /// PCB library into them. The SDK lesson: IPCB_ServerInterface.LoadCompFromLibrary reads a footprint from a
    /// library file as an IPCB_LibComponent that is not on any board, so its properties can be read without changing
    /// the board.
    /// </summary>
    internal static class ReplacePcbFootprintCommands
    {
        /// <summary>
        /// ReplacePcbFootprint "From=R0805|To=R0805-W|Library=&lt;.PcbLib&gt;": for every component whose Pattern is
        /// From, sets Pattern to To and copies Height and Description of the footprint To from the component's
        /// SourceFootprintLibrary (or from Library) into the component. This renames the footprint reference, it
        /// does not swap the geometry: the pads and other primitives are expected to stay as they are.
        /// </summary>
        [HotCommand("ReplacePcbFootprint", "Rename a footprint pattern and refresh height and description")]
        public static string ReplacePcbFootprint(CommandContext ctx)
        {
            // R0805 -> R0805-W are the defaults.
            string from = ctx.Get("From", "R0805");
            string to = ctx.Get("To", "R0805-W");
            string libraryOverride = ctx.Get("Library");
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                throw new UserException("From and To must name footprint patterns, for example From=R0805|To=R0805-W.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = CurrentBoard(pcbServer);

            // Collect first, change afterwards. Changing Pattern is not expected to move a component, but collecting
            // avoids relying on the iterator staying valid while the board is being changed.
            List<IPCB_Component> matches = Components(board)
                .Where(c => string.Equals(c.GetState_Pattern(), from, StringComparison.Ordinal))
                .ToList();
            string boardName = Path.GetFileName(board.GetState_FileName());
            if (matches.Count == 0)
                return $"{boardName}: no component uses the pattern '{from}'. Nothing changed.";

            var lines = new List<string>();
            int refreshed = 0, notFound = 0;

            pcbServer.PreProcess();
            try
            {
                foreach (IPCB_Component component in matches)
                {
                    string library = string.IsNullOrEmpty(libraryOverride)
                        ? component.GetState_SourceFootprintLibrary()
                        : libraryOverride;
                    string designator = component.GetState_SourceDesignator();
                    int oldHeight = component.GetState_Height();

                    // No BeginModify/EndModify around the Pattern change: EndModify re-resolves the
                    // component against the available libraries (the mechanism is not established), so it
                    // throws the new Pattern away (the property is back to the old value right after EndModify)
                    // and, when the new name is in no available library, AD shows a modal "Component not found in
                    // available libraries." box - which stalls any non-interactive caller.
                    // The change is still inside PreProcess/PostProcess and followed by GraphicallyInvalidate.
                    // Pattern is the footprint name only; assigning it is expected not to reload the primitives.
                    component.SetState_Pattern(to);

                    // The footprint is looked up by the new name in the component's source
                    // library. The returned footprint is not on any board.
                    IPCB_LibComponent footprint = string.IsNullOrEmpty(library)
                        ? null
                        : pcbServer.LoadCompFromLibrary(to, library);
                    if (footprint != null)
                    {
                        component.SetState_Height(footprint.GetState_Height());
                        component.SetState_FootprintDescription(footprint.GetState_Description());
                        refreshed++;
                    }
                    else
                    {
                        // The component stays renamed with its old height and description; the case is counted
                        // and reported.
                        notFound++;
                    }

                    // Recalculates the size of the child primitives and the component's bounds.
                    component.SetState_XSizeYSize();
                    component.GraphicallyInvalidate();

                    lines.Add($"    {designator}: {from} -> {to}, Height {Mils(oldHeight)} -> {Mils(component.GetState_Height())}, " +
                              $"Description '{component.GetState_FootprintDescription()}'" +
                              (footprint == null ? $" (footprint '{to}' not found in '{library}')" : ""));
                }
            }
            finally
            {
                pcbServer.PostProcess();
            }

            lines.Insert(0, $"{boardName}: {matches.Count} components changed from '{from}' to '{to}', " +
                            $"{refreshed} refreshed from the library, {notFound} without a library match.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// ListFootprints "Pattern=&lt;name&gt;|Library=&lt;.PcbLib&gt;|Lookup=&lt;name&gt;": read-only. Lists every
        /// component (or only those of Pattern) with its Pattern, Height, FootprintDescription and
        /// SourceFootprintLibrary. With Lookup, also loads that footprint from Library (or from each component's
        /// source library) and shows its Height and Description, so a caller can pick a From/To pair and check the
        /// result.
        /// </summary>
        [HotCommand("ListFootprints", "List the footprint patterns of the board's components")]
        public static string ListFootprints(CommandContext ctx)
        {
            string pattern = ctx.Get("Pattern");
            string libraryOverride = ctx.Get("Library");
            string lookup = ctx.Get("Lookup");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = CurrentBoard(pcbServer);

            List<IPCB_Component> components = Components(board)
                .Where(c => string.IsNullOrEmpty(pattern) || string.Equals(c.GetState_Pattern(), pattern, StringComparison.Ordinal))
                .ToList();

            var lines = new List<string>();
            var patterns = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (IPCB_Component component in components)
            {
                string p = component.GetState_Pattern();
                patterns[p] = patterns.TryGetValue(p, out int n) ? n + 1 : 1;
                lines.Add($"    {component.GetState_SourceDesignator()}: Pattern '{p}', Height {Mils(component.GetState_Height())}, " +
                          $"Description '{component.GetState_FootprintDescription()}', " +
                          $"Library '{component.GetState_SourceFootprintLibrary()}'");
            }

            if (!string.IsNullOrEmpty(lookup))
            {
                // One lookup per distinct library, so a board with many components doesn't load the same file again.
                IEnumerable<string> libraries = string.IsNullOrEmpty(libraryOverride)
                    ? components.Select(c => c.GetState_SourceFootprintLibrary()).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.OrdinalIgnoreCase)
                    : new[] { libraryOverride };
                foreach (string library in libraries)
                {
                    IPCB_LibComponent footprint = pcbServer.LoadCompFromLibrary(lookup, library);
                    lines.Add(footprint == null
                        ? $"Lookup '{lookup}' in '{library}': not found"
                        : $"Lookup '{lookup}' in '{library}': Height {Mils(footprint.GetState_Height())}, Description '{footprint.GetState_Description()}'");
                }
            }

            string boardName = Path.GetFileName(board.GetState_FileName());
            lines.Insert(0, $"{boardName}: {components.Count} components, {patterns.Count} patterns: " +
                            string.Join(", ", patterns.Select(kv => $"{kv.Key} ({kv.Value})")));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>All components of the board. The iterator is destroyed before the list is used.</summary>
        private static List<IPCB_Component> Components(IPCB_Board board)
        {
            var result = new List<IPCB_Component>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    result.Add(component);
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return result;
        }

        /// <summary>Height is an int in internal units (10,000 per mil); the raw value is kept next to the mils.</summary>
        private static string Mils(int value) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###} mil ({1})", value / (double)EDP.EDPConstant.InternalUnits, value);

        private static IPCB_Board CurrentBoard(IPCB_ServerInterface pcbServer) =>
            pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
    }
}
