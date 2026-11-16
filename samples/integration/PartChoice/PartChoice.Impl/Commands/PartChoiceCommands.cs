using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.PartChoice
{
    /// <summary>
    /// Finds a component item in the installed Vaults by its HRID and lists its part choices (the manufacturer
    /// parts approved for it) from the Vault's part catalog. The lesson is the chain from a Vault item to its
    /// part choices: vault manager, installed vault, item by HRID, the vault's IPartCatalog2 (by vault GUID),
    /// GetItemPartChoiceList (by item GUID), then the IPartChoice2 records.
    /// </summary>
    internal static class PartChoiceCommands
    {
        [HotCommand("GetPartChoiceInfo", "List the part choices of a Vault component")]
        public static string GetPartChoiceInfo(CommandContext ctx)
        {
            string hrid = ctx.Get("Hrid");
            if (string.IsNullOrWhiteSpace(hrid))
                throw new UserException("Pass Hrid=<component item HRID>, for example Hrid=CMP-00029-00000.");
            hrid = hrid.Trim();

            // The .NET API exposes the vault manager directly (no EDMSInterface.dll P/Invoke needed).
            IEDMS_VaultManager vaultManager = EDP.Utils.GetVaultManager()
                ?? throw new UserException("The Vault manager is not available in this Altium Designer session.");

            int vaultCount = vaultManager.GetInstalledVaultCount();
            if (vaultCount == 0)
                throw new UserException("No Vault is installed. Sign in to a workspace first.");

            // Vault= restricts the search to one installed vault; without it every vault is searched in order and
            // the first one holding the HRID wins.
            int first = 0, last = vaultCount - 1;
            string vaultText = ctx.Get("Vault");
            if (!string.IsNullOrEmpty(vaultText))
            {
                if (!int.TryParse(vaultText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    throw new UserException($"Vault='{vaultText}' is not a whole number.");
                if (index < 0 || index >= vaultCount)
                    throw new UserException($"Vault={index} is out of range: {vaultCount} Vault(s) installed (0..{vaultCount - 1}).");
                first = last = index;
            }

            IEDMS_Vault vault = null;
            IULB_Item item = null;
            int foundIndex = -1;
            var problems = new List<string>();
            int disabled = 0;
            for (int i = first; i <= last && item == null; i++)
            {
                IEDMS_Vault candidate = vaultManager.GetInstalledVault(i);
                if (candidate == null)
                {
                    problems.Add($"Vault {i}: could not be read");
                    continue;
                }
                // A disabled vault (Enabled false) still answers ItemByHRID, but GetPartCatalog then
                // throws E_UNEXPECTED. So the all-vault search skips disabled vaults; Vault=<i> still tries it.
                if (first != last && !candidate.GetEnabled())
                {
                    ctx.Log($"Skipping Vault {i} '{candidate.GetHRID()}': disabled.");
                    disabled++;
                    continue;
                }
                ctx.Log($"Looking up '{hrid}' in Vault {i} '{candidate.GetHRID()}'...");
                try
                {
                    // ItemByHRID takes the item HRID (no revision suffix) and is synchronous. Whether an unknown HRID
                    // or an unreachable vault gives null or a COM error is not documented: handle both and go on.
                    item = candidate.ItemByHRID(hrid);
                }
                catch (COMException ex)
                {
                    problems.Add($"Vault {i} '{candidate.GetHRID()}': {ex.Message}");
                    continue;
                }
                if (item != null)
                {
                    vault = candidate;
                    foundIndex = i;
                }
            }

            if (item == null)
            {
                string scope = first == last ? $"Vault {first}" : $"any of the {vaultCount - disabled} enabled Vault(s) ({disabled} disabled skipped)";
                string detail = problems.Count > 0 ? " Lookup errors: " + string.Join("; ", problems) + "." : "";
                throw new UserException($"No item '{hrid}' in {scope}.{detail}");
            }

            string vaultGuid = vault.GetVaultGUID();
            string itemGuid = item.GetGUID();
            string where = $"Item '{item.GetHRID()}' (GUID {itemGuid}) in Vault {foundIndex} '{vault.GetHRID()}'";

            // The part catalog is per vault and keyed by the vault GUID. EDP.Utils hands out the SCH interface.
            // It throws a plain System.Exception ("API_CreatePartCatalog returns error code ...") when it fails,
            // for example for a disabled vault.
            IPartCatalog2 catalog;
            try
            {
                catalog = EDP.Utils.GetPartCatalog(vaultGuid);
            }
            catch (Exception ex) when (ex is not UserException)
            {
                string state = vault.GetEnabled() ? "" : " (the vault is disabled)";
                throw new UserException($"{where}: GetPartCatalog failed{state}: {ex.Message}");
            }
            if (catalog == null)
                throw new UserException($"{where}: no part catalog for vault GUID {vaultGuid}.");

            IPartChoice2ListResult result = catalog.GetItemPartChoiceList(itemGuid)
                ?? throw new UserException($"{where}: GetItemPartChoiceList returned no result.");

            // The result carries its own outcome (IPartCatalog2Result): check it.
            if (!result.GetIsSuccess())
                throw new UserException($"{where}: GetItemPartChoiceList failed: {result.GetErrorMessage()}".TrimEnd());

            IPartChoice2List list = result.GetPartChoiceList();
            int count = list?.GetCount() ?? 0;

            var lines = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                // GetPartChoice(i) is the typed accessor of the IInterfaceList the list derives from.
                IPartChoice2 choice = list.GetPartChoice(i);
                if (choice == null)
                {
                    lines.AppendLine($"{i + 1}. (no record)");
                    continue;
                }
                lines.AppendLine($"{i + 1}. PartID: {choice.GetPartID()}, MPN: {choice.GetMPN()}, " +
                                 $"Manufacturer: {choice.GetManufacturerName()}, Description: {choice.GetDescription()}");
            }

            return $"{where}: {count} part choice(s)." + (count > 0 ? "\n" + lines.ToString().TrimEnd() : "");
        }
    }
}
