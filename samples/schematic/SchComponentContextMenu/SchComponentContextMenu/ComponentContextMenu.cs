using System;
using DXP;
using SCH;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3): the schematic right-click menu item. The item's command needs its own
    /// GetState procedure, which shows and enables the item only while
    /// ISch_Document.CountContextMenuObjects finds a component under the right-click position. The framework
    /// registers Run with an always-visible GetState, so this sample registers one extra process, "RunOnComponent",
    /// with that GetState; the .rcs inserts its item into PUSCHMENU. RunOnComponent then runs the implementation
    /// command exactly like the generic "Run" process.
    /// The GetState lives in the shim (not in the implementation), so it does not depend on the hot-reload host.
    /// </summary>
    public partial class PluginServerModule
    {
        // The framework's constructor calls RegisterViews(client) once, right after InitializeCommands (which
        // registers Run/Reload/Status). This sample has no panel (no Panel.cs), so the partial method is free.
        // A command registered here, after InitializeCommands, is found by AD and its GetState is called.
        partial void RegisterViews(IClient client)
        {
            var launcher = (CommandLauncher)CommandLauncher;
            launcher.RegisterCommand("RunOnComponent", ComponentContextMenu.RunOnComponent, ComponentContextMenu.GetState_ComponentUnderCursor);
        }
    }

    internal static class ComponentContextMenu
    {
        /// <summary>SchComponentContextMenu:RunOnComponent  Params: Cmd=&lt;HotCommand&gt;|...  (same as Run).</summary>
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
                component = ContextMenuComponentCount() > 0;
            }
            catch (Exception)
            {
                // Hide the item when the test itself fails. The main menu item
                // runs the same calls in the implementation and reports such an error as text.
                component = false;
            }
            enabled = component;
            visible = component;
        }

        private static int ContextMenuComponentCount()
        {
            ISch_Document document = SCH.GlobalVars.SchServer?.GetCurrentSchDocument();
            if (document == null)
                return 0;
            var components = new SCH.TObjectSet();
            components.Add(TObjectId.eSchComponent);
            return document.CountContextMenuObjects(components);
        }
    }
}
