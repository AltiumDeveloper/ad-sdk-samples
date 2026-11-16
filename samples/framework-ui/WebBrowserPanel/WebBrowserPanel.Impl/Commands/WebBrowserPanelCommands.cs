using AdSamples.WebBrowserPanel.Ui;

namespace AdSamples.WebBrowserPanel
{
    internal static class WebBrowserPanelCommands
    {
        /// <summary>
        /// Opens the sample's panel and loads the address in its browser. The panel itself (PanelInfo in the .ins,
        /// ServerPanelForm/ServerPanelView registered with the client) is the shared framework's shim panel.
        /// </summary>
        [HotCommand("NavigateToURL", "Load a web address in the Web Browser panel and show it")]
        public static string NavigateToURL(CommandContext ctx)
        {
            var text = ctx.Get("URL");
            if (string.IsNullOrWhiteSpace(text))
                throw new UserException("No URL parameter specified (URL=https://...).");
            var uri = PanelContent.ParseWebAddress(text)
                      ?? throw new UserException("Only absolute http:// or https:// addresses are accepted: '" + text + "'.");

            // IGUIManager.SetPanelVisibleInCurrentForm + SetPanelActiveInCurrentForm, then docks the content.
            SamplePanel.Show();
            var content = PanelContent.Current
                          ?? throw new UserException("The " + SamplePanel.PanelName + " panel is not available: redeploy the extension and restart Altium Designer.");
            content.Navigate(uri);

            // The page loads asynchronously: GetBrowserState reports the outcome.
            return ctx.IsInteractive ? null : "Navigating the " + SamplePanel.PanelName + " panel to " + uri.AbsoluteUri + ".";
        }

        /// <summary>Read-back for tests: what the browser was asked to load and what it actually shows.</summary>
        [HotCommand("GetBrowserState", "Report the Web Browser panel's address, page title and last navigation result")]
        public static string GetBrowserState(CommandContext ctx)
        {
            var content = PanelContent.Current;
            if (content == null)
                return "The " + SamplePanel.PanelName + " panel has no content yet: run NavigateToURL or Panel.Show first.";
            return content.Describe();
        }
    }
}
