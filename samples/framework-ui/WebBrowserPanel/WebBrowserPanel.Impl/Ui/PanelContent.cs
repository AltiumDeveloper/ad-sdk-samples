using System;
using System.IO;
using System.Windows.Forms;
using DXP;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AdSamples
{
    internal static partial class SamplePanel
    {
        internal static partial Control CreateContent() => new WebBrowserPanel.Ui.PanelContent();
    }
}

namespace AdSamples.WebBrowserPanel.Ui
{
    /// <summary>
    /// The panel's content: an address bar, a status line and a Microsoft Edge WebView2 browser.
    /// Rebuilt on every hot reload; the address shown survives it as a string in AppDomain data.
    /// </summary>
    internal sealed class PanelContent : UserControl
    {
        /// <summary>AppDomain data key of the last address navigated to (a string, so it outlives this generation).</summary>
        private const string UrlKey = SampleInfo.Name + ".Url";

        private readonly TextBox _address;
        private readonly Label _status;
        private readonly WebView2 _browser;

        private string _initState = "pending";
        private string _lastNavigation = "none";

        public PanelContent()
        {
            BackColor = DxpThemeManager.ColorPanelBody;

            // Docked layout, no fixed pixel sizes (docs/guides/panels.mdx: fixed heights clip on scaled displays).
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // address bar
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // status line
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // browser

            _address = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = DxpThemeManager.ColorEditBoxBack,
                ForeColor = DxpThemeManager.ColorDefaultFont,
            };
            _address.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                GoToTypedAddress();
            };
            var go = new Button { Text = "Go", AutoSize = true, ForeColor = DxpThemeManager.ColorPanelFont };
            go.Click += (_, _) => GoToTypedAddress();

            _status = new Label
            {
                Dock = DockStyle.Fill, AutoSize = true, Text = "Type an http:// or https:// address and press Go.",
                ForeColor = DxpThemeManager.ColorPanelFont,
            };

            // The user data folder must be writable: WebView2's default is a folder next to the host executable,
            // which lies under Program Files. Set it before the browser initialises (on the first Source).
            _browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = DxpThemeManager.ColorPanelBody,
                CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = UserDataFolder },
            };
            _browser.CoreWebView2InitializationCompleted += OnInitializationCompleted;
            _browser.NavigationCompleted += OnNavigationCompleted;

            layout.Controls.Add(_address, 0, 0);
            layout.Controls.Add(go, 1, 0);
            layout.Controls.Add(_status, 0, 1);
            layout.SetColumnSpan(_status, 2);
            layout.Controls.Add(_browser, 0, 2);
            layout.SetColumnSpan(_browser, 2);
            Controls.Add(layout);

            Current = this;

            // After a hot reload (or a re-created panel) show the page that was open before.
            if (AppDomain.CurrentDomain.GetData(UrlKey) is string saved && ParseWebAddress(saved) is { } uri)
                Navigate(uri);
        }

        /// <summary>The content currently docked in the panel, or null before the panel was first filled.</summary>
        internal static PanelContent Current { get; private set; }

        /// <summary>Where WebView2 keeps its profile (cookies, cache) for this sample.</summary>
        internal static string UserDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdSamples", SampleInfo.Name, "WebView2");

        /// <summary>Accepts absolute http and https addresses only (no file:, javascript: or other schemes).</summary>
        internal static Uri ParseWebAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri))
                return null;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps ? uri : null;
        }

        /// <summary>
        /// Starts loading the address. Setting WebView2.Source before the browser exists starts its initialisation;
        /// the result arrives later in CoreWebView2InitializationCompleted and NavigationCompleted.
        /// </summary>
        internal void Navigate(Uri uri)
        {
            AppDomain.CurrentDomain.SetData(UrlKey, uri.AbsoluteUri);
            _address.Text = uri.AbsoluteUri;
            _status.Text = "Loading " + uri.AbsoluteUri + " ...";
            _lastNavigation = "in progress: " + uri.AbsoluteUri;
            _browser.Source = uri;
        }

        /// <summary>A text summary of the browser's state, for GetBrowserState.</summary>
        internal string Describe()
        {
            var core = _browser.CoreWebView2;
            return string.Join(Environment.NewLine,
                "Requested: " + (AppDomain.CurrentDomain.GetData(UrlKey) as string ?? "(none)"),
                "Initialisation: " + _initState,
                "Runtime: " + (core?.Environment.BrowserVersionString ?? "(not initialised)"),
                "Source: " + (core?.Source ?? "(none)"),
                "Title: " + (core?.DocumentTitle ?? "(none)"),
                "Last navigation: " + _lastNavigation,
                "User data folder: " + UserDataFolder);
        }

        private void GoToTypedAddress()
        {
            var uri = ParseWebAddress(_address.Text);
            if (uri == null)
            {
                _status.Text = "Not an absolute http:// or https:// address: " + _address.Text.Trim();
                return;
            }
            Navigate(uri);
        }

        private void OnInitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _initState = "ok";
                return;
            }
            // Typical causes: no WebView2 runtime installed, or a user data folder that cannot be written.
            _initState = "failed: " + e.InitializationException?.Message;
            _status.Text = "The browser could not start: " + e.InitializationException?.Message;
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var source = _browser.CoreWebView2?.Source;
            _lastNavigation = e.IsSuccess ? "ok: " + source : "failed (" + e.WebErrorStatus + "): " + source;
            _status.Text = e.IsSuccess ? _browser.CoreWebView2?.DocumentTitle : "Could not load the page: " + e.WebErrorStatus;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Current == this)
                    Current = null;
                // Disposing the WebView2 control (a child control) closes its browser processes.
                _browser.CoreWebView2InitializationCompleted -= OnInitializationCompleted;
                _browser.NavigationCompleted -= OnNavigationCompleted;
            }
            base.Dispose(disposing);
        }
    }
}
