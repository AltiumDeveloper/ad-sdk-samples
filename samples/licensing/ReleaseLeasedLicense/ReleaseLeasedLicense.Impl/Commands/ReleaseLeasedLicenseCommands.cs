using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using Microsoft.Win32;

namespace AdSamples.ReleaseLeasedLicense
{
    /// <summary>
    /// Lists and releases the leased licence files (*.alf) of the running installation through
    /// DXP.ILicenseManager. ReleaseLicense gives up the user's licence and switches to standalone, so it is a dry run
    /// unless Confirm is set.
    /// </summary>
    internal static class ReleaseLeasedLicenseCommands
    {
        /// <summary>
        /// ListLicenseFiles ["SecurityFolder=&lt;folder&gt;"]: read-only. The current licence ids and names
        /// (GetCurrentLicenseInfo, returned as received) and the *.alf files in the security folder, so that
        /// ReleaseLicense can be previewed and its effect checked.
        /// </summary>
        [HotCommand("ListLicenseFiles", "List the current licences and the .alf files in the security folder")]
        public static string ListLicenseFiles(CommandContext ctx)
        {
            ILicenseManager licenseManager = LicenseManager();
            SecurityFolder folder = ResolveSecurityFolder(ctx);

            var text = new StringBuilder();
            AppendLicenseInfo(text, licenseManager);
            text.AppendLine($"Security folder ({folder.Source}): {folder.Path}");
            AppendFiles(text, LicenseFiles(folder.Path));
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// ReleaseLicense ["SecurityFolder=&lt;folder&gt;"] ["Confirm=true"]: without Confirm, reports what would be
        /// released. With Confirm, calls ReleaseLicense for every *.alf file, then ChangeToStandalone and
        /// ReturnLeasedLicensesOnLogout, and reports the state read back afterwards.
        /// </summary>
        [HotCommand("ReleaseLicense", "Release the leased licences (dry run unless Confirm is set)")]
        public static string ReleaseLicense(CommandContext ctx)
        {
            ILicenseManager licenseManager = LicenseManager();
            SecurityFolder folder = ResolveSecurityFolder(ctx);
            List<FileInfo> files = LicenseFiles(folder.Path);

            var text = new StringBuilder();
            if (!ctx.Flag("Confirm"))
            {
                // The menu item lands here: releasing a licence must be asked for explicitly.
                text.AppendLine("Dry run, nothing changed.");
                text.AppendLine($"Security folder ({folder.Source}): {folder.Path}");
                text.AppendLine($"Would release {files.Count} licence file(s), then call ChangeToStandalone and ReturnLeasedLicensesOnLogout:");
                foreach (FileInfo file in files)
                    text.AppendLine("  " + file.Name);
                text.Append("Run with Confirm=true to release them.");
                return text.ToString();
            }

            int released = 0;
            foreach (FileInfo file in files)
            {
                ctx.Log("Releasing " + file.Name);
                try
                {
                    // ReleaseLicense takes the full path of each .alf file.
                    licenseManager.ReleaseLicense(file.FullName);
                    released++;
                    text.AppendLine("Released " + file.Name);
                }
                catch (Exception ex) when (ex is not UserException)
                {
                    // What ReleaseLicense raises for a file it can't release is undocumented: report it and go on
                    // with the other files, rather than leave the installation half-way.
                    text.AppendLine($"Failed to release {file.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Both are called unconditionally, also when no .alf file was found.
            licenseManager.ChangeToStandalone();
            licenseManager.ReturnLeasedLicensesOnLogout();
            text.AppendLine($"Released {released} of {files.Count} file(s); called ChangeToStandalone and ReturnLeasedLicensesOnLogout.");

            // Read back, so the effect is visible in the result (does ReleaseLicense delete the file? is the licence gone?).
            text.AppendLine("After:");
            AppendLicenseInfo(text, licenseManager);
            AppendFiles(text, LicenseFiles(folder.Path));
            return text.ToString().TrimEnd();
        }

        private static ILicenseManager LicenseManager()
        {
            IClient client = DXP.GlobalVars.Client
                ?? throw new UserException("DXP.GlobalVars.Client is not available.");
            // In .NET GetLicenseManager is an extension method on IClient.
            return client.GetLicenseManager()
                ?? throw new UserException("IClient.GetLicenseManager returned no licence manager.");
        }

        private static void AppendLicenseInfo(StringBuilder text, ILicenseManager licenseManager)
        {
            // The format of the two strings is undocumented: they are shown as received.
            licenseManager.GetCurrentLicenseInfo(out string ids, out string names);
            text.AppendLine($"License ids: '{ids}'");
            text.AppendLine($"License names: '{names}'");
        }

        private static void AppendFiles(StringBuilder text, List<FileInfo> files)
        {
            text.AppendLine($"{files.Count} .alf file(s)" + (files.Count > 0 ? ":" : "."));
            foreach (FileInfo file in files)
                text.AppendLine($"  {file.Name} ({file.Length} bytes, {file.LastWriteTime:yyyy-MM-dd HH:mm})");
        }

        private static List<FileInfo> LicenseFiles(string folder) =>
            new DirectoryInfo(folder).GetFiles("*.alf").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

        private readonly record struct SecurityFolder(string Path, string Source);

        /// <summary>
        /// The _Security folder differs per installation. This takes the
        /// SecurityFolder parameter, or derives the folder of the running installation: the installation's key under
        /// HKLM\SOFTWARE\Altium\Builds whose ProgramsInstallPath is the folder of the running executable names the data
        /// folder (Application, under %ProgramData%; docs: getting-started, "Registering the extension"). The
        /// "&lt;data folder&gt;_Security" name is not documented.
        /// </summary>
        private static SecurityFolder ResolveSecurityFolder(CommandContext ctx)
        {
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
                throw new UserException(
                    $"No installation under HKLM\\SOFTWARE\\Altium\\Builds has ProgramsInstallPath '{programFolder}'. " +
                    "Pass the folder with the .alf files as SecurityFolder=<folder>.");

            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), application + "_Security");
            if (!Directory.Exists(folder))
                throw new UserException($"The derived security folder '{folder}' does not exist. Pass SecurityFolder=<folder>.");
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

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A malformed registry value belongs to some other installation: skip it instead of failing.
                return false;
            }
        }
    }
}
