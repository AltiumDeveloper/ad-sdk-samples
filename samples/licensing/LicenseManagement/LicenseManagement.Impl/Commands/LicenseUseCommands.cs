using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP;

namespace AdSamples.LicenseManagement
{
    /// <summary>
    /// The commands that change the license state: use a license, then give it back. Each is a dry run unless
    /// Confirm=true, and each ends with the license manager's state read back, so the effect is visible in the
    /// returned text. The per-server behaviour described in the comments is not documented in the SDK reference.
    /// </summary>
    internal static class LicenseUseCommands
    {
        // Process that releases the licenses used from an AIS. Not in the .NET reference.
        private const string AisLogoutProcess = "EDMSInterface:LogoutFromDXPServer";

        /// <summary>
        /// ReleaseLicenses "Confirm=true" ["Names=true"] ["AlfFiles=true"] ["Files=&lt;a.alf;b.alf&gt;"]
        /// ["SecurityFolder=&lt;folder&gt;"] ["ClearLicenses=true"] ["FreeAll=true"] ["AisLogout=true"]:
        /// ILicenseManager.ReturnLeasedLicensesOnLogout, plus optional release calls.
        /// </summary>
        [HotCommand("ReleaseLicenses", "Release leased licenses (dry run unless Confirm is set)")]
        public static string ReleaseLicenses(CommandContext ctx)
        {
            ILicenseManager licenseManager = Licensing.LicenseManager();

            // What to release by ReleaseLicense: license names, and/or .alf file paths.
            var targets = new List<string>();
            if (ctx.Flag("Names"))
                targets.AddRange(Licensing.CurrentLicenseNames(licenseManager));
            if (ctx.Flag("AlfFiles"))
            {
                Licensing.SecurityFolder folder = Licensing.ResolveSecurityFolder(ctx, out string problem)
                    ?? throw new UserException("AlfFiles=true: security folder not found (" + problem + ").");
                targets.AddRange(Licensing.LicenseFiles(folder.Path).Select(f => f.FullName));
            }
            foreach (string file in (ctx.Get("Files") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!File.Exists(file))
                    throw new UserException($"Files: '{file}' does not exist.");
                targets.Add(Path.GetFullPath(file));
            }

            var plan = new List<string> { "ILicenseManager.ReturnLeasedLicensesOnLogout" };
            plan.AddRange(targets.Select(t => $"ILicenseManager.ReleaseLicense('{t}')"));
            if (ctx.Flag("ClearLicenses")) plan.Add("ISecurityServerManager.ClearLicenses");
            if (ctx.Flag("FreeAll")) plan.Add("ISecurityServerManager.FreeAllLicenses");
            if (ctx.Flag("AisLogout")) plan.Add($"RunCommand {AisLogoutProcess}");

            var text = new StringBuilder();
            if (!Licensing.Confirmed(ctx, text, "call:\r\n  " + string.Join("\r\n  ", plan)))
                return text.ToString();

            // On a License Server this returns the used licenses on the server side and deletes the
            // .alf files in the security folder; on PLS and AIS it does nothing.
            Licensing.Try(text, "ILicenseManager.ReturnLeasedLicensesOnLogout", licenseManager.ReturnLeasedLicensesOnLogout);

            // Not working for license names; for .alf files the license is unused locally but
            // not released on the server, and the file is kept. Reported, not presented as a working technique.
            foreach (string target in targets)
            {
                ctx.Log("ReleaseLicense " + target);
                Licensing.Try(text, $"ILicenseManager.ReleaseLicense('{target}')", () => licenseManager.ReleaseLicense(target));
            }

            if (ctx.Flag("ClearLicenses"))
            {
                // License Server and AIS: used .alf files are unused and deleted, not released on the server,
                // "My Account" is not refreshed. PLS: nothing.
                Licensing.Try(text, "ISecurityServerManager.ClearLicenses", Licensing.SecurityServerManager().ClearLicenses);
            }
            if (ctx.Flag("FreeAll"))
            {
                // As ClearLicenses, but "My Account" is refreshed; on PLS it releases the used PLS license.
                Licensing.Try(text, "ISecurityServerManager.FreeAllLicenses", Licensing.SecurityServerManager().FreeAllLicenses);
            }
            if (ctx.Flag("AisLogout"))
                Licensing.Try(text, "RunCommand " + AisLogoutProcess, () => DXP.Utils.RunCommand(AisLogoutProcess, ""));

            return ReadBack(text, licenseManager);
        }

