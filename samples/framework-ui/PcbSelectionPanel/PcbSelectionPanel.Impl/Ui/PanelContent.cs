using System;
using System.Windows.Forms;
using DXP;

namespace AdSamples
{
    internal static partial class SamplePanel
    {
        internal static partial Control CreateContent() => new PcbSelectionPanel.Ui.PanelContent();
    }
}

namespace AdSamples.PcbSelectionPanel.Ui
{
    /// <summary>
    /// The sample's global panel (Panel.Show): the log the notifications write and a
    /// Refresh button that lists the current selection. Rebuilt on every hot reload; the log itself lives in
    /// AppDomain data (PcbSelectionPanelCommands.LogKey), so a reload keeps it.
    /// </summary>
    internal sealed class PanelContent : UserControl
    {
        // The content of the current generation, so that the commands can refresh it. SamplePanel disposes the
        // content on unload, and Dispose clears this field, so no old generation stays reachable.
        private static PanelContent _current;

        private readonly TextBox _log;
        private readonly Label _selection;

        public PanelContent()
        {
            BackColor = DxpThemeManager.ColorPanelBody;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // the button
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // the selection summary
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // the log

            var refresh = new Button { Text = "Refresh selection", AutoSize = true, ForeColor = DxpThemeManager.ColorDefaultFont };
            refresh.Click += (_, _) => ShowCurrentSelection();
            layout.Controls.Add(refresh, 0, 0);

            _selection = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = DxpThemeManager.ColorPanelFont, Text = "" };
            layout.Controls.Add(_selection, 0, 1);

            _log = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };
            layout.Controls.Add(_log, 0, 2);
            Controls.Add(layout);

            _current = this;
            RefreshLog();
        }

        /// <summary>Called by the commands after they add log lines.</summary>
        internal static void RefreshAll() => _current?.RefreshLog();

        private void RefreshLog()
        {
            if (IsDisposed)
                return;
            _log.Text = string.Join(Environment.NewLine, PcbSelectionPanelCommands.LogLines());
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void ShowCurrentSelection()
        {
            // Through the shim, like a menu command, so a hot reload never swaps the module under it.
            try { _selection.Text = SamplePanel.Invoke("ShowSelection"); }
            catch (Exception ex) { _selection.Text = ex.Message; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && ReferenceEquals(_current, this))
                _current = null;
            base.Dispose(disposing);
        }
    }
}
