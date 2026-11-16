using System;
using System.Globalization;
using System.Text;
using DXP;

namespace AdSamples.VaultItemSearchByHrid
{
    /// <summary>
    /// Shows the Vault Explorer panel and searches the items of an installed Vault by HRID with
    /// IEDMS_Vault.SearchItems. The lesson: the vault manager from EDP.Utils, the option and flag sets that
    /// SearchItems takes, and reading an EDMS item result (wait, check the outcome, cast the list entries).
    /// </summary>
    internal static class VaultItemSearchByHridCommands
    {
        // The panel name passed to SetPanelVisibleInCurrentForm. It is still a valid name (the panel the UI calls
        // Explorer); an unknown name is ignored silently.
        private const string DefaultPanel = "Vault Explorer";

        [HotCommand("BrowseVault", "Show Vault Explorer and search vault items by HRID")]
        public static string BrowseVault(CommandContext ctx)
        {
            string hrid = ctx.Get("Hrid", "").Trim();
            if (hrid.Length == 0)
                throw new UserException("Pass the item HRID to search for, e.g. Hrid=PCLCMP-0001-00010.");
            int vaultIndex = IntParam(ctx, "Vault", 0);
            int max = IntParam(ctx, "Max", 100);
            int timeoutSeconds = IntParam(ctx, "Timeout", 30);
            if (max < 1)
                throw new UserException($"Max={max}: pass at least 1.");
            // Showing a panel is a UI side effect; on the pipe path it is off unless asked for (ShowPanel=true).
            bool showPanel = BoolParam(ctx, "ShowPanel", ctx.IsInteractive);
            string panel = ctx.Get("Panel", DefaultPanel);

            if (showPanel)
            {
                IGUIManager gui = DXP.GlobalVars.GUIManager
                    ?? throw new UserException("The GUI manager is not available.");
                // Shows the panel by its name; the call returns nothing, so an unknown name cannot be detected here
                // (IsPanelValidInCurrentForm(name) could check it first).
                gui.SetPanelVisibleInCurrentForm(panel, true);
            }

            // No need for LoadLibrary("EDMSInterface.dll") + GetProcAddress("API_GetEDMS_VaultManager").
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
            ctx.Log($"Searching Vault {vaultIndex} {vaultName} for HRID '{hrid}'...");

            // HRID filter only (descriptions, content types, folders, life cycles and
            // naming schemes empty), the effective item revision option, edmsWait + edmsForceRefresh, no callback.
            IULB_ItemsResult result = vault.SearchItems(
                hrid,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                max,
                new TEDMS_GetOptionSet(TEDMS_GetOption.eGetOption_GetEffectiveItemRevision),
                new TEDMS_VaultFlagSet(TEDMS_VaultFlag.edmsWait, TEDMS_VaultFlag.edmsForceRefresh),
                null,
                null)
                ?? throw new UserException($"SearchItems returned no result for Vault {vaultName}.");

            // With edmsWait the call already blocks (as it does for GetFolders); WaitUntilDone guards the timeout.
            if (!result.WaitUntilDone(timeoutSeconds) || !result.IsDone())
                throw new UserException($"The item search in Vault {vaultName} did not finish within {timeoutSeconds} s.");

            // A failed request still completes: check both the result list and the output.
            IEDM_RecordResultList outcome = result.GetResult();
            if (outcome != null && !outcome.GetSuccess())
                throw new UserException($"SearchItems failed for Vault {vaultName}: {FirstNonEmpty(outcome.GetMessage(), outcome.GetFirstErrorMessage())}".TrimEnd());
            IEDM_Result output = result.GetOutput();
            if (output != null && !output.GetSuccess())
                throw new UserException($"SearchItems failed for Vault {vaultName}: {output.GetMessage()}".TrimEnd());

            IInterfaceList items = result.GetItems();
            int count = items?.GetCount() ?? 0;
            string header = $"Vault {vaultIndex} of {vaultCount} {vaultName}: {count} item(s) for HRID '{hrid}'" +
                            (showPanel ? $" (panel '{panel}' requested)." : ".");
            if (count == 0)
                return header;

            var lines = new StringBuilder(header);
            for (int i = 0; i < count; i++)
            {
                // IInterfaceList.Get returns object: cast to the record type.
                if (items.Get(i) is not IULB_Item item)
                {
                    lines.Append($"\n[{i}] (not an IULB_Item)");
                    continue;
                }
                // One "HRID  Description  CreatedByName" line per hit.
                lines.Append($"\n{item.GetHRID()}  {item.GetDescription()}  {item.GetCreatedByName()}");
            }

            // eGetOption_GetEffectiveItemRevision asks for the effective revisions; report how many came back (one
            // item revision per item).
            int revisions = result.GetItemRevisions()?.GetCount() ?? 0;
            lines.Append($"\n{revisions} item revision(s) in the result.");
            return lines.ToString();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
                if (!string.IsNullOrEmpty(v))
                    return v;
            return string.Empty;
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

        private static bool BoolParam(CommandContext ctx, string name, bool defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrEmpty(text))
                return defaultValue;
            if (bool.TryParse(text, out bool value))
                return value;
            throw new UserException($"{name}='{text}' must be true or false.");
        }
    }
}
