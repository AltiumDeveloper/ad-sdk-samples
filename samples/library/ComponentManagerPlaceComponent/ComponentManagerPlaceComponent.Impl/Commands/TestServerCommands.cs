using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PCB; // extension methods (GetCurrentPCBBoard)
using SCH; // extension methods (GetCurrentSchDocument)

namespace AdSamples.ComponentManagerPlaceComponent
{
    /// <summary>
    /// Test setup: start and stop the local stub of the component management server.
    /// </summary>
    internal static class TestServerCommands
    {
        private const string DefaultId = "CM-TEST-1";
        private const string DefaultParams = "CM_Supplier:STRING:Test Supplier & Co;CM_Rating:FLOAT:0.25;CM_RoHS:BOOLEAN:True";

        /// <summary>
        /// StartTestServer "SchLib=&lt;path&gt;|PcbLib=&lt;path&gt;|LibRef=&lt;symbol&gt;|Footprint=&lt;footprint&gt;|Id=..|LibraryPath=..|Params=Name:Type:Value;..|Port=n".
        /// LibRef and Footprint default to a component of the active document (see DefaultsFromDocument).
        /// </summary>
        [HotCommand("StartTestServer", "Start a local stub component server (test setup)")]
        public static string StartTestServer(CommandContext ctx)
        {
            string schLib = ExistingFile(ctx, "SchLib", ".SchLib");
            string pcbLib = ExistingFile(ctx, "PcbLib", ".PcbLib");

            (string docLibRef, string docFootprint, string source) = DefaultsFromDocument();
            string libRef = ctx.Get("LibRef", docLibRef);
            string footprint = ctx.Get("Footprint", docFootprint);
            if (string.IsNullOrWhiteSpace(libRef) || string.IsNullOrWhiteSpace(footprint))
                throw new UserException("Pass LibRef=<symbol in the SchLib> and Footprint=<footprint in the PcbLib>, or make a " +
                                        "schematic sheet with a component that has a PCB footprint model the active document.");

            string portText = ctx.Get("Port", "0");
            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 0 || port > 65535)
                throw new UserException($"Port='{portText}' is not a port number (0 = any free port).");

            var config = new TestServerConfig
            {
                SchLibPath = schLib,
                PcbLibPath = pcbLib,
                Id = ctx.Get("Id", DefaultId),
                ComponentType = ctx.Get("ComponentType", "Test component"),
                AssembleTech = ctx.Get("AssembleTech", "SMT"),
                // The SCH command matches placed components on SourceLibraryName = LibraryPath. Placing from a file
                // records the file name there (without folder), hence this default.
                LibraryPath = ctx.Get("LibraryPath", Path.GetFileName(schLib)),
                LibraryRef = libRef.Trim(),
                FootprintPath = ctx.Get("FootprintPath", Path.GetFileName(pcbLib)),
                FootprintRef = footprint.Trim(),
                Parameters = ParseParams(ctx.Get("Params", DefaultParams)),
            };

            TestServer server;
            try
            {
                server = TestServer.Start(config, port);
            }
            catch (System.Net.Sockets.SocketException e)
            {
                throw new UserException($"Could not listen on 127.0.0.1:{port}: {e.Message}");
            }

            return $"Test server listening at {server.Url}: component '{config.Id}' = symbol '{config.LibraryRef}' " +
                   $"({Path.GetFileName(schLib)}, LibraryPath '{config.LibraryPath}'), footprint '{config.FootprintRef}' " +
                   $"({Path.GetFileName(pcbLib)}), {config.Parameters.Count} parameters " +
                   $"[{string.Join(", ", config.Parameters.Select(p => $"{p.Name}:{p.Type}={p.Value}"))}]" +
                   (source == null ? "" : $"; LibRef/Footprint defaults from {source}") +
                   $". Use: Url={server.Url}|SessionId=test|ComponentId={config.Id}";
        }

        [HotCommand("StopTestServer", "Stop the local stub component server")]
        public static string StopTestServer(CommandContext ctx)
        {
            TestServer server = TestServer.Stop();
            return server == null
                ? "No test server was running."
                : $"Stopped the test server at {server.Url} after {server.Requests} request(s); last: '{server.LastRequest}'.";
        }

        /// <summary>"Name:Type:Value;..." (the value may contain ':'); an empty Params= sends no parameters.</summary>
        private static List<ComponentParameter> ParseParams(string text)
        {
            var result = new List<ComponentParameter>();
            foreach (string item in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = item.Split(':', 3);
                if (parts.Length != 3 || parts[0].Trim().Length == 0)
                    throw new UserException($"Params entry '{item}' is not <Name>:<Type>:<Value>.");
                result.Add(new ComponentParameter(parts[0].Trim(), parts[1].Trim(), parts[2]));
            }
            return result;
        }

        private static string ExistingFile(CommandContext ctx, string name, string extension)
        {
            string path = ctx.Get(name)?.Trim().Trim('"');
            if (string.IsNullOrEmpty(path))
                throw new UserException($"Pass {name}=<full path to a {extension} file>.");
            if (!Path.IsPathRooted(path) || !File.Exists(path))
                throw new UserException($"{name}='{path}' is not an existing file (give the full path).");
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"{name}='{path}' is not a {extension} file.");
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// Symbol and footprint names from the active document, so the server can be started without knowing the
        /// fixture libraries' contents: on a schematic sheet, the first component with a current PCBLIB model
        /// (its LibReference and model name); on a board, the footprint of the first component (for both names).
        /// </summary>
        private static (string LibRef, string Footprint, string Source) DefaultsFromDocument()
        {
            SCH.ISch_Document sheet = SCH.GlobalVars.SchServer?.GetCurrentSchDocument();
            if (sheet != null)
            {
                foreach (SchPlacement.ComponentInfo info in SchPlacement.ComponentsOf(sheet))
                    if (!string.IsNullOrEmpty(info.CurrentFootprint))
                        return (info.LibReference, info.CurrentFootprint, $"{info.Designator} on {Path.GetFileName(sheet.GetState_DocumentName())}");
                return (null, null, null);
            }

            PCB.IPCB_Board board = PCB.GlobalVars.PCBServer?.GetCurrentPCBBoard();
            if (board != null)
            {
                string pattern = PcbPlacement.FirstFootprint(board, out string designator);
                if (!string.IsNullOrEmpty(pattern))
                    // The PCB command does not use libraryref; the footprint name stands in for it.
                    return (pattern, pattern, $"{designator} on {Path.GetFileName(board.GetState_FileName())}");
            }
            return (null, null, null);
        }
    }
}
