using System;
using System.Text;
using DXP;

namespace AdSamples.NavigationProvider
{
    /// <summary>
    /// Registers a navigation provider for a custom URL protocol (default "npdemo") with AD's navigation system, and
    /// shows what AD passes to it. The provider object itself is in the shim
    /// (NavigationProvider\ProtocolNavigationProvider.cs); these commands register and unregister it, navigate to a
    /// destination of the protocol, and report the calls it received.
    /// </summary>
    internal static class NavigationProviderCommands
    {
        // Not "wflw": that protocol is already handled by something else in AD. NavigateTo returns True for
        // wflw:// whether or not this provider is registered, and the provider is never called.
        // A protocol nobody else claims reaches the provider.
        private const string DefaultProtocol = "npdemo";
        // The default reaction to a destination: DXP.Utils.RunCommand("WorkspaceManager:NewProject", "ShowDialog=True").
        private const string DefaultRunCommand = "WorkspaceManager:NewProject";
        private const string DefaultRunParams = "ShowDialog=True";

        [HotCommand("RegisterProvider", "Register the navigation provider for a URL protocol")]
        public static string RegisterProvider(CommandContext ctx)
        {
            if (NavigationState.Handle != null)
                throw new UserException($"The provider is already registered for '{NavigationState.Protocol}'. Run UnregisterProvider first.");

            string protocol = ctx.Get("Protocol", DefaultProtocol);
            if (string.IsNullOrEmpty(protocol) || protocol.Contains(":") || protocol.Contains("/") || protocol.Contains("|"))
                throw new UserException($"Protocol '{protocol}': give the scheme only, such as 'npdemo' (without '://').");

            // The provider object comes from the shim: AD calls it back for as long as it is registered.
            var provider = AppDomain.CurrentDomain.GetData(NavigationState.InstanceKey) as INavigationProvider
                ?? throw new UserException("The shim's navigation provider is not available. Is the deployed NavigationProvider extension up to date?");

            INavigationSystem navigation = Navigation();
            // Returns a handle (UIntPtr) that identifies this registration; UnregisterNavigationProtocol needs it.
            UIntPtr handle = navigation.RegisterNavigationProvider(protocol, provider);

            NavigationState.Handle = handle;
            NavigationState.Protocol = protocol;
            // RunCommand= (empty) registers a provider that only records the calls.
            NavigationState.RunCommand = ctx.Get("RunCommand", DefaultRunCommand);
            NavigationState.RunParams = ctx.Get("RunParams", DefaultRunParams);

            return $"Registered the provider for '{protocol}://' (handle 0x{handle.ToUInt64():X}).\n" + Reaction();
        }

        [HotCommand("TriggerNavigation", "Navigate to a URL of the protocol and report the provider call")]
        public static string TriggerNavigation(CommandContext ctx)
        {
            string url = ctx.Get("Url", DefaultProtocol + "://test");
            INavigationSystem navigation = Navigation();

            // How AD splits the destination: the provider receives the same three parts.
            string protocol = string.Empty, target = string.Empty, parameters = string.Empty;
            navigation.ParseDestinationString(url, ref protocol, ref target, ref parameters);

            int callsBefore = NavigationState.Calls;
            string destination = url;
            bool result;
            // INavigationSystem has two entry points; Via=Scene uses NavigateToScene, whose signature (two out
            // views) matches INavigationProvider.NavigateTo.
            bool scene = string.Equals(ctx.Get("Via", "NavigateTo"), "Scene", StringComparison.OrdinalIgnoreCase);
            // From the pipe, the provider's command (a modal dialog by default) would block AD for everyone: it is
            // suppressed unless AllowRun is given. From the menu it runs, as a link would run it.
            NavigationState.Suppress = !ctx.IsInteractive && !ctx.Flag("AllowRun");
            try
            {
                // No current view (null): the navigation is not started from a document or panel.
                IExternalForm targetView;
                if (scene)
                    result = navigation.NavigateToScene((object)null, ref destination, out targetView, out IExternalForm _);
                else
                    result = navigation.NavigateTo((object)null, ref destination, out targetView);
                var text = new StringBuilder();
                text.AppendLine($"{(scene ? "NavigateToScene" : "NavigateTo")}('{url}') returned {result}; destination after the call: '{destination}'; target view: {(targetView == null ? "none" : "set")}.");
                text.AppendLine($"ParseDestinationString: protocol='{protocol}' target='{target}' parameters='{parameters}'.");
                int calls = NavigationState.Calls;
                text.AppendLine(calls > callsBefore
                    ? $"The provider was called {calls - callsBefore} time(s)."
                      + (NavigationState.Handle == null ? " (It is unregistered: AD still calls it, the sample ignores the call.)" : string.Empty)
                    : "The provider was not called." + (NavigationState.Handle == null ? " (It is not registered: run RegisterProvider.)" : string.Empty));
                AppendCalls(text);
                return text.ToString().TrimEnd();
            }
            finally
            {
                NavigationState.Suppress = false;
            }
        }

        [HotCommand("ProviderStatus", "Show the registration and the calls the provider received")]
        public static string ProviderStatus(CommandContext ctx)
        {
            var text = new StringBuilder();
            text.AppendLine(NavigationState.Handle is UIntPtr handle
                ? $"Registered for '{NavigationState.Protocol}://' (handle 0x{handle.ToUInt64():X}). " + Reaction()
                : "Not registered.");
            AppendCalls(text);
            return text.ToString().TrimEnd();
        }

