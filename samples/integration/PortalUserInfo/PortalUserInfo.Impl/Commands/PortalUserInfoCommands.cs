using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DXP;

namespace AdSamples.PortalUserInfo
{
    /// <summary>
    /// Reads who is signed in to each Vault (workspace) installed in Altium Designer: the vault manager lists the
    /// installed vaults, and IEDMS_Vault.GetUserDetails returns the user (IEDM_User) of each one. With Server=true it
    /// also reads the server manager's user (IEDMS_DXPServerManager.GetUserInfo).
    /// </summary>
    internal static class PortalUserInfoCommands
    {
        // The vault type ID that is skipped (legacy Altium Vault entries).
        private const string LegacyVaultTypeId = "AltiumVault";

        [HotCommand("GetUserInfo", "Show the signed-in user of each installed Vault")]
        public static string GetUserInfo(CommandContext ctx)
        {
            int? only = OptionalIndex(ctx, "Vault");
            bool server = ctx.Flag("Server");

            IEDMS_VaultManager vaultManager = EDP.Utils.GetVaultManager()
                ?? throw new UserException("The Vault manager is not available in this Altium Designer session.");

            int count = vaultManager.GetInstalledVaultCount();
            if (count == 0 && !server)
                throw new UserException("No Vault is installed. Sign in to a workspace first.");
            if (only is int index && (index < 0 || index >= count))
                throw new UserException($"Vault={index} is out of range: {count} Vault(s) installed (0..{count - 1}).");

            var text = new StringBuilder();
            int withUser = 0, skipped = 0;
            int first = only ?? 0, last = only ?? count - 1;
            for (int i = first; i <= last; i++)
            {
                string line = DescribeVault(vaultManager, i, out bool hasUser);
                if (hasUser) withUser++; else skipped++;
                text.AppendLine(line);
            }

            string header = only is int one
                ? $"Vault {one} of {count}:"
                : $"{count} installed Vault(s): {withUser} with a signed-in user, {skipped} skipped.";

            if (server)
                text.Append(DescribeServer());

            return (header + "\n" + text.ToString()).TrimEnd();
        }

        // One line per installed vault. Each vault is read on its own: one that fails must not hide the others.
        private static string DescribeVault(IEDMS_VaultManager vaultManager, int i, out bool hasUser)
        {
            hasUser = false;
            try
            {
                IEDMS_Vault vault = vaultManager.GetInstalledVault(i);
                if (vault == null)
                    return $"[{i}] skipped: GetInstalledVault returned null";

                string typeId = vault.GetVaultTypeID();
                string name = $"'{vault.GetHRID()}' ({vault.GetServiceURL()})";
                if (typeId == LegacyVaultTypeId)
                    return $"[{i}] {name} skipped: vault type '{typeId}'";

                // GetUserDetails returns IEDM_User.
                IEDM_User user = vault.GetUserDetails();
                if (user == null)
                    return $"[{i}] {name} [{typeId}] skipped: no user details (not signed in?)";

                hasUser = true;
                return $"[{i}] {name} [{typeId}] user '{user.GetFullName()}' GUID {user.GetGUID()}";
            }
            catch (COMException ex)
            {
                // A COM error is reported per vault instead of failing the whole command.
                return $"[{i}] error: {ex.Message} (0x{ex.HResult:X8})";
            }
        }

        // The server manager path. Never read or print GetPassword.
        private static string DescribeServer()
        {
            var text = new StringBuilder();
            text.AppendLine("Server manager:");

            IEDMS_DXPServerManager serverManager = EDP.Utils.GetDXPServerManager();
            if (serverManager == null)
                return text.AppendLine("  not available in this Altium Designer session.").ToString();
            if (!serverManager.IsConnected())
                return text.AppendLine("  not connected (not signed in).").ToString();

            try
            {
                IEDMS_DXPServerUserInfo info = serverManager.GetUserInfo();
                if (info == null)
                    text.AppendLine("  GetUserInfo returned null.");
                else
                    text.AppendLine($"  user '{info.GetFullName()}' user name '{info.GetUserName()}' domain '{info.GetDomain()}' GUID {info.GetGUID()}");

                IEDMS_Vault vault = serverManager.GetEnterpriseVault();
                if (vault == null)
                {
                    text.AppendLine("  GetEnterpriseVault returned null.");
                }
                else
                {
                    IEDM_User user = vault.GetUserDetails();
                    text.AppendLine($"  enterprise vault '{vault.GetHRID()}' ({vault.GetServiceURL()}): user GUID {vault.GetUserGUID()}, " +
                                    (user == null ? "no user details" : $"user details '{user.GetFullName()}' GUID {user.GetGUID()}"));
                }
            }
            catch (COMException ex)
            {
                text.AppendLine($"  error: {ex.Message} (0x{ex.HResult:X8})");
            }
            return text.ToString();
        }

        private static int? OptionalIndex(CommandContext ctx, string name)
        {
            string value = ctx.Get(name);
            if (string.IsNullOrEmpty(value))
                return null;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                throw new UserException($"{name}='{value}' is not a whole number.");
            return index;
        }
    }
}
