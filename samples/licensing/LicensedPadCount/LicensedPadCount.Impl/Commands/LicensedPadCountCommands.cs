using DXP;
using PCB;

namespace AdSamples.LicensedPadCount
{
    /// <summary>
    /// Gates a command behind a licence check: CountPads asks the extension's own server module
    /// (DXP.IServerSecurity) whether a technology set is supported and only then counts the pads of the current board.
    /// </summary>
    internal static class LicensedPadCountCommands
    {
        /// <summary>
        /// CountPads ["TechnologySet=&lt;name&gt;"]: if the technology set is supported, walks every pad of the current
        /// board and returns the count; otherwise returns a "no licence" message and does not touch the board.
        /// The default set is this extension's server name.
        /// </summary>
        [HotCommand("CountPads", "Count the pads of the current board if a technology set is licensed")]
        public static string CountPads(CommandContext ctx)
        {
            string technologySet = TechnologySet(ctx);

            // The licence gate comes first, before any PCB access. A missing licence is a normal
            // outcome here, not an error, so it is returned as text rather than thrown.
            if (!ServerSecurity(ctx).IsTechnologySetSupported(technologySet))
                return $"No licence for technology set '{technologySet}': pads not counted.";

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            string fileName = System.IO.Path.GetFileName(board.GetState_FileName());

            int padCount = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.ePadObject });
                // The layer filter for all layers. In .NET the AddFilter_LayerSet_2 overload takes a
                // PCB.TLayerPartitionSet; PCBConstant.AllLayersSet is its all-layers value.
                iterator.AddFilter_LayerSet_2(PCBConstant.AllLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (IPCB_Primitive pad = iterator.FirstPCBObject(); pad != null; pad = iterator.NextPCBObject())
                {
                    padCount++;
                    if (padCount % 1000 == 0)
                        ctx.Log($"{padCount} pads so far...");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }

            return $"Technology set '{technologySet}' is licensed. {fileName}: {padCount} pad(s).";
        }

        /// <summary>
        /// CheckLicense ["TechnologySet=&lt;name&gt;"]: only the licence gate, without a PCB, so that the gate can be
        /// tried with a set that is licensed and with one that is not.
        /// </summary>
        [HotCommand("CheckLicense", "Check whether a technology set is licensed")]
        public static string CheckLicense(CommandContext ctx)
        {
            string technologySet = TechnologySet(ctx);
            bool supported = ServerSecurity(ctx).IsTechnologySetSupported(technologySet);
            return $"Technology set '{technologySet}' is {(supported ? "" : "not ")}supported.";
        }

        private static string TechnologySet(CommandContext ctx)
        {
            string technologySet = ctx.Get("TechnologySet", SampleInfo.Name);
            return string.IsNullOrWhiteSpace(technologySet) ? SampleInfo.Name : technologySet.Trim();
        }

        /// <summary>
        /// In .NET the class
        /// DXP.ServerModule implements DXP.IServerSecurity, but DXP.IServerModule does not, and commands in the Impl
        /// project don't hold the module. They look it up by this extension's server name and cast it.
        /// The optional Server parameter names another loaded server module instead: when this Impl is hot-loaded
        /// into a different host extension, only the host's server module is registered.
        /// </summary>
        private static IServerSecurity ServerSecurity(CommandContext ctx)
        {
            string serverName = ctx.Get("Server", SampleInfo.Name);
            if (string.IsNullOrWhiteSpace(serverName))
                serverName = SampleInfo.Name;
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");
            IServerModule module = client.GetServerModuleByName(serverName)
                ?? throw new UserException($"Server module '{serverName}' was not found.");
            return module as IServerSecurity
                ?? throw new UserException(
                    $"Server module '{serverName}' ({module.GetType().FullName}) does not implement IServerSecurity.");
        }
    }
}
