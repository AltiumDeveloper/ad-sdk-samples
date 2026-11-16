using System;
using DXP;
using SCH;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3): the schematic right-click menu item "Open SchLib Component". The item
    /// needs its own GetState procedure, which shows and enables it only while
    /// ISch_Document.PopupMenuHitTest has a component under the right-click position. The framework registers Run
    /// with an always-visible GetState, so the sample registers one extra process, "RunOnComponent", with that
    /// GetState; the .rcs inserts its item into PUSCHMENU. RunOnComponent then runs the implementation command
    /// exactly like the generic "Run" process.
    /// The GetState lives in the shim (not in the implementation), so it does not depend on the hot-reload host.
    /// </summary>
    public partial class PluginServerModule
    {
        // Template hook RegisterCommands: registers processes next to Run, when Altium Designer creates the module.
        // RunOnComponent is the context-menu item's process, with its own GetState.
        partial void RegisterCommands(CommandLauncher launcher)
        {
            launcher.RegisterCommand("RunOnComponent", ComponentContextMenu.RunOnComponent, ComponentContextMenu.GetState_ComponentUnderCursor);
        }
    }

    internal static class ComponentContextMenu
    {
        /// <summary>NavigateToSchLibComp:RunOnComponent  Params: Cmd=&lt;HotCommand&gt;|...  (same as Run).</summary>
        public static void RunOnComponent(IServerDocumentView view, ref string parameters) => Commands.Run(view, ref parameters);

        /// <summary>
        /// Visible and enabled only while a component is under the right-click position.
        /// AD calls this whenever it builds a menu that holds the item, so it does
        /// nothing else, and nothing may escape from it: an exception would reach AD's menu code.
        /// </summary>
        public static void GetState_ComponentUnderCursor(IServerDocumentView context, ref string parameters, ref bool enabled,
            ref bool isChecked, ref bool visible, ref string caption, ref string imageFile)
        {
            bool component;
            try
            {
                component = HasComponentUnderCursor();
            }
            catch (Exception)
            {
                // Hide the item when the test itself fails, as when no component is found.
                component = false;
            }
            enabled = component;
            visible = component;
        }

        // Walk the popup hit test and look for a component.
        private static bool HasComponentUnderCursor()
        {
            ISch_Document document = SCH.GlobalVars.SchServer?.GetCurrentSchDocument();
            // The item opens the library of a placed component: not in the library editor itself.
            if (document == null || document.GetState_ObjectId() == TObjectId.eSchLib)
                return false;
            ISch_HitTest hitTest = document.PopupMenuHitTest();
            int count = hitTest?.GetState_HitTestCount() ?? 0;
            for (int i = 0; i < count; i++)
            {
                ISch_GraphicalObject hit = hitTest.GetState_HitObject(i);
                if (hit != null && hit.GetState_ObjectId() == TObjectId.eSchComponent)
                    return true;
            }
            return false;
        }
    }
}
