using DXP;

namespace AdSamples.TechnologySetLicenseCheck
{
    /// <summary>
    /// Asks the licensing layer whether a technology set is licensed, as a hint of which Altium Designer license the
    /// running session uses.
    /// </summary>
    internal static class TechnologySetLicenseCheckCommands
    {
        /// <summary>The set to probe. It stands for "a license with schematic editing".</summary>
        private const string DefaultTechnologySet = "Schematic_Editor";

        /// <summary>CheckADLicense ["TechnologySet=&lt;name&gt;|Server=&lt;server&gt;"]: is the technology set supported?</summary>
        [HotCommand("CheckADLicense", "Check whether the Schematic_Editor technology set is licensed")]
        public static string CheckADLicense(CommandContext ctx)
        {
            string technologySet = ctx.Get("TechnologySet", DefaultTechnologySet);
            if (string.IsNullOrWhiteSpace(technologySet))
                technologySet = DefaultTechnologySet;

            // Server: which loaded server module to ask. Default is this extension's own server, which exists only
            // when the extension is installed; another extension that loads the Impl passes its own name.
            string server = ctx.Get("Server", SampleInfo.Name);
            if (string.IsNullOrWhiteSpace(server))
                server = SampleInfo.Name;

            bool supported = ServerSecurity(server).IsTechnologySetSupported(technologySet);

            // The AD license messages for the default set. For any other set, say plainly what was checked.
            if (technologySet == DefaultTechnologySet)
                return supported ? "AD License used." : "No AD License used.";
            return $"Technology set '{technologySet}' is {(supported ? "" : "not ")}supported.";
        }

        /// <summary>
        /// ListTechnologySets: the raw list IClient.GetTechnologySets returns, with its timestamp, so that a reader
        /// can see which set names the license actually reports.
        /// </summary>
        [HotCommand("ListTechnologySets", "List the technology sets the license reports")]
        public static string ListTechnologySets(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            uint validAt = 0;
            string sets = client.GetTechnologySets(ref validAt);

            // The SDK reference documents neither the list's separator nor the timestamp's unit, so both are
            // returned unchanged rather than parsed.
            return string.IsNullOrEmpty(sets)
                ? $"GetTechnologySets returned an empty list (ValidAtTimestamp={validAt})."
                : $"ValidAtTimestamp={validAt}\r\nTechnologySets={sets}";
        }

        /// <summary>
        /// In .NET the class DXP.ServerModule
        /// implements DXP.IServerSecurity (the compiler accepts the implicit conversion), but commands in the Impl
        /// project don't hold the module. They look it up by this extension's server name instead.
        /// </summary>
        private static IServerSecurity ServerSecurity(string serverName)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");
            IServerModule module = client.GetServerModuleByName(serverName)
                ?? throw new UserException(
                    $"Server module '{serverName}' was not found (is the extension installed and started?).");
            return module as IServerSecurity
                ?? throw new UserException(
                    $"Server module '{serverName}' ({module.GetType().FullName}) does not implement IServerSecurity.");
        }
    }
}
