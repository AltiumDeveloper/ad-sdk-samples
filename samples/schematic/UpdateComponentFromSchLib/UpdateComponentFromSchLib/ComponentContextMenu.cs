using System;
using DXP;
using SCH;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3): the schematic right-click menu item. The item needs a command
    /// with a GetState procedure that shows and enables the item only while a component is under the right-click
    /// position. The framework registers Run with an always-visible GetState, so the sample registers one extra
    /// process, "RunOnComponent", with such a GetState; the .rcs inserts its item into PUSCHMENU. RunOnComponent
    /// then runs the implementation command exactly like the generic "Run" process.
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
        /// <summary>UpdateComponentFromSchLib:RunOnComponent  Params: Cmd=&lt;HotCommand&gt;|...  (same as Run).</summary>
        public static void RunOnComponent(IServerDocumentView view, ref string parameters) => Commands.Run(view, ref parameters);

        /// <summary>
        /// Visible and enabled only while a component is under the right-click position. AD calls this whenever it
        /// builds a menu that holds the item, so it does nothing else, and nothing may escape from it: an exception
        /// would reach AD's menu code.
        /// Instead of walking PopupMenuHitTest it asks ISch_Document.CountContextMenuObjects, one call
        /// that returns a number, so no COM object of the hit list is touched from the menu code.
        /// </summary>
        public static void GetState_ComponentUnderCursor(IServerDocumentView context, ref string parameters, ref bool enabled,
            ref bool isChecked, ref bool visible, ref string caption, ref string imageFile)
        {
            bool component;
            try
            {
                component = ContextMenuComponentCount() > 0;
            }
            catch (Exception)
            {
                // Hide the item when the test itself fails, as when no component is found.
                component = false;
            }
            enabled = component;
            visible = component;
        }

        private static int ContextMenuComponentCount()
        {
            ISch_Document document = SCH.GlobalVars.SchServer?.GetCurrentSchDocument();
            if (document == null || document.GetState_ObjectId() == TObjectId.eSchLib)
                return 0;
            var components = new SCH.TObjectSet();
            components.Add(TObjectId.eSchComponent);
            return document.CountContextMenuObjects(components);
        }
    }
}
