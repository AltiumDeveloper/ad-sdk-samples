using System.Globalization;
using System.Text;
using DXP;

namespace AdSamples.GetVaultFolder
{
    /// <summary>
    /// Connects to one of the Vaults (workspaces) installed in Altium Designer and lists the folders that
    /// IEDMS_Vault.GetFolders returns for the root. The lesson is the EDMS call pattern: get the vault manager,
    /// pick an installed vault, start an asynchronous request, wait for it, then check the outcome before reading it.
    /// </summary>
    internal static class GetVaultFolderCommands
    {
        [HotCommand("GetVaultFolders", "List the folders of an installed Vault")]
        public static string GetVaultFolders(CommandContext ctx)
        {
            int vaultIndex = IntParam(ctx, "Vault", 0);
            int timeoutSeconds = IntParam(ctx, "Timeout", 30);
            int max = IntParam(ctx, "Max", 200);

            // The .NET API exposes the vault manager directly; no P/Invoke is needed.
            IEDMS_VaultManager vaultManager = EDP.Utils.GetVaultManager()
                ?? throw new UserException("The Vault manager is not available in this Altium Designer session.");

            int vaultCount = vaultManager.GetInstalledVaultCount();
            if (vaultCount == 0)
                throw new UserException("No Vault is installed. Sign in to a workspace first.");
            if (vaultIndex < 0 || vaultIndex >= vaultCount)
                throw new UserException($"Vault={vaultIndex} is out of range: {vaultCount} Vault(s) installed (0..{vaultCount - 1}).");

            IEDMS_Vault vault = vaultManager.GetInstalledVault(vaultIndex)
                ?? throw new UserException($"Installed Vault {vaultIndex} could not be read.");
            string vaultName = $"'{vault.GetHRID()}' ({vault.GetServiceURL()})";
            ctx.Log($"Requesting folders of Vault {vaultIndex} {vaultName}...");

            // No parent folder GUID (the root), no folder type filter, recursive,
            // edmsWait, no callback and no context. Folders holds only the top-level folders
            // either way; recursive=true additionally loads each folder's subtree into its SubFolder list.
            IULB_FoldersResult result = vault.GetFolders(
                null,
                string.Empty,
                true,
                new TEDMS_VaultFlagSet(TEDMS_VaultFlag.edmsWait),
                null,
                null)
                ?? throw new UserException($"GetFolders returned no result for Vault {vaultName}.");

            // WaitUntilDone returns false on timeout.
            if (!result.WaitUntilDone(timeoutSeconds) || !result.IsDone())
                throw new UserException($"The folder request to Vault {vaultName} did not finish within {timeoutSeconds} s.");

            // The request can complete and still fail (no connection, not signed in): read the outcome first.
            IEDM_Result output = result.GetOutput();
            if (output != null && !output.GetSuccess())
                throw new UserException($"GetFolders failed for Vault {vaultName}: {output.GetMessage()} {output.GetFaultCode()}".TrimEnd());

            IInterfaceList folders = result.GetFolders();
            int count = folders?.GetCount() ?? 0;

            var lines = new StringBuilder();
            int below = 0;
            for (int i = 0; i < count; i++)
            {
                // IInterfaceList.Get returns object: cast to the record type.
                if (folders.Get(i) is not IULB_Folder folder)
                    continue;
                int subtree = CountSubFolders(folder);
                below += subtree;
                if (i < max)
                    lines.AppendLine($"{folder.GetHRID()} ({folder.GetSubFolderCount()} subfolder(s), {subtree} in its subtree)");
            }
            if (count > max)
                lines.AppendLine($"... {count - max} more (raise Max= to see them)");

            return $"Vault {vaultIndex} of {vaultCount} {vaultName}: {count} top-level folder(s), {below} folder(s) below them." +
                   (count > 0 ? "\n" + lines.ToString().TrimEnd() : "");
        }

        // With recursive=true the subtree hangs off each folder (SubFolder[i]); it is not in the flat Folders list.
        private static int CountSubFolders(IULB_Folder folder)
        {
            int n = folder.GetSubFolderCount();
            int total = n;
            for (int i = 0; i < n; i++)
                if (folder.GetSubFolder(i) is IULB_Folder sub)
                    total += CountSubFolders(sub);
            return total;
        }

        private static int IntParam(CommandContext ctx, string name, int defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrEmpty(text))
                return defaultValue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new UserException($"{name}='{text}' is not a whole number.");
            return value;
        }
    }
}
