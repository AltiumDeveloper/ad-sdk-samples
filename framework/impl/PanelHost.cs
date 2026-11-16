using System;
using System.Windows.Forms;

namespace AdSamples
{
    /// <summary>
    /// Owns the content of the shim's sample panel (shared framework file). The shim publishes an empty host control under
    /// <see cref="HostKey"/>; this class docks a the sample's content (CreateContent) into it: when the panel is first shown
    /// (the shim calls Panel.Attach) and after every hot reload (HotEntry.OnLoad). The content is a control of the
    /// current generation, so HotEntry.OnUnload removes it: a shim control holding it would keep the old generation
    /// alive. State that must survive a reload belongs in AppDomain data as BCL types (strings, JSON), not in
    /// static fields.
    /// </summary>
    internal static partial class SamplePanel
    {
        /// <summary>Must match SamplePanelForm in the shim and the PanelInfo in <Sample>.ins.</summary>
        public const string PanelName = SampleInfo.PanelName;

        private const string HostKey = SampleInfo.PanelHostKey;
        private const string InvokeKey = SampleInfo.InvokeKey;

        private static Control _content;

        /// <summary>The sample implements this in Ui\PanelContent.cs: <c>internal static partial Control CreateContent() => new MyContent();</c></summary>
        internal static partial Control CreateContent();

        private static Control Host => AppDomain.CurrentDomain.GetData(HostKey) as Control;

        /// <summary>
        /// Runs a command the way the menus do: through the shim, so that in Debug the hot-reload host knows a
        /// command is running and never swaps the implementation under it. Throws when the command fails.
        /// </summary>
        public static string Invoke(string command, string parameters = "")
        {
            var invoke = AppDomain.CurrentDomain.GetData(InvokeKey) as Func<string, string, string>
                         ?? throw new UserException(SampleInfo.Name + " panel is not available: redeploy the extension and restart Altium Designer.");
            return invoke(command, parameters);
        }

        /// <summary>
        /// Docks the content into the shim's host, unless it is already there. A no-op until AD has created the
        /// panel (at Release startup, OnLoad runs before that; the panel then attaches when it is first shown).
        /// </summary>
        public static void Attach()
        {
            var host = Host;
            if (host == null || _content != null && !_content.IsDisposed && _content.Parent == host)
                return;
            Detach();
            var stale = new Control[host.Controls.Count]; // e.g. the shim's load-error label
            host.Controls.CopyTo(stale, 0);
            host.Controls.Clear();
            foreach (var control in stale)
                control.Dispose();
            _content = CreateContent(); _content.Dock = DockStyle.Fill;
            host.Controls.Add(_content);
        }

        /// <summary>
        /// Removes and disposes the content: the shim's host must not keep this generation alive. Release here, too,
        /// anything the content subscribed to outside itself (timers, Application.Idle, events of Altium or shim
        /// objects); disposing the control releases only its own child controls.
        /// </summary>
        public static void Detach()
        {
            if (_content == null)
                return;
            _content.Parent?.Controls.Remove(_content);
            _content.Dispose();
            _content = null;
        }

        /// <summary>Opens the panel and gives it the focus.</summary>
        public static void Show()
        {
            var gui = DXP.GlobalVars.GUIManager ?? throw new UserException("The Altium GUI manager is not available.");
            gui.SetPanelVisibleInCurrentForm(PanelName, true);
            gui.SetPanelActiveInCurrentForm(PanelName);
            Attach();
        }
    }
}
