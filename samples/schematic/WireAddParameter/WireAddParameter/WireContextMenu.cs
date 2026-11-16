using System;
using DXP;
using SCH;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3): the schematic right-click menu. Each item needs a command
    /// with its own GetState procedure, which shows the item only while a wire is under the right-click position.
    /// The sample registers one extra process, "RunOnWire", with such a GetState; the .rcs inserts its items into
    /// PUSCHMENU. RunOnWire then runs the implementation command exactly like the generic "Run" process.
    /// The GetState lives in the shim (not in the implementation), so it does not depend on the hot-reload host.
    /// </summary>
    public partial class PluginServerModule
    {
        // The framework's constructor calls RegisterViews(client) once, right after InitializeCommands (which
        // registers Run/Reload/Status). This sample has no panel (no Panel.cs), so the partial method is free.
        partial void RegisterViews(IClient client)
        {
            var launcher = (CommandLauncher)CommandLauncher;
            launcher.RegisterCommand("RunOnWire", WireContextMenu.RunOnWire, WireContextMenu.GetState_WireUnderCursor);
        }
    }

    internal static class WireContextMenu
    {
        /// <summary>WireAddParameter:RunOnWire  Params: Cmd=&lt;HotCommand&gt;|Wire=popup|...  (same as Run).</summary>
        public static void RunOnWire(IServerDocumentView view, ref string parameters) => Commands.Run(view, ref parameters);

        /// <summary>
        /// Visible and enabled only while a wire is under the right-click position: ISch_Document.PopupMenuHitTest,
        /// looking for the first eWire hit. AD calls this whenever it builds the menu, so it does nothing
        /// else, and nothing may escape from it (an exception here would reach AD's menu code).
        /// </summary>
        public static void GetState_WireUnderCursor(IServerDocumentView context, ref string parameters, ref bool enabled,
            ref bool isChecked, ref bool visible, ref string caption, ref string imageFile)
        {
            bool wire;
            try
            {
                wire = WireUnderPopupCursor();
            }
            catch (Exception)
            {
                // Show the item if the test itself fails: the command then reports the real error as text.
                wire = true;
            }
            enabled = wire;
            visible = wire;
        }

        private static bool WireUnderPopupCursor()
        {
            ISch_Document sheet = SCH.GlobalVars.SchServer?.GetCurrentSchDocument();
            if (sheet == null)
                return false;
            ISch_HitTest hitTest = sheet.PopupMenuHitTest();
            int count = hitTest?.GetState_HitTestCount() ?? 0;
            for (int i = 0; i < count; i++)
            {
                ISch_GraphicalObject hit = hitTest.GetState_HitObject(i);
                if (hit != null && hit.GetState_ObjectId() == TObjectId.eWire)
                    return true;
            }
            return false;
        }
    }
}
