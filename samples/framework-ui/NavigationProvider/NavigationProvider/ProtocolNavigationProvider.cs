using System;
using System.Runtime.InteropServices;
using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3). The provider object AD calls back lives here, in the shim, so it survives
    /// hot reloads of the implementation: AD keeps a reference to it for as long as it is registered, and a provider
    /// inside a reloadable implementation would keep the old generation alive and run its stale code.
    /// It is created once, when AD creates the server module, but not registered: the RegisterProvider command
    /// registers it.
    /// </summary>
    public partial class PluginServerModule
    {
        // Keeps the provider reachable for the whole AD session (AD only holds a COM reference to it).
        private ProtocolNavigationProvider _navigationProvider;

        // The framework's constructor calls RegisterViews(client) once, after InitializeCommands. This sample has no
        // panel (no Panel.cs), so the partial method is free for creating the provider.
        partial void RegisterViews(IClient client)
        {
            _navigationProvider = new ProtocolNavigationProvider();
            // The implementation cannot reference shim types, so it finds the object through AppDomain data and uses
            // it as a DXP.INavigationProvider (the SDK assemblies are shared by the shim and the implementation).
            AppDomain.CurrentDomain.SetData(ProtocolNavigationProvider.InstanceKey, _navigationProvider);
        }
    }

    /// <summary>
    /// Called by AD's navigation system for every destination of the protocol it is registered for. It only records
    /// the call and forwards it: what to do is decided by the implementation's "Navigation.Handle" command, which
    /// hot-reloads in Debug builds like any other command.
    /// </summary>
    /// <remarks>
    /// DXP.INavigationProvider is a dispatch-only interface (InterfaceIsIDispatch) and AD calls the provider through
    /// it. This class is declared [ClassInterface(None)] + [ComDefaultInterface(typeof(INavigationProvider))], so the
    /// interface itself carries the dispatch ids. AD calls this declaration, and also a
    /// [ClassInterface(AutoDispatch)] + [DispId(67)] declaration: both work.
    /// </remarks>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(INavigationProvider))]
    public sealed class ProtocolNavigationProvider : SupportService, INavigationProvider
    {
        // AppDomain data keys, shared with the implementation (NavigationState in NavigationProvider.Impl).
        /// <summary>object: this provider (set once by the server module).</summary>
        public const string InstanceKey = SampleInfo.Name + ".Provider";

        /// <summary>int: NavigateTo calls received since AD started.</summary>
        public const string CallsKey = SampleInfo.Name + ".Calls";

        /// <summary>string: time and arguments of the last NavigateTo call.</summary>
        public const string LastCallKey = SampleInfo.Name + ".LastCall";

        /// <summary>string: the last exception the forwarding caught.</summary>
        public const string LastErrorKey = SampleInfo.Name + ".LastError";

        // No [DispId]: with ClassInterface(None) + ComDefaultInterface the interface itself carries the dispatch ids.

        public bool NavigateTo(string argProtocol, string argTarget, string argParameters, object argSourceView,
            out IExternalForm argTargetView, out IExternalForm argTargetHeaderView)
        {
            // No view is opened: the request is handled by running a command (or only recorded), so the provider
            // returns false with both views null.
            argTargetView = null;
            argTargetHeaderView = null;

            try
            {
                int calls = AppDomain.CurrentDomain.GetData(CallsKey) is int n ? n : 0;
                AppDomain.CurrentDomain.SetData(CallsKey, calls + 1);
                AppDomain.CurrentDomain.SetData(LastCallKey,
                    $"{DateTime.Now:HH:mm:ss} protocol='{argProtocol}' target='{argTarget}' parameters='{argParameters}' sourceView={(argSourceView == null ? "null" : "set")}");

                // The arguments travel as the command's context, not as a parameter string: a target may contain
                // '|' or '=', which the Key=Value|Key=Value format cannot carry.
                Commands.Invoke("Navigation.Handle", string.Empty, new[] { argProtocol, argTarget, argParameters });
            }
            catch (Exception ex)
            {
                // Never let an exception escape into AD's navigation system: keep it for ProviderStatus.
                AppDomain.CurrentDomain.SetData(LastErrorKey, $"{DateTime.Now:HH:mm:ss} {ex.GetType().Name}: {ex.Message}");
            }
            return false;
        }
    }
}