        [HotCommand("UnregisterProvider", "Unregister the navigation provider")]
        public static string UnregisterProvider(CommandContext ctx)
        {
            if (!(NavigationState.Handle is UIntPtr handle))
                return "No provider is registered.";

            string protocol = NavigationState.Protocol;
            // The handle RegisterNavigationProvider returned identifies the registration to remove.
            Navigation().UnregisterNavigationProtocol(protocol, handle);
            NavigationState.Handle = null;
            return $"Unregistered the provider for '{protocol}://' (handle 0x{handle.ToUInt64():X}).";
        }

        /// <summary>
        /// The provider's reaction, called by the shim's provider for every destination it
        /// receives. The shim passes the three strings AD gave it as the context.
        /// </summary>
        [HotCommand("Navigation.Handle", "Handle one navigation request (the shim's provider calls this)")]
        public static string Handle(CommandContext ctx)
        {
            if (!(ctx.View is string[] args) || args.Length != 3)
                throw new UserException("Navigation.Handle is called by the shim's navigation provider, not by users. Use TriggerNavigation.");

            string protocol = args[0];
            // UnregisterNavigationProtocol does not stop the callbacks: AD
            // keeps calling the provider after UnregisterProvider. Ignore those calls, so unregistering still stops
            // the reaction.
            if (NavigationState.Handle == null)
                return NavigationState.Action($"ignored: called after UnregisterProvider (AD did not drop the registration)");
            // React to the own protocol only.
            if (!string.Equals(protocol, NavigationState.Protocol, StringComparison.OrdinalIgnoreCase))
                return NavigationState.Action($"ignored: protocol '{protocol}' is not '{NavigationState.Protocol}'");

            string command = NavigationState.RunCommand;
            if (string.IsNullOrEmpty(command))
                return NavigationState.Action("recorded only (no RunCommand)");
            if (NavigationState.Suppress)
                return NavigationState.Action($"{command} not run (pipe call without AllowRun)");

            DXP.Utils.RunCommand(command, NavigationState.RunParams);
            return NavigationState.Action($"ran {command} '{NavigationState.RunParams}'");
        }

        private static INavigationSystem Navigation() =>
            DXP.GlobalVars.Client?.GetNavigationSystem() ?? throw new UserException("AD's navigation system is not available.");

        private static string Reaction() =>
            string.IsNullOrEmpty(NavigationState.RunCommand)
                ? "On navigation it only records the call."
                : $"On navigation it runs {NavigationState.RunCommand} '{NavigationState.RunParams}'.";

        private static void AppendCalls(StringBuilder text)
        {
            text.AppendLine($"Calls received: {NavigationState.Calls}.");
            text.AppendLine("Last call: " + (NavigationState.LastCall ?? "none"));
            text.AppendLine("Last action: " + (NavigationState.LastAction ?? "none"));
            if (NavigationState.LastError != null)
                text.AppendLine("Last error: " + NavigationState.LastError);
        }
    }

    /// <summary>
    /// The registration and what the provider saw. Everything lives in AppDomain data (BCL types), because the shim's
    /// provider writes part of it and the registration must survive a hot reload of this implementation: the
    /// provider is in the shim, so a reload does not invalidate it and the registration is kept.
    /// </summary>
    internal static class NavigationState
    {
        // = ProtocolNavigationProvider.InstanceKey/CallsKey/LastCallKey/LastErrorKey in the shim.
        public const string InstanceKey = SampleInfo.Name + ".Provider";
        private const string CallsKey = SampleInfo.Name + ".Calls";
        private const string LastCallKey = SampleInfo.Name + ".LastCall";
        private const string LastErrorKey = SampleInfo.Name + ".LastError";
        private const string HandleKey = SampleInfo.Name + ".Handle";
        private const string ProtocolKey = SampleInfo.Name + ".Protocol";
        private const string RunCommandKey = SampleInfo.Name + ".RunCommand";
        private const string RunParamsKey = SampleInfo.Name + ".RunParams";
        private const string SuppressKey = SampleInfo.Name + ".Suppress";
        private const string LastActionKey = SampleInfo.Name + ".LastAction";

        /// <summary>The boxed UIntPtr RegisterNavigationProvider returned, or null when not registered.</summary>
        public static object Handle
        {
            get => AppDomain.CurrentDomain.GetData(HandleKey);
            set => AppDomain.CurrentDomain.SetData(HandleKey, value);
        }

        public static string Protocol
        {
            get => AppDomain.CurrentDomain.GetData(ProtocolKey) as string;
            set => AppDomain.CurrentDomain.SetData(ProtocolKey, value);
        }

        public static string RunCommand
        {
            get => AppDomain.CurrentDomain.GetData(RunCommandKey) as string;
            set => AppDomain.CurrentDomain.SetData(RunCommandKey, value);
        }

        public static string RunParams
        {
            get => AppDomain.CurrentDomain.GetData(RunParamsKey) as string ?? string.Empty;
            set => AppDomain.CurrentDomain.SetData(RunParamsKey, value);
        }

        public static bool Suppress
        {
            get => AppDomain.CurrentDomain.GetData(SuppressKey) is bool b && b;
            set => AppDomain.CurrentDomain.SetData(SuppressKey, value);
        }

        public static int Calls => AppDomain.CurrentDomain.GetData(CallsKey) is int n ? n : 0;
        public static string LastCall => AppDomain.CurrentDomain.GetData(LastCallKey) as string;
        public static string LastError => AppDomain.CurrentDomain.GetData(LastErrorKey) as string;
        public static string LastAction => AppDomain.CurrentDomain.GetData(LastActionKey) as string;

        /// <summary>Records what Navigation.Handle did and returns it.</summary>
        public static string Action(string what)
        {
            string text = $"{DateTime.Now:HH:mm:ss} {what}";
            AppDomain.CurrentDomain.SetData(LastActionKey, text);
            return text;
        }
    }
}
