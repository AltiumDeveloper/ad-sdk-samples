using System;
using System.Collections.Generic;
using System.Text;
using DXP;

namespace AdSamples.AutoReleaseLicense
{
    /// <summary>
    /// Returns the leased licences once the user has been idle in AD for a while, through
    /// DXP.ILicenseManager.ReturnLeasedLicensesOnLogout. The shim (IdleWatcher.cs) owns the input hooks and the timer,
    /// because native callbacks must not point into this hot-reloaded module; this side starts and stops it, formats
    /// its state and makes the SDK call. Returning licences is not undoable from here, so every command that can do
    /// it is a dry run unless DryRun=false.
    /// </summary>
    internal static class AutoReleaseLicenseCommands
    {
        // Same strings as CSharpPlugin.IdleWatcher in the shim (this module cannot reference the shim assembly).
        private const string Prefix = "AutoReleaseLicense.";
        private const string StartKey = Prefix + "Start";
        private const string StopKey = Prefix + "Stop";
        private const string StatusKey = Prefix + "Status";

        /// <summary>
        /// StartAutoRelease ["IdleSeconds=&lt;n&gt;"] ["DryRun=false"]: hook AD's keyboard and mouse input and return the
        /// leased licences once per idle period of IdleSeconds (default 30).
        /// </summary>
        [HotCommand("StartAutoRelease", "Start returning leased licences after user inactivity (dry run unless DryRun=false)")]
        public static string StartAutoRelease(CommandContext ctx)
        {
            int idleSeconds = IdleSeconds(ctx);
            bool dryRun = DryRun(ctx);
            var start = Switch<Func<int, bool, string>>(StartKey);

            string what;
            try { what = start(idleSeconds, dryRun); }
            catch (InvalidOperationException ex) { throw new UserException(ex.Message); }

            return $"Auto release {what}: idle threshold {idleSeconds} s, " +
                   (dryRun ? "dry run (nothing is returned; run with DryRun=false to return the licences)."
                           : "returns the leased licences with ReturnLeasedLicensesOnLogout.");
        }

        /// <summary>StopAutoRelease: remove the hooks and the timer.</summary>
        [HotCommand("StopAutoRelease", "Stop the inactivity watcher and remove the input hooks")]
        public static string StopAutoRelease(CommandContext ctx) =>
            "Auto release " + Switch<Func<string>>(StopKey)() + ".";

        /// <summary>AutoReleaseStatus: the watcher's state, as kept by the shim.</summary>
        [HotCommand("AutoReleaseStatus", "Show the inactivity watcher's state")]
        public static string AutoReleaseStatus(CommandContext ctx)
        {
            IReadOnlyDictionary<string, object> s = Switch<Func<IReadOnlyDictionary<string, object>>>(StatusKey)();
            var text = new StringBuilder();
            if (s["Running"] is true)
            {
                text.AppendLine($"Running since {s["StartedAt"]:HH:mm:ss}: idle threshold {s["IdleSeconds"]} s, " +
                                (s["DryRun"] is true ? "dry run." : "returns licences."));
                text.AppendLine($"Seconds since the last input in AD: {s["SecondsSinceInput"]:0.0}.");
            }
            else
            {
                text.AppendLine("Not running (run StartAutoRelease).");
            }
            text.AppendLine($"Input events seen: {s["InputEvents"]}.");
            text.AppendLine($"Idle triggers: {s["IdleTriggers"]}" +
                            (s["LastTriggerAt"] is DateTime at ? $", last at {at:HH:mm:ss}." : "."));
            text.AppendLine($"Last result: {s["LastResult"] as string ?? "none"}");
            text.Append($"Last error: {s["LastError"] as string ?? "none"}.");
            return text.ToString();
        }

        /// <summary>ReleaseLicensesNow ["DryRun=false"]: ReturnLeasedLicensesOnLogout, once, without waiting.</summary>
        [HotCommand("ReleaseLicensesNow", "Return leased licences once (dry run unless DryRun=false)")]
        public static string ReleaseLicensesNow(CommandContext ctx) => Release(DryRun(ctx), "on request");

        /// <summary>
        /// AutoRelease.Idle "DryRun=&lt;bool&gt;|IdleSeconds=&lt;n&gt;": run by the shim's timer when the idle threshold is
        /// crossed (not on a menu). Its text is kept as the status's "Last result".
        /// </summary>
        [HotCommand("AutoRelease.Idle", "Return the leased licences after an idle period (called by the shim's timer)")]
        public static string Idle(CommandContext ctx) =>
            Release(DryRun(ctx), $"after {ctx.Get("IdleSeconds", "?")} s idle");

        private static string Release(bool dryRun, string why)
        {
            ILicenseManager licenseManager = LicenseManager();
            var text = new StringBuilder();
            text.Append($"{DateTime.Now:HH:mm:ss} {why}: ");
            if (dryRun)
            {
                text.AppendLine("dry run, would call ILicenseManager.ReturnLeasedLicensesOnLogout.");
                AppendLicenseInfo(text, licenseManager);
                return text.ToString().TrimEnd();
            }

            text.AppendLine("calling ILicenseManager.ReturnLeasedLicensesOnLogout.");
            AppendLicenseInfo(text, licenseManager, "Before");
            licenseManager.ReturnLeasedLicensesOnLogout();
            // Read back: what the call returns is undocumented, so the result shows the licence state it left.
            AppendLicenseInfo(text, licenseManager, "After");
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

        private static void AppendLicenseInfo(StringBuilder text, ILicenseManager licenseManager, string label = "Current")
        {
            // The format of the two strings is undocumented: they are shown as received.
            licenseManager.GetCurrentLicenseInfo(out string ids, out string names);
            text.AppendLine($"{label} licence ids: '{ids}', names: '{names}'");
        }

        private static T Switch<T>(string key) where T : Delegate =>
            AppDomain.CurrentDomain.GetData(key) as T
            ?? throw new UserException("The idle watcher of the AutoReleaseLicense shim is not available. " +
                                       "The shim publishes it when AD creates the server module; restart AD after deploying this build.");

        private static int IdleSeconds(CommandContext ctx)
        {
            string value = ctx.Get("IdleSeconds", "30");
            if (!int.TryParse(value, out int seconds) || seconds < 1 || seconds > 86400)
                throw new UserException($"IdleSeconds '{value}': a whole number of seconds from 1 to 86400.");
            return seconds;
        }

        /// <summary>DryRun defaults to true: only "DryRun=false" (or 0) returns licences.</summary>
        private static bool DryRun(CommandContext ctx)
        {
            string value = ctx.Get("DryRun", "true");
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0")
                return false;
            if (value.Length == 0 || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1")
                return true;
            throw new UserException($"DryRun '{value}': true or false.");
        }
    }
}
