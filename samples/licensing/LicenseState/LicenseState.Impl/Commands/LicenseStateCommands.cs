using System;
using System.Globalization;
using DXP;

namespace AdSamples.LicenseState
{
    /// <summary>
    /// Reads the license and subscription state from the client's security manager. Read-only: the sample never
    /// calls ISecurityManager.RefreshLicenseInformation or UseLicense, which change the licensing state.
    /// </summary>
    internal static class LicenseStateCommands
    {
        /// <summary>
        /// GetLicenseState ["Raw=true"]: the licensed-state text and, when any valid license exists, the
        /// subscription status. Raw=true also returns the unconverted values (an addition, for checking the date).
        /// </summary>
        [HotCommand("GetLicenseState", "Show the license and subscription state")]
        public static string GetLicenseState(CommandContext ctx)
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");

            // In .NET the security manager comes from the extension method
            // GetSecurityManager on IClient.
            ISecurityManager securityManager = client.GetSecurityManager()
                ?? throw new UserException("IClient.GetSecurityManager returned no security manager.");

            string licenseState = "Available Licenses - " + securityManager.GetLicensedState() + ". ";
            bool anyValid = securityManager.AnyValidLicenses();
            bool current = false;
            double expiry = 0;

            if (anyValid)
            {
                expiry = securityManager.AbsoluteSubscriptionExpiryDate();
                current = securityManager.IsSubscriptionCurrent();

                if (current)
                    licenseState += "License subscription valid to " + FormatDate(expiry);
                else if (expiry == 0)
                    licenseState += "License subscription expired."; // 0 is taken as "no date"
                else
                    licenseState += "License subscription expired on " + FormatDate(expiry);
            }

            if (ctx.Flag("Raw"))
            {
                licenseState += "\r\nAnyValidLicenses=" + anyValid
                    + (anyValid
                        ? "|IsSubscriptionCurrent=" + current
                          + "|AbsoluteSubscriptionExpiryDate=" + expiry.ToString("R", CultureInfo.InvariantCulture)
                        : "");
            }

            return licenseState;
        }

        /// <summary>
        /// The .NET API types the expiry as double: an OLE Automation date (days since 1899-12-30), read with
        /// DateTime.FromOADate. The date is formatted 'd-MMM-yyyy'
        /// in the invariant culture.
        /// </summary>
        private static string FormatDate(double value)
        {
            try
            {
                return DateTime.FromOADate(value).ToString("d-MMM-yyyy", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException)
            {
                // Out of the OLE date range: show the number rather than fail the whole command.
                return "<unrecognised date value " + value.ToString("R", CultureInfo.InvariantCulture) + ">";
            }
        }
    }
}
