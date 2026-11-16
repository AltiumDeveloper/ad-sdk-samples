using DXP;
using PCB;

namespace AdSamples.PcbViewport
{
    /// <summary>
    /// Two independent reads in one command: a licence check through DXP.IServerSecurity,
    /// then the title of the current board's IPCB_Viewport.
    /// </summary>
    internal static class PcbViewportCommands
    {
        /// <summary>The technology set checked when no TechnologySet is passed.</summary>
        private const string DefaultTechnologySet = "Cam350";

        /// <summary>GetViewport ["TechnologySet=&lt;name&gt;"]: licence check result plus the viewport title.</summary>
        [HotCommand("GetViewport", "Show the PCB viewport title and a technology set licence check")]
        public static string GetViewport(CommandContext ctx)
        {
            string technologySet = ctx.Get("TechnologySet", DefaultTechnologySet);
            if (string.IsNullOrWhiteSpace(technologySet))
                technologySet = DefaultTechnologySet;

            // Step 1: the licence check needs no document.
            bool supported = ServerSecurity().IsTechnologySetSupported(technologySet);
            string licenceLine = $"Technology set '{technologySet}' supported: {(supported ? "true" : "false")}";

            // Logged before the viewport is touched, so the first result is not lost if the second step fails.
            ctx.Log(licenceLine);

            // Step 2: the current board. Without one the error says why.
            string nl = System.Environment.NewLine;
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException($"{licenceLine}{nl}The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException($"{licenceLine}{nl}No PCB document is active. Open and focus a .PcbDoc first.");
            string fileName = System.IO.Path.GetFileName(board.GetState_FileName());

            // Step 3: the viewport, checked for null. What object GetState_Viewport returns for a board is not
            // documented; it returns null on an ordinary open board, so this null branch is the one that fires.
            IPCB_Viewport viewport = board.GetState_Viewport();
            if (viewport == null)
                return $"{licenceLine}{nl}{fileName}: the board returned no viewport.";

            string title = viewport.GetState_ViewportTitle();
            return $"{licenceLine}{nl}{fileName}: ViewportTitle = '{title}'";
        }

        /// <summary>
        /// IServerSecurity is cast from a server module. Commands in the Impl project don't hold the module, so they
        /// look it up by this extension's server name and cast that.
        /// Every running server module casts to IServerSecurity, and the licence
        /// answer is a product-wide fact, not a per-module one. The extension's own module only exists once the
        /// extension is installed and started, so when it is not found (for example while the Impl runs inside
        /// another host during development) the first module that casts is used instead.
        /// </summary>
        private static IServerSecurity ServerSecurity()
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            if (client.GetServerModuleByName(SampleInfo.Name) is IServerSecurity own)
                return own;

            int count = client.GetCount();
            for (int i = 0; i < count; i++)
            {
                if (client.GetServerModule(i) is IServerSecurity any)
                    return any;
            }

            throw new UserException("No running server module implements IServerSecurity.");
        }
    }
}
