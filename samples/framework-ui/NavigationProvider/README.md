# Register a custom URL protocol navigation provider

Registers an object with Altium Designer's navigation system so that destinations of a custom URL protocol
(default `npdemo://`) are handed to the extension, which reacts by running an AD command (by default
`WorkspaceManager:NewProject` with `ShowDialog=True`). It shows how a .NET extension implements a COM callback
interface that AD calls through IDispatch, and how to register and unregister it.

## What it shows
- `DXP.IClient.GetNavigationSystem`: AD's navigation system, reached through `DXP.GlobalVars.Client`.
- `DXP.INavigationSystem.RegisterNavigationProvider`: registers a `DXP.INavigationProvider` for a protocol name
  (the scheme without `://`) and returns a `UIntPtr` handle that identifies the registration.
- `DXP.INavigationSystem.UnregisterNavigationProtocol`: removes the registration again, with the protocol and that
  handle.
- `DXP.INavigationProvider.NavigateTo`: the callback. AD passes the protocol, target and parameters of the
  destination. The provider returns `false` with no target views: it handles the request by running a command
  instead of opening a view.
- `DXP.INavigationSystem.ParseDestinationString` / `DXP.INavigationSystem.NavigateTo`: split a destination string
  into protocol, target and parameters, and navigate to it from code (exercises the provider without a link).
- `DXP.SupportService`: the base class of the provider.
- `DXP.Utils.RunCommand`: runs an AD command with a parameter string from inside the callback.
- COM callback declaration: `DXP.INavigationProvider` is a dispatch-only interface whose `NavigateTo` has
  dispatch ID 67 in the SDK assembly. The provider class is declared `[ClassInterface(ClassInterfaceType.None)]`
  + `[ComDefaultInterface(typeof(INavigationProvider))]`. `AutoDispatch` + `[DispId(67)]` also works.

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample NavigationProvider` and Altium Designer restarted.

1. No document is needed.
2. **NavigationProvider › Register Provider**, or `tools/ad.ps1 -Sample NavigationProvider run RegisterProvider "RunCommand="`
   (with `RunCommand=` empty the provider only records calls).
3. **NavigationProvider › Trigger Navigation**, or `tools/ad.ps1 -Sample NavigationProvider run TriggerNavigation "Url=npdemo://test"`:
   the result says whether the provider was called and with which arguments. From the menu, with the default
   registration, the New Project dialog opens. From the pipe the command is not run unless `AllowRun` is given.
4. **NavigationProvider › Unregister Provider**. AD still calls the provider after this (see Notes). A new
   **Trigger Navigation** reports that the call came in and that the sample ignored it.

| Command | Parameters | Result |
|---|---|---|
| `RegisterProvider` | `Protocol=<scheme>` (default `npdemo`), `RunCommand=<process id>` (default `WorkspaceManager:NewProject`, empty = record only), `RunParams=<params>` (default `ShowDialog=True`) | `Registered the provider for 'npdemo://' (handle 0x…)` and what it runs |
| `TriggerNavigation` | `Url=<destination>` (default `npdemo://test`), `AllowRun` (pipe only: run the provider's command) | the `NavigateTo` result, the parsed parts, how often the provider was called, its last call and action |
| `ProviderStatus` | none | the registration, the call count, the last call, action and error |
| `UnregisterProvider` | none | `Unregistered the provider for 'npdemo://' (handle 0x…)`, or `No provider is registered.` |

Registering twice without unregistering fails with `The provider is already registered for '<protocol>'`.

## How it works
`NavigationProvider/ProtocolNavigationProvider.cs` (shim) creates one `ProtocolNavigationProvider` when AD creates
the server module and publishes it in AppDomain data. It does not register it. Its `NavigateTo` counts the call,
records the arguments and forwards them to the implementation's `Navigation.Handle` command. It catches every
exception, so none reaches AD's navigation system.

`NavigationProvider.Impl/Commands/NavigationProviderCommands.cs`: `RegisterProvider` takes the shim's object as a
`DXP.INavigationProvider` and calls `RegisterNavigationProvider`. It stores the handle, the protocol and the
command to run in AppDomain data (`NavigationState`), so they survive a hot reload of the implementation.
`Navigation.Handle` ignores other protocols and calls that arrive after
`UnregisterProvider`. Otherwise it runs the command with `DXP.Utils.RunCommand`.
`TriggerNavigation` calls `ParseDestinationString` and `NavigateTo` (no current view), then compares the call
count. `UnregisterProvider` calls `UnregisterNavigationProtocol` with the stored handle.

## Notes
- **Use a protocol nobody else handles.** `wflw` does not work. The registration
  succeeds and returns a handle, but `INavigationSystem.NavigateTo` returns `True` for `wflw://` whether or not the
  provider is registered, and the provider is never called. There is no error and no message. Something else in
  AD handles `wflw`. The same provider registered for `npdemo` is called on every navigation, so the sample's
  default is `npdemo`.
- Both class declarations are called by AD: `ClassInterface(None)` +
  `ComDefaultInterface(typeof(INavigationProvider))` (the shim), and `AutoDispatch` with
  `[DispId(67)]` on `NavigateTo`.
- **`UnregisterNavigationProtocol` does not work.** It is called with the exact `UIntPtr` handle that
  `RegisterNavigationProvider` returned and returns without error, but the provider keeps receiving navigations.
  Treat a registration as permanent for the AD session. This is another reason the provider lives in the shim.
  `Navigation.Handle` therefore ignores calls after `UnregisterProvider`. Registering again gives a new handle, and
  the provider is then called once per navigation, not twice.
- The provider object lives in the shim, not in the implementation. AD keeps a reference to it while it is
  registered. A provider in the hot-reloaded implementation would keep the old generation alive and run stale
  code. For the same reason, a hot reload does not unregister it. A hot reload of the implementation keeps the
  registration, and the next call reaches the new generation.
- The registration lives as long as AD's session, or until **Unregister Provider**. Nothing unregisters it when AD
  closes.
- The default command opens a modal dialog. A navigation that starts from the pipe (`TriggerNavigation` from
  `tools/ad.ps1`) does not run it unless `AllowRun` is given, so the pipe never blocks AD.
- `TriggerNavigation` passes no current view (`null`) to `NavigateTo`. AD accepts this: the provider receives
  `argSourceView` as null.
- Behavior of the navigation system:
  - The protocol is registered without `://` (`npdemo`).
  - AD splits a destination at `://` and then at the first `?`. So `npdemo://folder/item?mode=1` reaches the
    provider as protocol `npdemo`, target `folder/item`, parameters `mode=1`, exactly what
    `ParseDestinationString` reports.
  - The provider is called synchronously from `NavigateTo` and from `NavigateToScene`.
  - `NavigateTo` returns the provider's own result. A provider that returns `false` makes `NavigateTo` return
    `false`, and AD shows nothing.
  - A protocol that **no provider claims is not simply "not handled"**: AD passes it on to the Windows shell.
    `TriggerNavigation "Url=zzzz://test"` opens Windows' modal "Pick an app" dialog for `zzzz`. So links to a
    protocol whose provider failed to register turn into shell URL launches. Do not navigate to an unregistered
    protocol from unattended code.
