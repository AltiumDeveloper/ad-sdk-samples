using System;
using System.Windows.Forms;
using DXP;

namespace AdSamples
{
    internal static partial class SamplePanel
    {
        internal static partial Control CreateContent() => new ProjectBomReport.Ui.PanelContent();
    }
}

namespace AdSamples.ProjectBomReport.Ui
{
    /// <summary>
    /// The sample's panel: a Refresh button and the last report. Rebuilt on every hot reload; the report itself lives in AppDomain data
    /// (ProjectBomReportCommands.LastReportKey), so a reload keeps it.
    /// </summary>
    internal sealed class PanelContent : UserControl
    {
        // The content of the current generation, so that the command can refresh it. SamplePanel disposes the
        // content on unload, and Dispose clears this field, so no old generation stays reachable.
        private static PanelContent _current;

        private readonly CheckBox _linkage;
        private readonly Label _status;
        private readonly TextBox _report;

        public PanelContent()
        {
            BackColor = DxpThemeManager.ColorPanelBody;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // button and option
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // status
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // the report

            var refresh = new Button { Text = "Refresh", AutoSize = true, ForeColor = DxpThemeManager.ColorDefaultFont };
            refresh.Click += (_, _) => RunReport();
            layout.Controls.Add(refresh, 0, 0);

            _linkage = new CheckBox { Text = "Library linkage", Checked = true, AutoSize = true, ForeColor = DxpThemeManager.ColorPanelFont };
            layout.Controls.Add(_linkage, 1, 0);

            _status = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = DxpThemeManager.ColorPanelFont, Text = "" };
            layout.Controls.Add(_status, 0, 1);
            layout.SetColumnSpan(_status, 2);

            _report = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.None,
                BackColor = DxpThemeManager.ColorEditBoxBack, ForeColor = DxpThemeManager.ColorDefaultFont,
            };
            layout.Controls.Add(_report, 0, 2);
            layout.SetColumnSpan(_report, 2);
            Controls.Add(layout);

            _current = this;
            ShowLastReport();
        }

        /// <summary>Called by the command after it stored a new report.</summary>
        internal static void RefreshAll() => _current?.ShowLastReport();

        private void ShowLastReport()
        {
            if (IsDisposed)
                return;
            string text = AppDomain.CurrentDomain.GetData(ProjectBomReportCommands.LastReportKey) as string;
            _report.Text = text ?? "No report yet. Focus a project and click Refresh.";
        }

        private void RunReport()
        {
            // Through the shim, like a menu command, so a hot reload never swaps the module under it.
            try
            {
                _status.Text = "";
                SamplePanel.Invoke("ProjectBomReport", "Linkage=" + (_linkage.Checked ? "true" : "false"));
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && ReferenceEquals(_current, this))
                _current = null;
            base.Dispose(disposing);
        }
    }
}
