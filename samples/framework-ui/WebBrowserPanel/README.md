# Show a web page in a dockable panel

Hosts a web browser in a dockable Altium Designer panel and loads an address passed as a command parameter. It
teaches how to put a third-party WinForms control (Microsoft Edge WebView2, which Altium Designer ships) into an
extension panel, and how to open that panel from a command.

## What it shows
- `DXP.ServerPanelForm` / `DXP.ServerPanelView` with `DXP.IClient.AddServerView`: the panel's form and view,
  registered with the client under the `PanelInfo` name (the shared framework's shim panel, `framework/shim/Panel.cs`).
- `DXP.IGUIManager.SetPanelVisibleInCurrentForm` and `DXP.IGUIManager.SetPanelActiveInCurrentForm`: open the panel
  by its `PanelInfo` name from a command (`SamplePanel.Show`).
- `DXP.DxpThemeManager.ColorPanelBody`, `DXP.DxpThemeManager.ColorPanelFont`, `DXP.DxpThemeManager.ColorEditBoxBack`,
  `DXP.DxpThemeManager.ColorDefaultFont`: theme colours for the address bar, the status line and the browser's
  background.
- `Microsoft.Web.WebView2.WinForms.WebView2` (not an Altium API): referenced from Altium Designer's `System` folder
  with `Private=false`, so the extension uses the same assembly as AD. Its profile goes to a writable
  `CoreWebView2CreationProperties.UserDataFolder`.

## Run it
1. No document is needed. The panel is declared in the `.ins`, so the extension must be deployed with
   `tools/deploy.ps1 -Sample WebBrowserPanel` and Altium Designer restarted.
2. **WebBrowserPanel › Navigate To URL** (opens `https://www.altium.com`), or
   `tools/ad.ps1 -Sample WebBrowserPanel run NavigateToURL "URL=https://www.altium.com"`
3. The **WebBrowserPanel** panel opens and loads the page. The address bar shows the address, and the status line
   shows the page title once it has loaded. **Browser State** (`GetBrowserState`) reports the outcome as text.

| Command | Parameters | Result |
|---|---|---|
| `NavigateToURL` | `URL=<http(s) address>` (required) | Opens the panel and starts loading the page. From `tools/ad.ps1` it returns `Navigating the WebBrowserPanelPanel panel to <url>.`; from the menu it returns nothing. |
| `GetBrowserState` | none | Helper: requested address, initialisation result, WebView2 runtime version, current source, page title, last navigation result and user data folder |
| `Panel.Show` | none | Opens the panel without navigating |

## How it works
`WebBrowserPanelCommands.cs`: `NavigateToURL` reads `URL` with `ctx.Get`. It accepts only absolute `http`/`https`
addresses, and throws a `UserException` when the parameter is missing or invalid. Then it calls `SamplePanel.Show`
(framework): `SetPanelVisibleInCurrentForm`, `SetPanelActiveInCurrentForm`, then it docks the content into the
shim's empty host control. Last, it calls `PanelContent.Navigate`.

`Ui/PanelContent.cs` builds the content as a `TableLayoutPanel`: an address box with a **Go** button (Enter works
too), a status line and the `WebView2` control. `Navigate` sets `WebView2.Source`. The first time, that also starts
the browser's asynchronous initialisation, with the user data folder set beforehand in `CreationProperties`.
`CoreWebView2InitializationCompleted` and `NavigationCompleted` record their results for the status line and
`GetBrowserState`. The last address is kept as a string in AppDomain data, so the page comes back after a hot
reload re-creates the content.

## Notes
- The sample uses WebView2, which Altium Designer ships in `System` (`Microsoft.Web.WebView2.Core/.WinForms`
  1.0.2420.47, `WebView2Loader.dll`), not CefSharp. Altium Designer runs its own CEF from versioned subfolders of
  `<AD>\System\Cef`; a second `Cef.Initialize` next to it, or a `Cef.Shutdown` on panel close, could break Altium
  Designer. WebView2 needs the Microsoft Edge WebView2 runtime, which Windows 11 includes. Without the runtime, the status line and
  `GetBrowserState` report the initialisation error.
- The WebView2 profile (cookies, cache) is stored in `%LOCALAPPDATA%\AdSamples\WebBrowserPanel\WebView2`. WebView2's
  default location is next to the host executable, under Program Files, and is not writable.
- The implementation resolves AD's `System` copy of `Microsoft.Web.WebView2.Core/.WinForms` at
  run time (referenced with `Private=false`, not copied to the output), and a second WebView2 environment with its
  own user data folder works alongside Altium Designer's own use of WebView2.
- A failing navigation is reported in the status line and by `GetBrowserState` (for an unresolvable host the
  `CoreWebView2WebErrorStatus` is `ConnectionAborted`); Altium Designer shows no dialog.
- Parameters are split on `|`, so a URL containing `|` cannot be passed to `NavigateToURL`. `=` inside the URL
  (query strings) is fine. Type such addresses in the panel's address box instead.
- The panel is named `WebBrowserPanelPanel` (the framework's convention), not `Web Browser`, which avoids a clash
  with any panel of that name. There is no hotkey.
- Hot reload (Debug): the content, including the browser, is disposed and re-created on every reload. Disposing the
  `WebView2` control closes its browser processes.
