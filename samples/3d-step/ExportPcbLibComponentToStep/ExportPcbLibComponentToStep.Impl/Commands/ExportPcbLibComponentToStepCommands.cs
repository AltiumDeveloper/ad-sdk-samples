using System;
using System.Collections.Generic;
using System.IO;
using DXP; // the IClient.OpenDocument/ShowDocument/CloseDocument/GetDocumentByPath extension methods live in namespace DXP
using PCB;

namespace AdSamples.ExportPcbLibComponentToStep
{
    /// <summary>
    /// Exports the 3D model of every footprint of a PCB library to its own STEP file. The SDK lesson: a footprint of a
    /// library is turned into a transient IPCB_Component (PCBObjectFactory + LoadFromLibrary), whose SaveModelToFile
    /// writes the STEP file. IPCB_LibComponent.SaveModelToFile is the direct alternative (Via=LibComponent).
    /// </summary>
    internal static class ExportPcbLibComponentToStepCommands
    {
        /// <summary>
        /// GenerateStep "Lib=&lt;.PcbLib path&gt;|Out=&lt;folder&gt;|Via=Component|LibComponent|Attach=false": writes
        /// &lt;Out&gt;/&lt;library name&gt;/&lt;footprint&gt;.step for every footprint that has a 3D model. Without Lib, the
        /// focused PCB library is used. The library itself is not changed.
        /// </summary>
        [HotCommand("GenerateStep", "Export every footprint of a PcbLib to STEP files")]
        public static string GenerateStep(CommandContext ctx)
        {
            string via = ctx.Get("Via", "Component").Trim();
            bool viaComponent = via.Equals("Component", StringComparison.OrdinalIgnoreCase);
            if (!viaComponent && !via.Equals("LibComponent", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Via must be Component or LibComponent (got '{via}').");
            // The library's board is assigned to the transient component before LoadFromLibrary, as the reference
            // remarks describe the SDK examples doing. Without a board
            // (Attach=false) makes LoadFromLibrary raise an access violation in ADVPCB.DLL on the first footprint.
            bool attach = !ctx.Get("Attach", "true").Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            string libValue = ctx.Get("Lib");
            IServerDocument openedHere = null;
            IPCB_Library library;
            string libPath;

            if (string.IsNullOrWhiteSpace(libValue))
            {
                library = pcbServer.GetCurrentPCBLibrary()
                    ?? throw new UserException("No PCB library is active. Focus a .PcbLib, or pass Lib=<path to .PcbLib>.");
                libPath = library.GetState_Board()?.GetState_FileName() ?? "";
            }
            else
            {
                libPath = Path.GetFullPath(libValue.Trim().Trim('"'));
                if (!File.Exists(libPath))
                    throw new UserException($"PCB library not found: {libPath}");

                // An open document is reused,
                // and a document opened by this command is closed again at the end.
                if (client.GetDocumentByPath(libPath) == null)
                {
                    openedHere = client.OpenDocument("PCBLib", libPath)
                        ?? throw new UserException($"Altium Designer could not open {libPath} as a PCB library.");
                }
                try
                {
                    library = FindLibrary(pcbServer, client, libPath);
                }
                catch
                {
                    CloseIfOpenedHere(client, openedHere);
                    throw;
                }
            }

            // LoadFromLibrary names the library by its file (FileName=...), so the library must exist on disk.
            if (!File.Exists(libPath))
            {
                CloseIfOpenedHere(client, openedHere);
                throw new UserException($"The PCB library has no file on disk ('{libPath}'). Save it first.");
            }

            string folder = Path.Combine(Path.GetFullPath(ctx.Get("Out", Path.GetTempPath())),
                                         Path.GetFileNameWithoutExtension(libPath));

            int footprints = 0, saveTrue = 0;
            var written = new List<string>();
            var noFile = new List<string>();
            var loadFailed = new List<string>();

            IPCB_LibraryIterator iterator = library.LibraryIterator_Create();
            try
            {
                Directory.CreateDirectory(folder);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (IPCB_Primitive item = iterator.FirstPCBObject(); item != null; item = iterator.NextPCBObject())
                {
                    if (item is not IPCB_LibComponent footprint)
                        continue;
                    footprints++;

                    // The footprint name is Pattern; IPCB_LibComponent has no Name member in .NET.
                    string name = footprint.GetState_Pattern();
                    int bodies = footprint.GetPrimitiveCount(new PCB.TObjectSet { TObjectId.eComponentBodyObject });
                    string file = Path.Combine(folder, UniqueFileName(name, usedNames) + ".step");
                    ctx.Log($"{footprints}: {name} ({bodies} 3D bodies)");

                    // Neither SaveModelToFile documents its result as "file written" (the LibComponent one is
                    // documented as not set), so the file on disk decides: new, or rewritten since the call started.
                    DateTime? before = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : (DateTime?)null;

                    bool saved;
                    if (viaComponent)
                    {
                        bool? result = ExportViaComponent(pcbServer, library, libPath, name, file, attach);
                        if (result == null)
                        {
                            loadFailed.Add(name);
                            continue;
                        }
                        saved = result.Value;
                    }
                    else
                    {
                        saved = footprint.SaveModelToFile(file, false); // false: STEP, not a part
                    }
                    if (saved)
                        saveTrue++;

                    bool fileWritten = File.Exists(file) &&
                                       (before == null || File.GetLastWriteTimeUtc(file) > before.Value);
                    if (fileWritten)
                        written.Add($"{name} ({bodies} bodies, {new FileInfo(file).Length} bytes)");
                    else
                        noFile.Add($"{name} ({bodies} bodies)");
                }
            }
            finally
            {
                library.LibraryIterator_Destroy(ref iterator);
                CloseIfOpenedHere(client, openedHere);
            }

            string method = viaComponent
                ? "IPCB_Component.LoadFromLibrary + SaveModelToFile" + (attach ? " (board attached)" : " (no board)")
                : "IPCB_LibComponent.SaveModelToFile";
            var lines = new List<string>
            {
                $"{Path.GetFileName(libPath)}: {written.Count} of {footprints} footprints written as STEP to {folder} via {method}.",
                $"SaveModelToFile returned true {saveTrue} times."
            };
            if (written.Count > 0)
                lines.Add("Written: " + string.Join(", ", written));
            if (noFile.Count > 0)
                lines.Add("No file: " + string.Join(", ", noFile));
            if (loadFailed.Count > 0)
                lines.Add("LoadFromLibrary returned false: " + string.Join(", ", loadFailed));
            if (openedHere != null)
                lines.Add("The library was opened for the export and closed again.");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// A component created with PCBObjectFactory, never added to a board, loaded from
        /// the library file, then asked to save its model. The library's board is assigned first
        /// (attach): without a board, LoadFromLibrary raises an access violation. Returns null when LoadFromLibrary fails, otherwise the
        /// result of SaveModelToFile. The component is freed afterwards.
        /// </summary>
        private static bool? ExportViaComponent(IPCB_ServerInterface pcbServer, IPCB_Library library, string libPath,
                                                string footprintName, string file, bool attach)
        {
            IPCB_Primitive primitive = pcbServer.PCBObjectFactory(TObjectId.eComponentObject, TDimensionKind.eNoDimension,
                                                                  TObjectCreationMode.eCreate_Default);
            if (primitive is not IPCB_Component component)
            {
                pcbServer.DestroyPCBObject(ref primitive);
                throw new UserException("PCBObjectFactory did not create a component.");
            }
            try
            {
                if (attach)
                    component.SetState_Board(library.GetState_Board());

                // '|' separates the Name=Value pairs; FileName and Footprint are the keys.
                if (!component.LoadFromLibrary("FileName=" + libPath + "|Footprint=" + footprintName))
                    return null;
                return component.SaveModelToFile(file);
            }
            finally
            {
                if (attach)
                    component.SetState_Board(null);
                pcbServer.DestroyPCBObject(ref primitive);
            }
        }

        /// <summary>
        /// The library of a document that is open in the workspace. GetPCBLibraryByPath leaves the focus alone;
        /// showing the document and taking GetCurrentPCBLibrary is the fallback.
        /// </summary>
        private static IPCB_Library FindLibrary(IPCB_ServerInterface pcbServer, IClient client, string libPath)
        {
            IPCB_Library library = pcbServer.GetPCBLibraryByPath(libPath);
            if (library != null)
                return library;

            IServerDocument document = client.GetDocumentByPath(libPath)
                ?? throw new UserException($"{libPath} is not open in the workspace.");
            client.ShowDocument(document);
            library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException($"{libPath} is open but is not a PCB library.");

            string current = library.GetState_Board()?.GetState_FileName();
            if (!string.Equals(current, libPath, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The current PCB library is '{current}', not '{libPath}'.");
            return library;
        }

        private static void CloseIfOpenedHere(IClient client, IServerDocument document)
        {
            if (document != null)
                client.CloseDocument(document);
        }

        /// <summary>The footprint name without characters a file name can't have, made unique within the folder.</summary>
        private static string UniqueFileName(string name, HashSet<string> used)
        {
            string clean = name ?? "";
            foreach (char c in Path.GetInvalidFileNameChars())
                clean = clean.Replace(c.ToString(), "");
            if (clean.Length == 0)
                clean = "_";

            string candidate = clean;
            for (int i = 2; !used.Add(candidate); i++)
                candidate = $"{clean}_{i}";
            return candidate;
        }
    }
}
