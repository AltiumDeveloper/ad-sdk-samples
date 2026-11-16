using System;
using System.IO;
using System.Text;
using ComponentLib; // GetSelectedComponents and IComponentDefinitionList.Get are typed extension methods
using DXP;          // IServerDocumentView.GetOwnerDocument is an extension method in namespace DXP

namespace AdSamples.CmpLibSelectedHrids
{
    /// <summary>
    /// Lists the HRIDs of the components selected in the active component library (.CmpLib) editor. The SDK lesson:
    /// check the document kind first, then treat the view's owner document as IComponentLibraryDocument and read
    /// its selection with GetSelectedComponents.
    /// </summary>
    internal static class CmpLibSelectedHridsCommands
    {
        // The kind string of a CmpLib document. The comparison ignores case, because the exact casing AD reports
        // for a CmpLib document is not documented.
        private const string CmpLibKind = "CmpLib";

        /// <summary>
        /// GetSelectedComponents: returns one line per component selected in the active CmpLib editor, with its HRID.
        /// Changes nothing.
        /// </summary>
        [HotCommand("GetSelectedComponents", "List the HRIDs of the components selected in the active CmpLib")]
        public static string GetSelectedComponents(CommandContext ctx)
        {
            // From a menu or Run Process, AD passes the view the command was started in.
            // Without one (ctx.View may be null, for example from the pipe), fall back to the client's current view.
            IServerDocumentView view = ctx.View as IServerDocumentView ?? DXP.GlobalVars.Client?.GetCurrentView()
                ?? throw new UserException("No document view is active. Open and focus a .CmpLib document first.");
            IServerDocument document = view.GetOwnerDocument()
                ?? throw new UserException("The active view has no document. Open and focus a .CmpLib document first.");

            // The kind check is what makes the cast below safe: SDK interfaces are dispatch interfaces, so a cast to
            // IComponentLibraryDocument succeeds for any document, and only calling a member would fail.
            string kind = document.GetKind();
            if (!string.Equals(kind, CmpLibKind, StringComparison.OrdinalIgnoreCase))
                throw new UserException(
                    $"This is not a CmpLib document (kind '{kind}', file '{Path.GetFileName(document.GetFileName())}'). " +
                    "Open and focus a .CmpLib document first.");

            var cmpLib = (IComponentLibraryDocument)document;
            IComponentDefinitionList selected = cmpLib.GetSelectedComponents();
            int count = selected?.Count() ?? 0;

            string fileName = Path.GetFileName(document.GetFileName());
            if (count == 0)
                return $"{fileName}: no components selected. Select one or more components in the CmpLib editor.";

            // One text result for all components.
            var result = new StringBuilder();
            result.Append($"{fileName}: {count} selected component{(count == 1 ? "" : "s")}");
            for (int i = 0; i < count; i++)
            {
                IComponentDefinition component = selected.Get(i);
                string hrid = component?.GetItemHRID();
                result.AppendLine();
                result.Append($"  {i + 1}. {(string.IsNullOrEmpty(hrid) ? "(no HRID)" : hrid)}");
            }
            return result.ToString();
        }
    }
}