        /// <summary>
        /// UseLicenseFile "File=&lt;.alf&gt;|Server=AIS|LicenseServer|Confirm=true" ["Keep=true"]:
        /// ILicenseManager.UseLicense loads the license from the file and uses it by its serial number (one more
        /// used seat on the server). Then it is released in the way that fits that server type, unless Keep=true.
        /// </summary>
        [HotCommand("UseLicenseFile", "Use a license from an .alf file, then release it (needs File, Server, Confirm)")]
        public static string UseLicenseFile(CommandContext ctx)
        {
            string file = ctx.Get("File");
            if (string.IsNullOrEmpty(file))
                throw new UserException("No license file specified. Pass File=<path to .alf>.");
            if (!File.Exists(file))
                throw new UserException($"License file '{file}' does not exist.");
            file = Path.GetFullPath(file);

            string server = ctx.Get("Server", "");
            bool ais = server.Equals("AIS", StringComparison.OrdinalIgnoreCase);
            if (!ais && !server.Equals("LicenseServer", StringComparison.OrdinalIgnoreCase))
                throw new UserException("Pass Server=AIS or Server=LicenseServer: it selects how the license is released.");
            bool keep = ctx.Flag("Keep");
            string release = ais ? "RunCommand " + AisLogoutProcess : "ILicenseManager.ReturnLeasedLicensesOnLogout";

            ILicenseManager licenseManager = Licensing.LicenseManager();
            var text = new StringBuilder();
            if (!Licensing.Confirmed(ctx, text, $"call ILicenseManager.UseLicense('{file}')" + (keep ? " and keep it." : ", then " + release + ".")))
                return text.ToString();

            licenseManager.UseLicense(file);
            text.AppendLine($"Called ILicenseManager.UseLicense('{file}')");
            try
            {
                // The license is in use between the two calls: do the licensed work here.
                text.AppendLine("While in use:");
                Licensing.AppendLicenseManagerState(text, licenseManager);
            }
            finally
            {
                if (!keep)
                {
                    if (ais)
                        Licensing.Try(text, release, () => DXP.Utils.RunCommand(AisLogoutProcess, ""));
                    else
                        Licensing.Try(text, release, licenseManager.ReturnLeasedLicensesOnLogout);
                }
            }
            if (keep)
                text.AppendLine("Kept (Keep=true). Release with ReleaseLicenses.");
            return ReadBack(text, licenseManager);
        }

        /// <summary>
        /// UseLicenseByName "Name=&lt;license name&gt;|Confirm=true": ILicenseManager.UseLicenseByName. It gets
        /// the serial number from the name, which only works once the license file is loaded, and it is of no use
        /// on PLS. Not released afterwards.
        /// </summary>
        [HotCommand("UseLicenseByName", "Use a license by name (needs Name, Confirm)")]
        public static string UseLicenseByName(CommandContext ctx)
        {
            string name = ctx.Get("Name");
            if (string.IsNullOrEmpty(name))
                throw new UserException("No license name specified. Pass Name=<license name> (for example 'Altium Designer').");

            ILicenseManager licenseManager = Licensing.LicenseManager();
            var text = new StringBuilder();
            if (!Licensing.Confirmed(ctx, text, $"call ILicenseManager.UseLicenseByName('{name}')."))
                return text.ToString();

            Licensing.Try(text, $"ILicenseManager.UseLicenseByName('{name}')", () => licenseManager.UseLicenseByName(name));
            return ReadBack(text, licenseManager);
        }

        /// <summary>
        /// UseLicenseBySerial "Serial=&lt;PLS serial number&gt;|Confirm=true" ["Keep=true"]:
        /// ISecurityManager.UseLicense, which works for PLS licenses only (the serial number is
        /// shown in the PLS UI), then ISecurityServerManager.FreeAllLicenses, which releases it on the PLS.
        /// </summary>
        [HotCommand("UseLicenseBySerial", "Use a PLS license by serial, then free it (needs Serial, Confirm)")]
        public static string UseLicenseBySerial(CommandContext ctx)
        {
            if (!long.TryParse(ctx.Get("Serial"), out long serial))
                throw new UserException("Pass Serial=<PLS license serial number> (an integer).");
            bool keep = ctx.Flag("Keep");

            ILicenseManager licenseManager = Licensing.LicenseManager();
            var text = new StringBuilder();
            if (!Licensing.Confirmed(ctx, text, $"call ISecurityManager.UseLicense({serial})" + (keep ? " and keep it." : ", then ISecurityServerManager.FreeAllLicenses.")))
                return text.ToString();

            Licensing.SecurityManager().UseLicense(serial);
            text.AppendLine($"Called ISecurityManager.UseLicense({serial})");
            try
            {
                text.AppendLine("While in use:");
                Licensing.AppendLicenseManagerState(text, licenseManager);
            }
            finally
            {
                if (!keep)
                    Licensing.Try(text, "ISecurityServerManager.FreeAllLicenses", Licensing.SecurityServerManager().FreeAllLicenses);
            }
            if (keep)
                text.AppendLine("Kept (Keep=true). Release with ReleaseLicenses FreeAll=true.");
            return ReadBack(text, licenseManager);
        }

