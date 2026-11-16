using System.Text;
using DXP;

namespace AdSamples.LicenseManagement
{
    /// <summary>
    /// The read commands: what the three licensing managers (DXP.ISecurityManager, DXP.ILicenseManager,
    /// DXP.ISecurityServerManager) report about the running Altium Designer. Results are returned as text.
    /// </summary>
    internal static class LicenseManagementCommands
    {
        /// <summary>
        /// GetLicenseInfo ["Refresh=false"]: ISecurityManager.RefreshLicenseInformation, then the security
        /// manager's license fields. On PLS and AIS the refresh may clear used licenses without releasing them on
        /// the server, so Refresh=false skips it.
        /// </summary>
        [HotCommand("GetLicenseInfo", "Refresh and show the security manager's license information")]
        public static string GetLicenseInfo(CommandContext ctx)
        {
            ISecurityManager securityManager = Licensing.SecurityManager();
            var text = new StringBuilder();

            // Refresh whenever licenses were used or released, so the getters below are up to date.
            bool refresh = !string.Equals(ctx.Get("Refresh", "true"), "false", System.StringComparison.OrdinalIgnoreCase);
            if (refresh)
                securityManager.RefreshLicenseInformation();
            text.AppendLine("RefreshLicenseInformation: " + (refresh ? "called" : "skipped (Refresh=false)"));

            text.AppendLine("AnyValidLicenses: " + securityManager.AnyValidLicenses());
            text.AppendLine("IsSubscriptionCurrent: " + securityManager.IsSubscriptionCurrent());
            text.AppendLine("AbsoluteSubscriptionExpiryDate: " + Licensing.FormatDate(securityManager.AbsoluteSubscriptionExpiryDate()));
            text.AppendLine("LicensedState: " + securityManager.GetLicensedState());
            text.AppendLine("AbsoluteSubscriptionActivationCode: " + securityManager.AbsoluteSubscriptionActivationCode());
            text.Append("LicenseUsage: " + securityManager.GetLicenseUsage());

            // Using a license is not "getting info": it is the UseLicenseBySerial command.
            return text.ToString();
        }

        /// <summary>
        /// CheckADLicense ["SecurityFolder=&lt;folder&gt;"]: read-only. The license manager's current licenses and
        /// subscription, and the *.alf files in the security folder. Releasing and using licenses are the
        /// ReleaseLicenses and UseLicenseFile / UseLicenseByName commands.
        /// </summary>
        [HotCommand("CheckADLicense", "Show the license manager's state and the .alf files")]
        public static string CheckADLicense(CommandContext ctx)
        {
            var text = new StringBuilder();
            Licensing.AppendLicenseManagerState(text, Licensing.LicenseManager());

            Licensing.SecurityFolder? folder = Licensing.ResolveSecurityFolder(ctx, out string problem);
            if (folder is { } f)
            {
                text.AppendLine($"Security folder ({f.Source}): {f.Path}");
                Licensing.AppendFiles(text, Licensing.LicenseFiles(f.Path));
            }
            else
            {
                text.AppendLine("Security folder: not found (" + problem + ")");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// SecurityServerInfo: read-only. The two getters of ISecurityServerManager. Its changing
        /// methods are in ReleaseLicenses (ClearLicenses, FreeAll, AisLogout), UseOnDemandLicenses and
        /// AddLicenseFile.
        /// </summary>
        [HotCommand("SecurityServerInfo", "Show the security server manager's state")]
        public static string SecurityServerInfo(CommandContext ctx)
        {
            ISecurityServerManager serverManager = Licensing.SecurityServerManager();
            // In .NET IsFreeLicenseMode is the method GetIsFreeLicenseMode. Its meaning is undocumented.
            return "IsFreeLicenseMode: " + serverManager.GetIsFreeLicenseMode() + "\r\n"
                 + "HasActiveSubscription: " + serverManager.HasActiveSubscription();
        }
    }
}
