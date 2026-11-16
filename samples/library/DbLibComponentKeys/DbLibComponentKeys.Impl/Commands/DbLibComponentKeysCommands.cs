using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using EDP; // IDatabaseLibDocument, and the typed GetAllComponentKeys(int, DXP.IStrings) helper (IDatabaseLibDocumentHelper)
using SCH; // IIntegratedLibraryManager lives in namespace SCH, although EDP.Utils hands it out

namespace AdSamples.DbLibComponentKeys
{
    /// <summary>
    /// Opens a database library (.DbLib) through the integrated library manager and walks its tables.
    /// With Keys=true it also asks the library for each table's component keys, which AD returns by filling a
    /// DXP.IStrings list that the caller supplies.
    /// </summary>
    internal static class DbLibComponentKeysCommands
    {
        /// <summary>
        /// ListComponentKeys "DbLib=&lt;path&gt;|Keys=true|Max=&lt;n&gt;": reports the tables of the DbLib at DbLib
        /// (default: every database library in Available Libraries). Keys=true adds each table's component key count
        /// and its first Max keys (default 5, 0 = all).
        /// </summary>
        [HotCommand("ListComponentKeys", "List the tables and component keys of a DbLib")]
        public static string ListComponentKeys(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max", "5"));
            bool keys = ctx.Flag("Keys");

            IIntegratedLibraryManager manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new UserException("The integrated library manager is not available (EDP.Utils.LoadIntegratedLibraryManager returned null).");

            List<string> paths = LibraryPaths(ctx.Get("DbLib"), manager);

            var result = new StringBuilder();
            foreach (string path in paths)
            {
                ctx.Log("DbLib: " + path);
                IDatabaseLibDocument dbLib = manager.GetAvailableDBLibDocAtPath(path);
                if (dbLib == null)
                {
                    // Null for a DbLib that is not installed in Available Libraries.
                    result.AppendLine($"{path}: GetAvailableDBLibDocAtPath returned null (is the DbLib installed in Available Libraries?)");
                    continue;
                }
                AppendTables(result, dbLib, path, keys, max, ctx);
            }

            if (!keys)
                result.AppendLine("Component keys not read; pass Keys=true to read them (see the sample's README, Notes).");
            return result.ToString().TrimEnd();
        }

        /// <summary>The DbLib parameter as a full path, or every database library in Available Libraries.</summary>
        private static List<string> LibraryPaths(string dbLibParameter, IIntegratedLibraryManager manager)
        {
            if (!string.IsNullOrWhiteSpace(dbLibParameter))
            {
                string fullPath = Path.GetFullPath(dbLibParameter);
                if (!File.Exists(fullPath))
                    throw new UserException("DbLib file not found: " + fullPath);
                return new List<string> { fullPath };
            }

            // Available libraries (Installed, Project and Search path tabs) are indexed from 0.
            var paths = new List<string>();
            int count = manager.AvailableLibraryCount();
            for (int i = 0; i < count; i++)
            {
                if (manager.AvailableLibraryType(i) == TLibraryType.eLibDatabase)
                    paths.Add(manager.AvailableLibraryPath(i));
            }
            if (paths.Count == 0)
                throw new UserException($"No database library among the {count} available libraries. Install a .DbLib, or pass DbLib=<path to a .DbLib>.");
            return paths;
        }

        private static void AppendTables(StringBuilder result, IDatabaseLibDocument dbLib, string path, bool keys, int max, CommandContext ctx)
        {
            int tableCount = dbLib.GetTableCount();
            result.AppendLine($"{path}: {tableCount} table(s)");

            for (int t = 0; t < tableCount; t++)
            {
                string line = $"  [{t}] {dbLib.GetTableNameAt(t)}" + (dbLib.TableEnabled(t) ? "" : " (disabled)");
                if (keys)
                    line += ": " + DescribeKeys(dbLib, t, max, ctx);
                result.AppendLine(line);
            }
        }

        private static string DescribeKeys(IDatabaseLibDocument dbLib, int tableIndex, int max, CommandContext ctx)
        {
            // The caller supplies the list and AD fills it. No SDK type or
            // factory creates a DXP.IStrings, so this passes a managed implementation. Whether AD accepts it is not
            // documented: see the README, Notes.
            var componentKeys = new StringList();
            try
            {
                dbLib.GetAllComponentKeys(tableIndex, componentKeys);
            }
            catch (COMException ex)
            {
                ctx.Log($"GetAllComponentKeys({tableIndex}) failed: {ex.Message}");
                return $"GetAllComponentKeys failed: {ex.Message} (HRESULT 0x{ex.HResult:X8})";
            }

            int count = componentKeys.GetCount();
            int listed = max > 0 ? Math.Min(count, max) : count;
            var text = new StringBuilder($"{count} component key(s)");
            if (listed > 0)
            {
                var first = new List<string>(listed);
                for (int i = 0; i < listed; i++)
                    first.Add(componentKeys.GetItem(i));
                text.Append(": ").Append(string.Join(", ", first));
                if (listed < count)
                    text.Append($", ... ({count - listed} more)");
            }
            return text.ToString();
        }

        private static int ParseMax(string text)
        {
            if (!int.TryParse(text, out int max) || max < 0)
                throw new UserException($"Max must be a number >= 0 (0 = all keys), not '{text}'.");
            return max;
        }
    }

    /// <summary>
    /// A managed DXP.IStrings for AD to fill. DXP.IStrings is declared as a dispatch interface
    /// (InterfaceType InterfaceIsIDispatch, DispIds 660-663), and the typed GetAllComponentKeys helper passes the
    /// list on as a plain object. Public and COM-visible so that the COM-callable wrapper answers those DispIds;
    /// that AD calls it this way is not documented.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class StringList : DXP.IStrings
    {
        private readonly List<string> _items = new List<string>();

        public int Add(string argS)
        {
            _items.Add(argS);
            return _items.Count - 1;
        }

        public int GetCount() => _items.Count;

        public string GetItem(int argIndex) => _items[argIndex];

        public int IndexOf(string argValue) => _items.IndexOf(argValue);
    }
}