        /// <summary>
        /// UseOnDemandLicenses "Confirm=true" ["Force=true"] ["Keep=true"]:
        /// ISecurityServerManager.UseAvailableOnDemandLicenses leases every available on-demand license of the
        /// License Server (one seat each). Force=true also takes seats from other users when none are free (default
        /// False). Then ReturnLeasedLicensesOnLogout, unless Keep=true.
        /// </summary>
        [HotCommand("UseOnDemandLicenses", "Lease the on-demand licenses, then return them (needs Confirm)")]
        public static string UseOnDemandLicenses(CommandContext ctx)
        {
            bool force = ctx.Flag("Force");
            bool keep = ctx.Flag("Keep");

            ILicenseManager licenseManager = Licensing.LicenseManager();
            var text = new StringBuilder();
            if (!Licensing.Confirmed(ctx, text, $"call ISecurityServerManager.UseAvailableOnDemandLicenses({force})"
                    + (keep ? " and keep the licenses." : ", then ILicenseManager.ReturnLeasedLicensesOnLogout.")))
                return text.ToString();

            Licensing.SecurityServerManager().UseAvailableOnDemandLicenses(force);
            text.AppendLine($"Called ISecurityServerManager.UseAvailableOnDemandLicenses({force})");

            // The leasing runs on a separate thread, so it may still be running here, and returning
            // the licenses right away races with it. This command does not wait on AD's UI thread; to see the leased
            // licenses, run with Keep=true, check with CheckADLicense, and release with ReleaseLicenses.
            if (keep)
            {
                text.AppendLine("Kept (Keep=true). The leasing may still be running; check with CheckADLicense, release with ReleaseLicenses.");
            }
            else
            {
                text.AppendLine("Returning right away: the leasing thread may not have finished yet.");
                Licensing.Try(text, "ILicenseManager.ReturnLeasedLicensesOnLogout", licenseManager.ReturnLeasedLicensesOnLogout);
            }
            return ReadBack(text, licenseManager);
        }

        /// <summary>
        /// AddLicenseFile "Confirm=true" ["Replace=true"]: ISecurityServerManager.AddLicenseFile opens a file
        /// dialog and loads the chosen standalone .alf (another kind shows an error and does
        /// nothing). Replace=true first removes all used licenses, without releasing them on the server.
        /// Interactive only, because of the dialog.
        /// </summary>
        [HotCommand("AddLicenseFile", "Pick and load a standalone .alf file (dialog; needs Confirm)")]
        public static string AddLicenseFile(CommandContext ctx)
        {
            bool replace = ctx.Flag("Replace");
            if (!ctx.IsInteractive)
                throw new UserException("AddLicenseFile opens a file dialog; run it from the menu, not from the pipe.");

            var text = new StringBuilder();
            if (!Licensing.Confirmed(ctx, text, $"call ISecurityServerManager.AddLicenseFile({replace}) (opens a file dialog)."))
                return text.ToString();

            IServerLicense license = Licensing.SecurityServerManager().AddLicenseFile(replace);
            text.AppendLine($"Called ISecurityServerManager.AddLicenseFile({replace})");
            text.AppendLine(license == null
                ? "No license returned (dialog cancelled or the file was not loaded)."
                : "Loaded license with serial number " + license.GetSerialNumber());
            return ReadBack(text, Licensing.LicenseManager());
        }

        private static string ReadBack(StringBuilder text, ILicenseManager licenseManager)
        {
            text.AppendLine("After:");
            Licensing.AppendLicenseManagerState(text, licenseManager);
            return text.ToString().TrimEnd();
        }
    }
}
