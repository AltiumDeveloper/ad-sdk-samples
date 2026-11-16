using System;
using System.Collections.Generic;
using System.IO;
using DXP;
using SCH;

namespace AdSamples.AddDeviceSheetPath
{
    /// <summary>
    /// Registers, lists and removes device sheet folders through SCH.IDeviceSheetManager, then flushes the options
    /// of the IntegratedLibrary server so the folder list is written to the user's settings.
    /// </summary>
    internal static class AddDeviceSheetPathCommands
    {
        // The server whose options are flushed after changing the folder list.
        private const string OptionsServerName = "IntegratedLibrary";

        /// <summary>AddDeviceSheetPath "Path=&lt;folder&gt;|Recursive": adds an existing folder to the device sheet folders.</summary>
        [HotCommand("AddDeviceSheetPath", "Add a folder to the device sheet folders")]
        public static string AddDeviceSheetPath(CommandContext ctx)
        {
            IDeviceSheetManager manager = DeviceSheetManager();
            bool recursive = ctx.Flag("Recursive");

            string path = ctx.Get("Path");
            if (string.IsNullOrWhiteSpace(path) && ctx.IsInteractive)
            {
                // From the menu there is no parameter string: let the user pick the folder in AD's own dialog.
                // ChooseDeviceFolder returns false when the dialog is cancelled.
                string chosen = string.Empty;
                if (!manager.ChooseDeviceFolder(ref chosen))
                    return "Cancelled: no folder added.";
                path = chosen;
            }
            if (string.IsNullOrWhiteSpace(path))
                throw new UserException("No Path specified. Pass it as a parameter: Path=<folder>[|Recursive].");

            string folder = Normalize(path);
            if (!Directory.Exists(folder))
                throw new UserException($"Folder '{folder}' does not exist.");

            // Whether AddDeviceFolder itself skips a folder that is already in the list is not documented, so the
            // folder is looked up first.
            int existing = IndexOf(manager, folder);
            if (existing >= 0)
                return $"'{folder}' is already device sheet folder #{existing} " +
                       $"(subfolders {OnOff(manager.GetFolders_SearchSubFolders(existing))}); nothing changed.";

            int before = manager.GetFoldersCount();
            // AddDeviceFolder returns a bool; what false means is not documented, so it is reported as returned.
            bool added = manager.AddDeviceFolder(folder, recursive);
            int after = manager.GetFoldersCount();

            string flush = FlushOptions();
            return $"AddDeviceFolder('{folder}', subfolders {OnOff(recursive)}) returned {added.ToString().ToLowerInvariant()}; " +
                   $"folders {before} -> {after}; WillSearchDeviceFolder = " +
                   $"{manager.WillSearchDeviceFolder(folder).ToString().ToLowerInvariant()} ({flush}).";
        }

        /// <summary>ListDeviceSheetPaths: one line per registered device sheet folder.</summary>
        [HotCommand("ListDeviceSheetPaths", "List the device sheet folders")]
        public static string ListDeviceSheetPaths(CommandContext ctx)
        {
            IDeviceSheetManager manager = DeviceSheetManager();
            int count = manager.GetFoldersCount();
            var lines = new List<string> { $"{count} device sheet folder(s):" };
            // Folder indexes start at 0 (reference: GetFolders_FolderPath).
            for (int i = 0; i < count; i++)
                lines.Add($"  #{i} {manager.GetFolders_FolderPath(i)} (subfolders {OnOff(manager.GetFolders_SearchSubFolders(i))})");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>RemoveDeviceSheetPath "Path=&lt;folder&gt;": removes a registered folder (cleanup for AddDeviceSheetPath).</summary>
        [HotCommand("RemoveDeviceSheetPath", "Remove a folder from the device sheet folders")]
        public static string RemoveDeviceSheetPath(CommandContext ctx)
        {
            string path = ctx.Get("Path");
            if (string.IsNullOrWhiteSpace(path))
                throw new UserException("No Path specified. Pass it as a parameter: Path=<folder>.");

            IDeviceSheetManager manager = DeviceSheetManager();
            string folder = Normalize(path);
            int index = IndexOf(manager, folder);
            if (index < 0)
                return $"'{folder}' is not a device sheet folder; nothing changed.";

            int before = manager.GetFoldersCount();
            manager.DeleteFolder(index);
            int after = manager.GetFoldersCount();

            string flush = FlushOptions();
            return $"Removed device sheet folder #{index} '{folder}'; folders {before} -> {after} ({flush}).";
        }

        private static IDeviceSheetManager DeviceSheetManager() =>
            EDP.Utils.GetDeviceSheetManager()
            ?? throw new UserException("The device sheet manager is not available (GetDeviceSheetManager returned null).");

        /// <summary>
        /// In .NET GetServerModuleByName returns IServerModule (null when the server is not loaded), so the
        /// IntegratedLibrary module is not cast straight to IServerOptions: this tests the cast and reports what happened instead of failing.
        /// </summary>
        private static string FlushOptions()
        {
            IServerModule module = DXP.GlobalVars.Client.GetServerModuleByName(OptionsServerName);
            if (module == null)
                return $"server '{OptionsServerName}' is not loaded, options not flushed";
            if (module is not IServerOptions options)
                return $"server '{OptionsServerName}' does not implement IServerOptions, options not flushed";
            options.FlushOptions();
            return $"server '{OptionsServerName}' flushed its options";
        }

        /// <summary>Index of the folder in the device sheet list, comparing normalized paths case-insensitively; -1 if absent.</summary>
        private static int IndexOf(IDeviceSheetManager manager, string folder)
        {
            int count = manager.GetFoldersCount();
            for (int i = 0; i < count; i++)
            {
                string registered = manager.GetFolders_FolderPath(i);
                if (string.IsNullOrEmpty(registered))
                    continue;
                string normalized;
                try { normalized = Normalize(registered); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                { normalized = registered; } // keep a stored path that is not a valid file-system path as it is
                if (string.Equals(normalized, folder, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // Full path without a trailing separator, so "X:\a\" and "X:\a" compare equal.
        private static string Normalize(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));

        private static string OnOff(bool value) => value ? "on" : "off";
    }
}
