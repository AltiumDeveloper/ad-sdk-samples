using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;
using SCH;

namespace AdSamples.HarnessPortTypes
{
    /// <summary>
    /// Lists the harness ports of the focused schematic sheet. The ports come from the project's data model (EDP),
    /// each port's schematic handle is resolved to the live SCH object through the unified data model, and the
    /// object is asked for its harness type through SCH.IHarnessTypeHolder.
    /// </summary>
    internal static class HarnessPortTypesCommands
    {
        [HotCommand("GetHarness", "List the harness ports of the focused sheet")]
        public static string GetHarness(CommandContext ctx)
        {
            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");
            IProject project = workspace.DM_FocusedProject()
                ?? throw new UserException("No project is focused. Open a schematic project and focus one of its sheets.");

            // The data model only knows the sheet's ports after a compile; compile only when needed.
            bool compiled = true;
            if (project.DM_NeedsCompile())
            {
                ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
                compiled = project.DM_Compile();
            }

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            var lines = new StringBuilder();
            string sheetName;
            int portCount, distinctPorts = 0, harnessCount = 0, unresolved = 0;
            // DM_Ports yields one net item per net of a port, so a harness port appears once per harness signal
            // (all entries share the same DM_SchHandle). Report each drawn port once.
            var seenHandles = new HashSet<string>(StringComparer.Ordinal);

            // Every access to the compiled model, including resolving handles, happens inside one read bracket.
            dataModel.BeginRead();
            try
            {
                IDocument sheet = workspace.DM_FocusedDocument()
                    ?? throw new UserException("No document is focused. Focus a schematic sheet of the project.");
                sheetName = sheet.DM_FileName();
                // The extension check avoids asking a PcbDoc for its ports.
                if (!string.Equals(Path.GetExtension(sheetName), ".SchDoc", StringComparison.OrdinalIgnoreCase))
                    throw new UserException($"The focused document '{sheetName}' is not a schematic sheet. Focus a .SchDoc.");

                portCount = sheet.DM_PortCount();
                for (int i = 0; i < portCount; i++)
                {
                    INetItem port = sheet.DM_Ports(i);
                    if (port == null)
                        continue;

                    string handle = port.DM_SchHandle();
                    if (!string.IsNullOrEmpty(handle) && !seenHandles.Add(handle))
                        continue;   // another net of a port already reported above
                    distinctPorts++;

                    ISch_Port schPort = ResolvePort(dataModel, handle);
                    if (schPort == null)
                    {
                        unresolved++;
                        continue;
                    }

                    // ISch_Port does not derive from IHarnessTypeHolder in the .NET interfaces; the cast goes through
                    // COM at run time. Casts to SDK interfaces always succeed (docs: Interfaces at run time), so a
                    // missing interface shows up as a COMException on the call, which counts as "not resolved".
                    var holder = (IHarnessTypeHolder)schPort;
                    try
                    {
                        if (!holder.GetState_IsHarnessObject())
                            continue;
                        harnessCount++;
                        lines.AppendLine($"  {port.DM_PortName()}: harness type '{holder.GetState_HarnessType()}'");
                    }
                    catch (COMException)
                    {
                        unresolved++;
                    }
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            var result = new StringBuilder();
            if (!compiled)
                result.AppendLine("Warning: DM_Compile returned false; the ports may be out of date.");
            result.AppendLine($"{sheetName}: {distinctPorts} port(s) from {portCount} data-model entries, " +
                              $"{harnessCount} harness port(s), {unresolved} not resolved.");
            if (harnessCount == 0)
                result.AppendLine("  No harness ports on this sheet.");
            result.Append(lines);
            return result.ToString().TrimEnd();
        }

        /// <summary>
        /// Resolves a data-model handle (INetItem.DM_SchHandle) to the live schematic port, or null when the handle
        /// does not resolve or the object is not a port.
        /// </summary>
        private static ISch_Port ResolvePort(IUnifiedDataModel dataModel, string schHandle)
        {
            if (string.IsNullOrEmpty(schHandle))
                return null;
            if (!dataModel.SupportsInterface(schHandle, out object obj) || obj == null)
                return null;

            // Branch on the object kind instead of a type test (type tests succeed for every SDK object).
            var container = (ISch_BasicContainer)obj;
            try
            {
                return container.GetState_ObjectId() == TObjectId.ePort ? (ISch_Port)obj : null;
            }
            catch (COMException)
            {
                return null;
            }
        }
    }
}
