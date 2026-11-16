using System;
using System.IO;
using DXP; // IClient.GetCurrentView, IServerDocumentView.GetOwnerDocument and IServerDocument.GetServerModule are extension methods in DXP

namespace AdSamples.CamSelectedNetInfo
{
    /// <summary>
    /// The nets selected in the CAMtastic viewer can only be read through exports of the
    /// native CAMDXP.dll (CAMAPI_GetCurrentDocHandle, CAMAPI_CountSelectedNets, CAMAPI_GetSelectedNetIDs,
    /// CAMAPI_GetNetInfoFromNetNo). The .NET SDK has no CAMtastic types, and these samples do not P/Invoke into
    /// Altium Designer's native modules (the export signatures are documented nowhere, and a wrong
    /// signature would crash the Altium Designer process). This sample therefore keeps the part the managed
    /// API can do: find the active document view and decide whether it belongs to CAMtastic. See README, Notes.
    /// </summary>
    internal static class CamSelectedNetInfoCommands
    {
        // Name of the CAMtastic server (Updates = 'CAMTASTIC' in a CAMtastic extension's .ins). The kind and the
        // server module name of a CAM document may differ from this string: the returned text shows both values.
        private const string CamServerName = "CAMTASTIC";

        [HotCommand("GetSelectedNets", "Report the nets selected in the active CAMtastic document")]
        public static string GetSelectedNets(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // The active view is
            // checked first, so the command fails cleanly when no document (or a non-CAM document) is active.
            IServerDocumentView view = client.GetCurrentView()
                ?? throw new UserException("No document is active. Open and focus a CAMtastic (.Cam) document first.");

            IServerDocument document = view.GetOwnerDocument()
                ?? throw new UserException("The active view has no document. Open and focus a CAMtastic (.Cam) document first.");
            string kind = document.GetKind() ?? "";
            string module = document.GetServerModule()?.GetModuleName() ?? "";
            string file = Path.GetFileName(document.GetFileName() ?? "");

            bool isCam = string.Equals(kind, CamServerName, StringComparison.OrdinalIgnoreCase)
                      || string.Equals(module, CamServerName, StringComparison.OrdinalIgnoreCase);
            if (!isCam)
                throw new UserException(
                    $"The active document '{file}' is not a CAMtastic document (kind '{kind}', server '{module}'). " +
                    "Open and focus a CAMtastic (.Cam) document first.");

            // API gap: the managed SDK exposes no CAM net, net selection or net info. A net list (one line
            // per selected net: number, name, length, nodes) cannot be produced without the native CAMDXP.dll calls.
            return $"{file}: CAMtastic document (kind '{kind}', server '{module}').\r\n" +
                   "Selected nets: not available. The .NET SDK has no CAMtastic API for the net selection; " +
                   "it is only reachable through native CAMDXP.dll exports, which this sample does not call.";
        }
    }
}
