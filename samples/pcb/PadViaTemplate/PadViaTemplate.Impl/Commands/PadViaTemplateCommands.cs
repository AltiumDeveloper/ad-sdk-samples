using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PCB;

namespace AdSamples.PadViaTemplate
{
    /// <summary>
    /// Reports which pad/via template each pad of the current board is linked to. The SDK lesson:
    /// IPCB_Board.FindLinkedBoardTemplate(pad) returns the board template (IPCB_PadViaTemplate) behind a pad, or null
    /// when the pad is not linked; the pad's own IPCB_Pad.TemplateLink only holds the identifiers.
    /// </summary>
    internal static class PadViaTemplateCommands
    {
        private const int DefaultMax = 50;

        /// <summary>
        /// GetTemplateName "Max=50": one text report. Lists up
        /// to Max linked pads, then the distinct templates and a summary of linked and unlinked pads.
        /// </summary>
        [HotCommand("GetTemplateName", "Report the pad/via template each pad is linked to")]
        public static string GetTemplateName(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max"));
            IPCB_Board board = CurrentBoard();

            int padCount = 0, linkedCount = 0, linkOnlyCount = 0;
            var padLines = new List<string>();
            var templates = new SortedDictionary<string, (IPCB_PadViaTemplate Template, int Pads)>(StringComparer.Ordinal);

            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // In .NET the all-layers set is PCBConstant.AllLayersSet.
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_LayerSet_2(PCBConstant.AllLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                {
                    padCount++;
                    if (padCount % 500 == 0)
                        ctx.Log($"{padCount} pads checked");

                    // The pad's own link (identifiers only). Reported next to the board template so the two can be
                    // compared; how they relate is not documented.
                    string linkId = LinkId(pad.GetState_TemplateLink());

                    IPCB_PadViaTemplate template = board.FindLinkedBoardTemplate(pad);
                    if (template == null)
                    {
                        if (linkId != null)
                            linkOnlyCount++;
                        continue;
                    }

                    linkedCount++;
                    string fullId = template.GetFullTemplateID() ?? "";
                    templates[fullId] = templates.TryGetValue(fullId, out var entry) ? (entry.Template, entry.Pads + 1) : (template, 1);

                    if (padLines.Count < max)
                        padLines.Add($"    {PadName(pad)} : Library ID : {template.GetState_LibraryID()}, " +
                                     $"Template ID : {template.GetState_TemplateID()}, Revision ID : {template.GetState_RevisionID()}, " +
                                     $"Template Name : {template.GetState_TemplateName()}, " +
                                     $"Template Description : {template.GetState_TemplateDescription()}, " +
                                     $"Pad TemplateLink : {linkId ?? "(empty)"}");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            IPCB_PadViaLibrary library = board.GetState_PadViaLibrary();
            string libraryText = library == null ? "no board pad/via library" : $"board pad/via library: {library.GetState_Count()} templates";

            var report = new List<string>
            {
                $"{Path.GetFileName(board.GetState_FileName())}: {padCount} pads, {linkedCount} linked to a template " +
                $"({templates.Count} distinct), {padCount - linkedCount} not linked; {libraryText}."
            };
            if (linkOnlyCount > 0)
                report.Add($"{linkOnlyCount} unlinked pads have a non-empty TemplateLink (FindLinkedBoardTemplate returned null).");

            if (linkedCount == 0)
            {
                report.Add("No pad is linked to a pad/via template.");
                return string.Join(Environment.NewLine, report);
            }

            report.Add(linkedCount > padLines.Count
                ? $"Linked pads (first {padLines.Count} of {linkedCount}, Max={max}):"
                : "Linked pads:");
            report.AddRange(padLines);

            report.Add("Templates:");
            foreach (var pair in templates)
                report.Add($"    {pair.Value.Template.GetState_TemplateName()} ({pair.Key}) : {pair.Value.Pads} pads");

            return string.Join(Environment.NewLine, report);
        }

        /// <summary>
        /// LinkTestPadTemplate "Pad=U1-1": setup helper. Adds a template made from a pad to the
        /// board's pad/via library and links the pad to it, so GetTemplateName has a linked pad to report on boards
        /// that have none. Default pad: the first pad that is not linked. Changes the board in memory only.
        /// </summary>
        [HotCommand("LinkTestPadTemplate", "Create a board template from a pad and link the pad to it (test setup)")]
        public static string LinkTestPadTemplate(CommandContext ctx)
        {
            IPCB_Board board = CurrentBoard();
            IPCB_PadViaLibrary library = board.GetState_PadViaLibrary()
                ?? throw new UserException("The board has no pad/via library.");

            string wanted = ctx.Get("Pad");
            IPCB_Pad pad = FindPad(board, wanted)
                ?? throw new UserException(string.IsNullOrWhiteSpace(wanted)
                    ? "The board has no pad that is not linked to a template."
                    : $"No pad '{wanted}' found. Use the pin descriptor, for example Pad=U1-1.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer;
            IPCB_PadViaTemplate template;
            int before = library.GetState_Count();
            pcbServer.PreProcess();
            try
            {
                // AddTemplate(primitive) is expected to build a template from the pad's settings and append it. Neither
                // is documented: the count check below is the evidence, and the read-back after linking confirms it.
                library.AddTemplate(pad);
                int after = library.GetState_Count();
                if (after <= before)
                    throw new UserException($"AddTemplate did not add a template (library count stayed {before}).");
                template = library.GetState_Template(after - 1)
                    ?? throw new UserException("The new template could not be read from the library.");

                pad.BeginModify();
                board.LinkToTemplate(pad, template);
                pad.EndModify();
            }
            finally
            {
                pcbServer.PostProcess();
            }

            IPCB_PadViaTemplate linked = board.FindLinkedBoardTemplate(pad);
            string linkedText = linked == null
                ? "FindLinkedBoardTemplate returned null after linking"
                : $"FindLinkedBoardTemplate returns '{linked.GetState_TemplateName()}' ({linked.GetFullTemplateID()})";
            return $"Linked pad {PadName(pad)} to new template '{template.GetState_TemplateName()}' " +
                   $"({template.GetFullTemplateID()}); library count {before} -> {library.GetState_Count()}; {linkedText}; " +
                   $"pad TemplateLink : {LinkId(pad.GetState_TemplateLink()) ?? "(empty)"}. " +
                   "Close the document without saving to discard it.";
        }

        /// <summary>The pad with the given pin descriptor, or (no descriptor) the first pad without a linked template.</summary>
        private static IPCB_Pad FindPad(IPCB_Board board, string descriptor)
        {
            bool any = string.IsNullOrWhiteSpace(descriptor);
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_LayerSet_2(PCBConstant.AllLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                {
                    if (any ? board.FindLinkedBoardTemplate(pad) == null
                            : string.Equals(PadName(pad), descriptor.Trim(), StringComparison.OrdinalIgnoreCase))
                        return pad;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return null;
        }

        /// <summary>"U1-1" for a component pad, "free pad 1" for a pad without a component.</summary>
        private static string PadName(IPCB_Pad pad) =>
            pad.GetState_Component() != null ? pad.GetState_PinDescriptorString() : "free pad " + pad.GetState_Name();

        /// <summary>The link's LibraryID@TemplateID, or null when both identifiers are empty.</summary>
        private static string LinkId(IPCB_PadViaTemplateLink link)
        {
            if (link == null || (string.IsNullOrEmpty(link.GetState_LibraryID()) && string.IsNullOrEmpty(link.GetState_TemplateID())))
                return null;
            return link.GetFullTemplateID();
        }

        private static int ParseMax(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return DefaultMax;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 0)
                throw new UserException($"Max must be a whole number >= 0, not '{text}'.");
            return max;
        }

        private static IPCB_Board CurrentBoard()
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            return pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
        }
    }
}
