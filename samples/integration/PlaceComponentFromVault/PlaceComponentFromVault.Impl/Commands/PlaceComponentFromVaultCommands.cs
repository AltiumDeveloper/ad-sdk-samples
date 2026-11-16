using System.Globalization;
using System.Runtime.InteropServices;
using DXP;

namespace AdSamples.PlaceComponentFromVault
{
    /// <summary>
    /// Looks up a component item revision in an installed Vault by its HRID and hands it to the workspace with
    /// IDXPWorkSpace.DM_UseItemRevision, which starts placing it into the active document: the API equivalent of
    /// placing a component from the Components / Explorer panel. The lesson is the chain of identifiers the call
    /// needs: the Vault GUID, the item GUID and the item revision GUID.
    /// </summary>
    internal static class PlaceComponentFromVaultCommands
    {
        [HotCommand("PlaceVaultComponent", "Place a Vault component by item revision HRID")]
        public static string PlaceVaultComponent(CommandContext ctx)
        {
            string hrid = ctx.Get("Hrid");
            if (string.IsNullOrWhiteSpace(hrid))
                throw new UserException("Pass Hrid=<item revision HRID>, for example Hrid=CMP-0001-00000-1.");
            hrid = hrid.Trim();
            int vaultIndex = IntParam(ctx, "Vault", 0);
            bool dryRun = ctx.Flag("DryRun");

            // The .NET API exposes the vault manager directly: no need to load EDMSInterface.dll and call its
            // exported API_GetEDMS_VaultManager through P/Invoke.
            IEDMS_VaultManager vaultManager = EDP.Utils.GetVaultManager()
                ?? throw new UserException("The Vault manager is not available in this Altium Designer session.");

            int vaultCount = vaultManager.GetInstalledVaultCount();
            if (vaultCount == 0)
                throw new UserException("No Vault is installed. Sign in to a workspace first.");
            if (vaultIndex < 0 || vaultIndex >= vaultCount)
                throw new UserException($"Vault={vaultIndex} is out of range: {vaultCount} Vault(s) installed (0..{vaultCount - 1}).");

            IEDMS_Vault vault = vaultManager.GetInstalledVault(vaultIndex)
                ?? throw new UserException($"Installed Vault {vaultIndex} could not be read.");
            // The vault's HRID only labels the messages; if reading it fails, carry on with the index alone.
            string vaultName;
            try { vaultName = $"'{vault.GetHRID()}'"; }
            catch (COMException) { vaultName = "(HRID unreadable)"; }

            // The destination is the active document; without it there is nothing to place into. Which document kinds DM_UseItemRevision accepts is not documented.
            IServerDocumentView view = DXP.GlobalVars.Client.GetCurrentView();
            IServerDocument document = view?.GetOwnerDocument();
            if (!dryRun && document == null)
                throw new UserException("Open and focus a document (a .SchDoc) to place the component into.");

            ctx.Log($"Looking up '{hrid}' in Vault {vaultIndex} {vaultName}...");

            // ItemRevisionByHRID is a synchronous lookup (no flags, callback or context, unlike the IEDMS_Vault
            // Get* requests). Whether an unknown HRID gives null or a COM error is not documented: handle both.
            IULB_ItemRevision revision;
            try
            {
                revision = vault.ItemRevisionByHRID(hrid);
            }
            catch (COMException ex)
            {
                throw new UserException($"Looking up '{hrid}' in Vault {vaultIndex} {vaultName} failed: {ex.Message}");
            }
            if (revision == null)
                throw new UserException($"No item revision '{hrid}' in Vault {vaultIndex} {vaultName}.");

            // DM_UseItemRevision takes three GUIDs: the vault, the item and this revision of the item.
            string vaultGuid = vault.GetVaultGUID();
            string itemGuid = revision.GetItemGUID();
            string revisionGuid = revision.GetGUID();
            string found = $"Item revision '{revision.GetHRID()}' (item '{revision.GetItemHRID()}') in Vault {vaultIndex} {vaultName}: " +
                           $"vault {vaultGuid}, item {itemGuid}, revision {revisionGuid}.";

            if (dryRun)
                return found + " DryRun: not placed.";

            // Starts the placement into the active document. The last argument is passed as ""; its parameter
            // format is not documented.
            bool started = DXP.GlobalVars.Client.GetDXPWorkspace().DM_UseItemRevision(vaultGuid, itemGuid, revisionGuid, string.Empty);

            string target = $"{document.GetKind()} '{System.IO.Path.GetFileName(document.GetFileName())}'";
            if (!started)
                throw new UserException(found + $" DM_UseItemRevision returned false for {target}.");
            return found + $" DM_UseItemRevision returned true: placement started in {target}.";
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
