using System.IO;
using SCH;

namespace AdSamples.SchComponentContextMenu
{
    /// <summary>
    /// The component under the schematic right-click position. The SDK lesson: ISch_Document.PopupMenuHitTest
    /// returns the objects at the position where the context menu was opened, and
    /// ISch_Document.CountContextMenuObjects counts those of given kinds. The shim (ComponentContextMenu.cs) uses
    /// the count to show its context menu item only over a component; this command uses the hit test to find it.
    /// </summary>
    internal static class SchComponentContextMenuCommands
    {
        /// <summary>
        /// ComponentUnderContextMenu: the designator of the first component in the right-click hit list (read-only).
        /// From the pipe or the main menu no context menu is open, so the result is whatever AD keeps from the last
        /// right-click (nothing before the first right-click).
        /// </summary>
        [HotCommand("ComponentUnderContextMenu", "Report the schematic component under the right-click position")]
        public static string ComponentUnderContextMenu(CommandContext ctx)
        {
            ISch_Document sheet = CurrentSheet();

            // The same test the shim's GetState makes.
            var components = new SCH.TObjectSet();
            components.Add(TObjectId.eSchComponent);
            int contextComponents = sheet.CountContextMenuObjects(components);

            // PopupMenuHitTest is on ISch_Document in .NET: no cast to ISch_Sheet is needed.
            ISch_HitTest hitTest = sheet.PopupMenuHitTest();
            int hitCount = hitTest?.GetState_HitTestCount() ?? 0;

            ISch_Component component = null;
            for (int i = 0; i < hitCount; i++)
            {
                ISch_GraphicalObject hit = hitTest.GetState_HitObject(i);
                // Type tests succeed for every SDK object: branch on ObjectId, then cast.
                if (hit != null && hit.GetState_ObjectId() == TObjectId.eSchComponent)
                {
                    component = (ISch_Component)hit;
                    break;
                }
            }

            string counts = $"(hit objects: {hitCount}, context-menu components: {contextComponents})";
            if (component == null)
                return $"{FileName(sheet)}: no component under the context-menu position {counts}.";

            // SchDesignator is the .NET name of the Designator property.
            string designator = component.GetState_SchDesignator()?.GetState_Text() ?? "(no designator)";
            string libReference = component.GetState_LibReference();
            return $"{FileName(sheet)}: component under the context menu: {designator} (LibReference {libReference}) {counts}.";
        }

        private static ISch_Document CurrentSheet()
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static string FileName(ISch_Document sheet) => Path.GetFileName(sheet.GetState_DocumentName());
    }
}
