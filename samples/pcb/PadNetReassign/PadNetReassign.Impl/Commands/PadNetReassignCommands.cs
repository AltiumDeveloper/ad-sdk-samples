using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DXP; // the IClient.GetDXPWorkspace/GetDocumentByPath extension methods live in namespace DXP
using EDP;
using PCB;

namespace AdSamples.PadNetReassign
{
    /// <summary>
    /// Moves primitives between nets the way the PCB editor expects: the old net gives the primitive up with
    /// CancelGroupWarehouseRegistration, the primitive gets its new Net, and the new net takes it with
    /// RegisterWithGroupWarehouse, all between the primitive's BeginModify/EndModify and inside PreProcess/PostProcess.
    /// </summary>
    internal static class PadNetReassignCommands
    {
        private const string NoNet = "(no net)";

        /// <summary>
        /// UpdatePad "Net=&lt;name&gt;|AddProjectParam|ParamName=testParam1|ParamValue=1234|Redraw": moves every pad of the
        /// current board to one net (default: the first net the iterator returns).
        /// Destructive: the board is changed in memory and becomes modified.
        /// </summary>
        [HotCommand("UpdatePad", "Move every pad of the board to one net")]
        public static string UpdatePad(CommandContext ctx)
        {
            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");

            // The project parameter is opt-in, and the project is resolved first so that a missing project fails before anything is changed.
            bool addParam = ctx.Flag("AddProjectParam");
            IProject project = null;
            if (addParam)
            {
                var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace;
                project = workspace?.DM_FocusedProject()
                    ?? throw new UserException("AddProjectParam needs a focused project. Focus the board's project first.");
            }

            string wanted = ctx.Get("Net");
            IPCB_Net target = FindNet(board, wanted);
            string targetName = target.GetState_Name();

            List<IPCB_Pad> pads = CollectPads(board);
            ctx.Log($"Moving {pads.Count} pads to net '{targetName}'...");

            int moved = 0, alreadyThere = 0;
            pcbServer.PreProcess();
            try
            {
                foreach (IPCB_Pad pad in pads)
                {
                    pad.BeginModify();
                    try
                    {
                        IPCB_Net oldNet = pad.GetState_Net();
                        if (oldNet != null && oldNet.GetState_Name() == targetName)
                            alreadyThere++;
                        // The old net must release the pad first: RegisterWithGroupWarehouse does nothing while the
                        // pad's InNet is still true. A pad already on the target net goes through the same pair.
                        oldNet?.CancelGroupWarehouseRegistration(pad);
                        pad.SetState_Net(target);
                        target.RegisterWithGroupWarehouse(pad);
                    }
                    finally
                    {
                        pad.EndModify();
                    }
                    moved++;
                }
            }
            finally
            {
                // PreProcess and PostProcess must always be paired, or the PCB editor stays suspended.
                pcbServer.PostProcess();
            }

            if (ctx.Flag("Redraw"))
                board.ViewManager_FullUpdate(); // opt-in

            // Read back: RegisterWithGroupWarehouse clears Net instead of adding a primitive that is not on a signal
            // layer, the multi-layer or the connection layer, so count the pads that really ended on the target net.
            int onTarget = pads.Count(p => p.GetState_Net()?.GetState_Name() == targetName);

            var lines = new List<string>
            {
                $"{Path.GetFileName(board.GetState_FileName())}: {moved} pads moved to net '{targetName}' " +
                $"({alreadyThere} were already on it); {onTarget} of {pads.Count} pads are now on '{targetName}'.",
            };
            if (onTarget != pads.Count)
                lines.Add($"{pads.Count - onTarget} pads are not on '{targetName}' after RegisterWithGroupWarehouse.");

            if (addParam)
                lines.Add(AddProjectParameter(project, ctx.Get("ParamName", "testParam1"), ctx.Get("ParamValue", "1234")));

            lines.Add("The board is modified in memory: close it without saving to discard the change.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// ListPadNets "Top=&lt;n&gt;": read-only. Counts the pads of the current board per net, to check UpdatePad.
        /// </summary>
        [HotCommand("ListPadNets", "Count the pads of each net (read-only)")]
        public static string ListPadNets(CommandContext ctx)
        {
            IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
            int top = int.TryParse(ctx.Get("Top", "10"), out int n) && n > 0 ? n : 10;

            List<IPCB_Pad> pads = CollectPads(board);
            var byNet = pads
                .GroupBy(p => p.GetState_Net()?.GetState_Name() ?? NoNet)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lines = new List<string>
            {
                $"{Path.GetFileName(board.GetState_FileName())}: {pads.Count} pads on {byNet.Count(g => g.Key != NoNet)} nets, " +
                $"{byNet.Where(g => g.Key == NoNet).Sum(g => g.Count())} pads without a net.",
            };
            lines.AddRange(byNet.Take(top).Select(g => $"    {g.Key} : {g.Count()} pads"));
            if (byNet.Count > top)
                lines.Add($"    ... {byNet.Count - top} more (Top=<n> to list more)");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>The net with the given name, or the first net the iterator returns when no name is given.</summary>
        private static IPCB_Net FindNet(IPCB_Board board, string name)
        {
            int count = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.eNetObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                // FirstPCBObject returns IPCB_Primitive: cast to IPCB_Net.
                for (var net = iterator.FirstPCBObject() as IPCB_Net; net != null; net = iterator.NextPCBObject() as IPCB_Net)
                {
                    count++;
                    if (string.IsNullOrEmpty(name) || string.Equals(net.GetState_Name(), name, StringComparison.OrdinalIgnoreCase))
                        return net;
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            throw new UserException(string.IsNullOrEmpty(name)
                ? "The board has no nets."
                : $"No net named '{name}' on the board ({count} nets). ListPadNets shows the net names.");
        }

        /// <summary>
        /// All pads of the board, collected before any change so the changes cannot disturb the iteration.
        /// </summary>
        private static List<IPCB_Pad> CollectPads(IPCB_Board board)
        {
            var pads = new List<IPCB_Pad>();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { TObjectId.ePadObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var pad = iterator.FirstPCBObject() as IPCB_Pad; pad != null; pad = iterator.NextPCBObject() as IPCB_Pad)
                    pads.Add(pad);
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return pads;
        }

        /// <summary>Adds a parameter to the project, marks the project document modified, and reads the value back.</summary>
        private static string AddProjectParameter(IProject project, string name, string value)
        {
            project.DM_AddParameter(name, value);

            // The project document is marked modified so that AD offers to save the parameter.
            DXP.IServerDocument doc = DXP.GlobalVars.Client.GetDocumentByPath(project.DM_ProjectFullPath());
            doc?.SetModified(true);

            // DM_AddParameter appends a second entry when the name already exists (adding the
            // same name twice takes DM_ParameterCount from 1 to 2, the new entry first), so read back
            // the first match: that is the one just added.
            string readBack = null;
            for (int i = 0; i < project.DM_ParameterCount(); i++)
            {
                IParameter p = project.DM_Parameters(i);
                if (p != null && string.Equals(p.DM_Name(), name, StringComparison.OrdinalIgnoreCase))
                {
                    readBack = p.DM_Value();
                    break;
                }
            }
            return $"Project parameter {name}={value} added to {Path.GetFileName(project.DM_ProjectFullPath())} " +
                   $"(read back: {(readBack == null ? "not found" : "'" + readBack + "'")}; project document " +
                   $"{(doc == null ? "not open, not marked modified" : "marked modified")}).";
        }
    }
}
