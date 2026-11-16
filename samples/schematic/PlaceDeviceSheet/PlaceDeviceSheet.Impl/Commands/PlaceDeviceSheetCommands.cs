using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using DXP;
using SCH;

namespace AdSamples.PlaceDeviceSheet
{
    /// <summary>
    /// Two ways to put a reusable sheet on a schematic:
    /// PlaceDeviceSheet resolves a device sheet by name through SCH.IDeviceSheetManager and runs the built-in
    /// Sch:CreateSheetSymbolFromSheet process with its interactive steps switched off; PlaceSheetFromVault looks up a
    /// sheet item revision in a Vault by HRID and hands it to IDXPWorkSpace.DM_UseItemRevision.
    /// </summary>
    internal static class PlaceDeviceSheetCommands
    {
        /// <summary>
        /// PlaceDeviceSheet "Name=&lt;device sheet&gt;|X=&lt;mils&gt;|Y=&lt;mils&gt;|DryRun": places the named device sheet as a
        /// sheet symbol on the active schematic and reports the sheet symbols that appeared.
        /// </summary>
        [HotCommand("PlaceDeviceSheet", "Place a device sheet as a sheet symbol")]
        public static string PlaceDeviceSheet(CommandContext ctx)
        {
            string name = ctx.Get("Name");
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("Pass Name=<device sheet name>, for example Name=TestDeviceSheet.");
            name = name.Trim();
            double xMils = MilsParam(ctx, "X", 5000);
            double yMils = MilsParam(ctx, "Y", 5000);
            bool dryRun = ctx.Flag("DryRun");

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            // The .NET API exposes the device sheet manager directly; there is no need to load IntegratedLibrary.dll
            // and call its exported LibApi_GetDeviceSheetManager through LoadLibrary/GetProcAddress.
            IDeviceSheetManager manager = EDP.Utils.GetDeviceSheetManager()
                ?? throw new UserException("The device sheet manager is not available (GetDeviceSheetManager returned null).");

            // The reference says an invalid name returns a blank string; a test for null alone would let a missing
            // sheet slip through. Both are treated as "not found".
            string sheetPath = manager.FindDeviceSheetPath(name);
            if (string.IsNullOrEmpty(sheetPath))
                throw new UserException($"Device sheet '{name}' was not found in the device sheet folders " +
                                        $"({manager.GetFoldersCount()} folder(s) registered).");

            string found = $"Device sheet '{name}' resolved to '{sheetPath}'.";
            if (dryRun)
                return found + " DryRun: not placed.";

            // The process reports nothing back, so the sheet symbols are listed before and after to see what it did.
            List<string> before = SheetSymbols(sheet);

            // Sch:CreateSheetSymbolFromSheet is the process behind placing a device sheet from the UI. Its parameters
            // are not documented in the SDK reference. InteractiveCreate=False and InteractivePlace=False skip the
            // dialog and the cursor placement. LocationX/LocationY are internal coordinates (EDP.Utils.MilsToCoord).
            int x = EDP.Utils.MilsToCoord(xMils);
            int y = EDP.Utils.MilsToCoord(yMils);
            string parameters =
                "SymbolType=Device Sheet" +
                "|InteractiveCreate=False" +
                "|InteractivePlace=False" +
                "|LocationX=" + x.ToString(CultureInfo.InvariantCulture) +
                "|LocationY=" + y.ToString(CultureInfo.InvariantCulture) +
                "|SourceSheetID=" + name;
            ctx.Log($"Running Sch:CreateSheetSymbolFromSheet with {parameters}");
            DXP.Utils.MessageRouterSendCommandToModule("Sch:CreateSheetSymbolFromSheet", parameters,
                                                       DXP.GlobalVars.Client.GetCurrentView());

            // Re-query the document: the process may have changed which one is current.
            ISch_Document after = schServer.GetCurrentSchDocument() ?? sheet;
            List<string> added = Added(before, SheetSymbols(after));
            string placed = $"{found} Sch:CreateSheetSymbolFromSheet at ({Mils(xMils)}, {Mils(yMils)}) mils on {file}: ";
            if (added.Count == 0)
                return placed + $"no new sheet symbol ({before.Count} before and after). The process may still be " +
                       "waiting for input, or it rejected the parameters.";
            return placed + $"{added.Count} new sheet symbol(s): " + string.Join("; ", added) + ".";
        }

