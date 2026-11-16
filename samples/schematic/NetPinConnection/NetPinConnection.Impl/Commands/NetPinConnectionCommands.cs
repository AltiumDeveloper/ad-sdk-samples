using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;
using SCH;

namespace AdSamples.NetPinConnection
{
    /// <summary>
    /// Walks the nets of the focused project's flattened document and follows each net's pins (INet.DM_Pins) to the
    /// live schematic pin, to report "&lt;component designator&gt;-&lt;pin designator&gt;" per net.
    /// </summary>
    internal static class NetPinConnectionCommands
    {
        /// <summary>How many nets the returned text lists; the report file always has all of them.</summary>
        private const int NetsInSummary = 15;

        /// <summary>
        /// GetPinConnection "Out=&lt;folder&gt;|Open=true": writes NetPinConnection_&lt;project&gt;.txt and returns a summary.
        /// </summary>
        [HotCommand("GetPinConnection", "List the component pins of every net of the focused project")]
        public static string GetPinConnection(CommandContext ctx)
        {
            string outFolder = ctx.Get("Out", Path.GetTempPath());
            bool open = ctx.Flag("Open");

            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its documents.");

            // The nets exist only in the compiled model. Compile only when the project says it
            // needs it (NetItemReport compiles unconditionally instead).
            bool compiled = true;
            bool needsCompile = project.DM_NeedsCompile();
            if (needsCompile)
            {
                ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                compiled = project.DM_Compile();
            }

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            var report = new List<string>();
            var summary = new StringBuilder();
            int netCount, pinItems = 0, resolved = 0, unresolved = 0;

            // One read bracket covers all access to the compiled model, including the net walk, closed in a finally.
            dataModel.BeginRead();
            try
            {
                IDocument flattened = project.DM_DocumentFlattened()
                    ?? throw new UserException("The project has no flattened document. Does it contain a schematic sheet?");

                netCount = flattened.DM_NetCount();
                for (int i = 0; i < netCount; i++)
                {
                    INet net = flattened.DM_Nets(i);
                    if (net == null)
                        continue;
                    if (i % 50 == 0)
                        ctx.Log($"Net {i + 1}/{netCount}");

                    string netName = net.DM_NetName();
                    var pins = new List<string>();
                    int pinCount = net.DM_PinCount();
                    for (int j = 0; j < pinCount; j++)
                    {
                        INetItem item = net.DM_Pins(j);
                        if (item == null)
                            continue;
                        pinItems++;
                        string pin = PinText(dataModel, item);
                        if (pin == null)
                        {
                            unresolved++;
                            continue; // a pin that does not resolve is left out of the block
                        }
                        resolved++;
                        pins.Add(pin);
                    }

                    // The block format: "(", the net name, one line per pin, ")".
                    report.Add("(");
                    report.Add(netName);
                    report.AddRange(pins);
                    report.Add(")");

                    if (i < NetsInSummary)
                        summary.AppendLine($"  {netName}: {string.Join(" ", pins)}");
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            Directory.CreateDirectory(outFolder);
            string fileName = Path.Combine(outFolder,
                "NetPinConnection_" + Path.GetFileNameWithoutExtension(project.DM_ProjectFileName()) + ".txt");
            File.WriteAllLines(fileName, report);

            if (open)
            {
                IClient client = DXP.GlobalVars.Client;
                IServerDocument reportDocument = client.OpenDocument("Text", fileName);
                if (reportDocument != null)
                    client.ShowDocument(reportDocument);
            }

            var result = new StringBuilder();
            if (!compiled)
                result.AppendLine("Warning: DM_Compile returned false; the nets may be out of date.");
            result.AppendLine($"{project.DM_ProjectFileName()}: {netCount} nets, {pinItems} pin items, {resolved} resolved to " +
                              $"schematic pins, {unresolved} not resolved{(needsCompile ? " (compiled first)" : "")}.");
            if (unresolved > 0)
                result.AppendLine("  Pins resolve only for sheets open in an editor (seen in NetItemReport): open the project's sheets to resolve all pins.");
            result.Append(summary);
            if (netCount > NetsInSummary)
                result.AppendLine($"  ... {netCount - NetsInSummary} more nets in the report.");
            result.Append("Report: " + fileName);
            return result.ToString();
        }

        /// <summary>
        /// Resolves a pin item of a net (its DM_SchHandle) to the live SCH.ISch_Pin and returns
        /// "&lt;component designator&gt;-&lt;pin designator&gt;", or null when the handle does not resolve to a pin.
        /// </summary>
        private static string PinText(IUnifiedDataModel dataModel, INetItem item)
        {
            string handle = item.DM_SchHandle();
            if (string.IsNullOrEmpty(handle))
                return null;
            if (!dataModel.SupportsInterface(handle, out object obj) || obj == null)
                return null;

            try
            {
                // A cast to an SDK interface always succeeds (guide "Interfaces at run time"), so the pin test is the
                // object kind; only then is the object used as an ISch_Pin.
                var container = (ISch_BasicContainer)obj;
                if (container.GetState_ObjectId() != SCH.TObjectId.ePin)
                    return null;
                var pin = (ISch_Pin)obj;

                // In .NET the component's designator object
                // is ISch_Component.SchDesignator, and its text is ISch_Designator.Text.
                ISch_Component component = pin.OwnerSchComponent();
                string componentDesignator = component?.GetState_SchDesignator()?.GetState_Text() ?? "?";
                return componentDesignator + "-" + pin.GetState_Designator();
            }
            catch (COMException)
            {
                // Not a schematic object after all (a call on it failed): skip it like any other non-pin item.
                return null;
            }
        }
    }
}
