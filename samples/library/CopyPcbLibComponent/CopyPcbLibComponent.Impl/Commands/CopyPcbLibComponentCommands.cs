using System;
using System.IO;
using DXP; // IServerDocumentView.GetOwnerDocument is an extension method in namespace DXP
using PCB;

namespace AdSamples.CopyPcbLibComponent
{
    /// <summary>
    /// Copies the footprint shown in the active PCB library into a new PCB library. The SDK lesson: clone a footprint
    /// with ReplicateWithChildren, move the clone to another library's board, register it there with
    /// RegisterComponent, and only then make it current with SetBoardToComponentByName.
    /// </summary>
    internal static class CopyPcbLibComponentCommands
    {
        private const string DefaultFileName = "CopyPcbLibComponent.PcbLib";

        // Process, parameters and save kind: the SDK reference does not document them.
        private const string NewLibraryProcess = "WorkspaceManager:OpenObject";
        private const string NewLibraryParameters = "ObjectKind=NewAnything|Kind=DefaultPcbLib";
        private const string SaveKind = "PCB Library File";

        /// <summary>
        /// CopyCurrentLibComponent "Name=&lt;footprint name&gt;|Out=&lt;folder or .PcbLib path&gt;|Overwrite=true":
        /// copies the current footprint into a new library saved as Out (default: the temp folder).
        /// </summary>
        [HotCommand("CopyCurrentLibComponent", "Copy the current footprint into a new PCB library")]
        public static string CopyCurrentLibComponent(CommandContext ctx)
        {
            string name = ctx.Get("Name", "TestComp");
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("The footprint name is empty. Pass it as a parameter: Name=<footprint name>.");

            string target = TargetPath(ctx.Get("Out", Path.GetTempPath()));
            if (File.Exists(target) && !ctx.Flag("Overwrite"))
                throw new UserException($"'{target}' already exists. Pass Overwrite=true to replace it, or choose another Out.");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbLib first.");
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("The Altium Designer client is not available.");

            // 1. The source: the current footprint of the active library. Clone it now, before the new library
            //    takes the focus (GetCurrentPCBLibrary follows the focused document).
            IPCB_Library source = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("No PCB library is active. Open and focus a .PcbLib first.");
            IPCB_LibComponent original = source.GetState_CurrentComponent()
                ?? throw new UserException("The PCB library has no current footprint.");
            string sourceFile = source.GetState_Board()?.GetState_FileName() ?? "";
            string sourceName = original.GetState_Pattern();

            // ReplicateWithChildren is declared on IPCB_Group and returns IPCB_Primitive: cast it back.
            var clone = original.ReplicateWithChildren() as IPCB_LibComponent
                ?? throw new UserException($"ReplicateWithChildren did not return a footprint for '{sourceName}'.");
            ctx.Log($"Cloned '{sourceName}' from {Path.GetFileName(sourceFile)}.");

            // 2. A new, empty PCB library, created by the workspace manager's own process.
            DXP.Utils.RunCommand(NewLibraryProcess, NewLibraryParameters);

            IServerDocument document = client.GetCurrentView()?.GetOwnerDocument()
                ?? throw new UserException($"{NewLibraryProcess} left no document in focus. Nothing was copied.");
            // Check that the focused document is the new library, so a failed process does not
            // rename and overwrite the source library (or any other document).
            if (!string.Equals(document.GetKind(), "PCBLIB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(document.GetFileName(), sourceFile, StringComparison.OrdinalIgnoreCase))
                throw new UserException(
                    $"{NewLibraryProcess} did not focus a new PCB library (focused: kind '{document.GetKind()}', " +
                    $"file '{document.GetFileName()}'). Nothing was copied.");

            // 3. Name the new document and save it (still without the copied footprint).
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            document.SetFileName(target);
            bool saved = document.DoFileSave(SaveKind);
            ctx.Log($"New library saved as {target}: {saved}.");

            // 4. Re-query: the current library is now the new one.
            IPCB_Library library = pcbServer.GetCurrentPCBLibrary()
                ?? throw new UserException("The new document is not a PCB library. Nothing was copied.");
            IPCB_Board board = library.GetState_Board();
            if (!string.Equals(board?.GetState_FileName(), target, StringComparison.OrdinalIgnoreCase))
                throw new UserException(
                    $"The current PCB library is '{board?.GetState_FileName()}', not the new '{target}'. Nothing was copied.");

            // 5. Rename the clone, move it to the new library's board and register it. RegisterComponent refuses an
            //    empty or already used name (reference: IPCB_Library.RegisterComponent), so check the result.
            int before = library.ComponentCount();
            clone.SetState_Pattern(name);
            clone.SetState_Board(board);
            if (!library.RegisterComponent(clone))
                throw new UserException(
                    $"RegisterComponent refused the copy named '{name}' ({before} footprints in the new library; " +
                    $"a free name would be '{library.GetUniqueCompName(name)}'). The new library is open and saved without it.");

            // RegisterComponent does not mark the document modified: without this the
            // tab shows no '*' and closing the library would drop the copy without asking.
            document.SetModified(true);

            // Registering does not make the footprint current: show it, then zoom to it.
            bool shown = library.SetBoardToComponentByName(name);
            DXP.Utils.RunCommand("PCB:Zoom", "Action=All");

            IPCB_LibComponent current = library.GetState_CurrentComponent();
            return $"Copied footprint '{sourceName}' from {Path.GetFileName(sourceFile)} into '{target}' as '{name}'.\n" +
                   $"New library: {before} footprints before, {library.ComponentCount()} after; " +
                   $"current footprint '{current?.GetState_Pattern() ?? "none"}' (SetBoardToComponentByName: {shown}).\n" +
                   $"The file was saved {(saved ? "" : "(DoFileSave returned false) ")}before the copy was added: " +
                   "save the new library to keep the footprint, or close it without saving.";
        }

        /// <summary>Out is a .PcbLib path, or a folder that gets the default file name.</summary>
        private static string TargetPath(string outValue)
        {
            string path = Path.GetFullPath(outValue);
            return string.Equals(Path.GetExtension(path), ".PcbLib", StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.Combine(path, DefaultFileName);
        }
    }
}