        /// <summary>
        /// PlaceSheetFromVault "Hrid=&lt;item revision HRID&gt;|Vault=&lt;index&gt;|DryRun": looks up a sheet item revision in an
        /// installed Vault and starts placing it on the active schematic with DM_UseItemRevision.
        /// </summary>
        [HotCommand("PlaceSheetFromVault", "Place a Vault sheet by item revision HRID")]
        public static string PlaceSheetFromVault(CommandContext ctx)
        {
            string hrid = ctx.Get("Hrid");
            if (string.IsNullOrWhiteSpace(hrid))
                throw new UserException("Pass Hrid=<item revision HRID>, for example Hrid=SCH-0001-00021-1.");
            hrid = hrid.Trim();
            int vaultIndex = IntParam(ctx, "Vault", 0);
            bool dryRun = ctx.Flag("DryRun");

            // The .NET API exposes the vault manager directly; there is no need to load EDMSInterface.dll and call
            // API_GetEDMS_VaultManager.
            IEDMS_VaultManager vaultManager = EDP.Utils.GetVaultManager()
                ?? throw new UserException("The Vault manager is not available in this Altium Designer session.");
            int vaultCount = vaultManager.GetInstalledVaultCount();
            if (vaultCount == 0)
                throw new UserException("No Vault is installed. Sign in to a workspace first.");
            if (vaultIndex < 0 || vaultIndex >= vaultCount)
                throw new UserException($"Vault={vaultIndex} is out of range: {vaultCount} Vault(s) installed (0..{vaultCount - 1}).");
            IEDMS_Vault vault = vaultManager.GetInstalledVault(vaultIndex)
                ?? throw new UserException($"Installed Vault {vaultIndex} could not be read.");
            string vaultName = $"'{vault.GetHRID()}'";

            // The sheet goes into the active schematic, so check for one.
            IServerDocumentView view = DXP.GlobalVars.Client.GetCurrentView();
            IServerDocument document = view?.GetOwnerDocument();
            if (!dryRun && (document == null || !string.Equals(document.GetKind(), "SCH", StringComparison.OrdinalIgnoreCase)))
                throw new UserException("Open and focus a .SchDoc to place the sheet into.");

            ctx.Log($"Looking up '{hrid}' in Vault {vaultIndex} {vaultName}...");
            // Whether an unknown HRID gives null or a COM error is not documented: handle both.
            IULB_ItemRevision revision;
            try
            {
                revision = vault.ItemRevisionByHRID(hrid);
            }
            catch (COMException ex)
            {
                throw new UserException($"Looking up '{hrid}' in Vault {vaultName} failed: {ex.Message}");
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

            // The parameter string and the result are not documented; the parameters are passed empty and the
            // result is reported.
            bool started = DXP.GlobalVars.Client.GetDXPWorkspace().DM_UseItemRevision(vaultGuid, itemGuid, revisionGuid, string.Empty);
            string target = $"'{Path.GetFileName(document.GetFileName())}'";
            if (!started)
                throw new UserException(found + $" DM_UseItemRevision returned false for {target}.");
            return found + $" DM_UseItemRevision returned true: placement started in {target}.";
        }

        /// <summary>One description per sheet symbol on the sheet: type, file name and location in mils.</summary>
        private static List<string> SheetSymbols(ISch_Document sheet)
        {
            var result = new List<string>();
            ISch_Iterator iterator = sheet.SchIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSheetSymbol });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                {
                    // The filter guarantees sheet symbols, so the cast is safe.
                    var symbol = (ISch_SheetSymbol)obj;
                    Point location = symbol.GetState_Location();
                    result.Add($"{symbol.GetState_SymbolType()} '{symbol.GetState_SchSheetFileName()?.GetState_Text()}' " +
                               $"at ({Mils(EDP.Utils.CoordToMils(location.X))}, {Mils(EDP.Utils.CoordToMils(location.Y))})");
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return result;
        }

        /// <summary>The entries of <paramref name="after"/> that are not in <paramref name="before"/> (multiset difference).</summary>
        private static List<string> Added(List<string> before, List<string> after)
        {
            var remaining = new List<string>(before);
            var added = new List<string>();
            foreach (string entry in after)
            {
                if (!remaining.Remove(entry))
                    added.Add(entry);
            }
            return added;
        }

        private static string Mils(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static double MilsParam(CommandContext ctx, string name, double defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrEmpty(text))
                return defaultValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                throw new UserException($"{name}='{text}' is not a non-negative number of mils.");
            return value;
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
