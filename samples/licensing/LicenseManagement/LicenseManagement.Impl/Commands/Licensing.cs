using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using Microsoft.Win32;

namespace AdSamples.LicenseManagement
{
    /// <summary>
    /// Helpers shared by the commands: the three licensing managers, the Confirm guard, the read-back of the
    /// license state, and the security folder that holds the leased *.alf files.
    /// </summary>
    internal static class Licensing
    {
        // In .NET GetLicenseManager, GetSecurityManager and GetSecurityServerManager are extension methods on
        // DXP.IClient (npm run api -- "DXP.IClient:Licen|Secur").
        public static IClient Client() =>
            DXP.GlobalVars.Client ?? throw new UserException("DXP.GlobalVars.Client is not available.");

        public static ILicenseManager LicenseManager() =>
            Client().GetLicenseManager() ?? throw new UserException("IClient.GetLicenseManager returned no license manager.");

        public static ISecurityManager SecurityManager() =>
            Client().GetSecurityManager() ?? throw new UserException("IClient.GetSecurityManager returned no security manager.");

        public static ISecurityServerManager SecurityServerManager() =>
            Client().GetSecurityServerManager() ?? throw new UserException("IClient.GetSecurityServerManager returned no security server manager.");

        /// <summary>
        /// Every command that uses, releases or clears licenses changes the license state of this Altium Designer
        /// (and possibly seats on the license server). Without Confirm=true it only says what it would do.
        /// </summary>
        public static bool Confirmed(CommandContext ctx, StringBuilder text, string wouldDo)
        {
            if (ctx.Flag("Confirm"))
                return true;
            text.AppendLine("Dry run, nothing changed.");
            text.AppendLine("Would " + wouldDo);
            text.Append("Run with Confirm=true to do it. This changes the license state of the running Altium Designer.");
            return false;
        }

        /// <summary>ILicenseManager state, as CheckADLicense shows it. The two strings are shown as received.</summary>
        public static void AppendLicenseManagerState(StringBuilder text, ILicenseManager licenseManager)
        {
            licenseManager.GetCurrentLicenseInfo(out string ids, out string names);
            text.AppendLine($"LicenseIds: '{ids}'");
            text.AppendLine($"LicenseNames: '{names}'");
            text.AppendLine("SubscriptionExpiryDate: " + FormatDate(licenseManager.SubscriptionExpiryDate()));
            text.AppendLine("IsSubscriptionCurrent: " + licenseManager.IsSubscriptionCurrent());
        }

        /// <summary>The license names of GetCurrentLicenseInfo, split as a comma list.</summary>
        public static List<string> CurrentLicenseNames(ILicenseManager licenseManager)
        {
            licenseManager.GetCurrentLicenseInfo(out _, out string names);
            return (names ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(n => n.Trim('"')).Where(n => n.Length > 0).ToList();
        }

        /// <summary>
        /// The .NET API types the expiry dates as double: OLE Automation dates (days since 1899-12-30), read with
        /// DateTime.FromOADate. The raw number is shown too.
        /// </summary>
        public static string FormatDate(double value)
        {
            string raw = value.ToString("R", CultureInfo.InvariantCulture);
            if (value == 0)
                return $"(none) [raw {raw}]";
            try
            {
                return $"{DateTime.FromOADate(value):yyyy-MM-dd} [raw {raw}]";
            }
            catch (ArgumentException)
            {
                return $"<not an OLE date> [raw {raw}]";
            }
        }

        public static void AppendFiles(StringBuilder text, IReadOnlyCollection<FileInfo> files)
        {
            text.AppendLine($"{files.Count} .alf file(s)" + (files.Count > 0 ? ":" : "."));
            foreach (FileInfo file in files)
                text.AppendLine($"  {file.Name} ({file.Length} bytes, {file.LastWriteTime:yyyy-MM-dd HH:mm})");
        }

        public static List<FileInfo> LicenseFiles(string folder) =>
            new DirectoryInfo(folder).GetFiles("*.alf").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

        public readonly record struct SecurityFolder(string Path, string Source);

        /// <summary>
        /// The security folder has no .NET helper. This takes SecurityFolder=&lt;folder&gt;, or derives the folder of the
        /// running installation: the key under HKLM\SOFTWARE\Altium\Builds whose ProgramsInstallPath is the folder of
        /// the running executable names the data folder (value Application, under %ProgramData%). The
        /// "&lt;data folder&gt;_Security" name ("Altium Designer {GUID}_Security") is not documented.
        /// Returns null (with the reason) when the folder cannot be found, so read commands can still report the rest.
        /// </summary>
        public static SecurityFolder? ResolveSecurityFolder(CommandContext ctx, out string problem)
        {
            problem = null;
            string given = ctx.Get("SecurityFolder");
            if (!string.IsNullOrEmpty(given))
            {
                if (!Directory.Exists(given))
                    throw new UserException($"SecurityFolder '{given}' does not exist.");
                return new SecurityFolder(Path.GetFullPath(given), "parameter");
            }

            string programFolder = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? "");
            string application = FindApplicationFolder(programFolder);
            if (application == null)
            {
                problem = $"no installation under HKLM\\SOFTWARE\\Altium\\Builds has ProgramsInstallPath '{programFolder}'; pass SecurityFolder=<folder>";
                return null;
            }

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), application + "_Security");
            if (!Directory.Exists(folder))
            {
                problem = $"the derived folder '{folder}' does not exist; pass SecurityFolder=<folder>";
                return null;
            }
            return new SecurityFolder(folder, "derived from HKLM\\SOFTWARE\\Altium\\Builds");
        }

        private static string FindApplicationFolder(string programFolder)
        {
            if (string.IsNullOrEmpty(programFolder))
                return null;
            using RegistryKey builds = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Altium\Builds");
            if (builds == null)
                return null;
            foreach (string name in builds.GetSubKeyNames())
            {
                using RegistryKey build = builds.OpenSubKey(name);
                if (build?.GetValue("ProgramsInstallPath") is string installPath
                    && SamePath(installPath, programFolder)
                    && build.GetValue("Application") is string application
                    && application.Length > 0)
                    return application;
            }
            return null;
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Runs one SDK call, records its outcome, and goes on: what these calls raise on failure is undocumented.</summary>
        public static void Try(StringBuilder text, string what, Action call)
        {
            try
            {
                call();
                text.AppendLine("Called " + what);
            }
            catch (Exception ex) when (ex is not UserException)
            {
                text.AppendLine($"{what} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
