using System;
using System.Windows.Forms;
using DXP;

namespace AdSamples
{
    internal static partial class SamplePanel
    {
        internal static partial Control CreateContent() => new SchWireParameterPanel.Ui.PanelContent();
    }
}

namespace AdSamples.SchWireParameterPanel.Ui
{
    /// <summary>
    /// The panel's content: the wire parameters of the current sheet and the AutoShow state, with buttons for the
    /// sample's commands. Rebuilt on every hot reload; it keeps no state of its own (the shim keeps AutoShow's).
    /// </summary>
    internal sealed class PanelContent : UserControl
    {
        private static PanelContent _current;
        private readonly TextBox _text;

        public PanelContent()
        {
            BackColor = DxpThemeManager.ColorPanelBody;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = DxpThemeManager.ColorPanelBody };
            buttons.Controls.Add(MakeButton("Refresh", () => null));
            buttons.Controls.Add(MakeButton("Show wire parameters", () => SamplePanel.Invoke("ShowWireParameters", "Select=true")));
            buttons.Controls.Add(MakeButton("AutoShow on", () => SamplePanel.Invoke("AutoShow", "State=on")));
            buttons.Controls.Add(MakeButton("AutoShow off", () => SamplePanel.Invoke("AutoShow", "State=off")));

            _text = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                BorderStyle = BorderStyle.None,
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };

            // Fill first, then Top: WinForms docks the last added control first.
            Controls.Add(_text);
            Controls.Add(buttons);

            _current = this;
            Disposed += (_, _) => { if (_current == this) _current = null; };
            UpdateText(null);
        }

        /// <summary>Refreshes the panel if it exists (commands call this after they change something).</summary>
        public static void RefreshCurrent()
        {
            var panel = _current;
            if (panel != null && !panel.IsDisposed)
                panel.UpdateText(null);
        }

        private Button MakeButton(string caption, Func<string> action)
        {
            var button = new Button { Text = caption, AutoSize = true, ForeColor = DxpThemeManager.ColorDefaultFont };
            button.Click += (_, _) =>
            {
                string result;
                try { result = action(); }
                catch (Exception ex) { result = (ex is UserException ? "" : ex.GetType().Name + ": ") + ex.Message; }
                UpdateText(result);
            };
            return button;
        }

        private void UpdateText(string lastResult)
        {
            string body;
            try
            {
                body = SchWireParameterPanelCommands.DescribeCurrentSheet() + Environment.NewLine + Environment.NewLine +
                       SchWireParameterPanelCommands.AutoShowStatus();
            }
            catch (Exception ex)
            {
                body = "Could not read the sheet: " + ex.Message;
            }
            _text.Text = (string.IsNullOrEmpty(lastResult) ? "" : lastResult + Environment.NewLine + Environment.NewLine) +
                         body.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }
    }
}
