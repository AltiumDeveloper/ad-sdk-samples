using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AdSamples.FixPcbComponent
{
    /// <summary>
    /// The import file: row 1 is "Designator" followed by parameter names; each later row is a designator list
    /// ("R1", "C1,C3", "R1-R5", or a mix) followed by the values. The file is CSV (Excel can
    /// save one), so it needs no package. Reading the file is not the SDK lesson; setting the parameters is.
    /// </summary>
    internal sealed class ParameterTable
    {
        public List<string> ParameterNames { get; } = new List<string>();

        /// <summary>Designator -> values (one per parameter name). The first row that names a designator wins.</summary>
        public Dictionary<string, List<string>> Values { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public int Rows { get; private set; }
        public List<string> DuplicateDesignators { get; } = new List<string>();

        public static ParameterTable Load(string path, string delimiterParam)
        {
            string text = File.ReadAllText(path); // UTF-8 unless the file has another BOM
            char delimiter = Delimiter(delimiterParam, text);
            List<List<string>> rows = ParseCsv(text, delimiter);

            if (rows.Count == 0 || rows[0].Count == 0 || !string.Equals(rows[0][0].Trim(), "Designator", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"{Path.GetFileName(path)}: the first cell of row 1 must be 'Designator' (delimiter '{Show(delimiter)}').");

            var table = new ParameterTable();
            // Parameter names: row 1 from column 2 up to the first empty cell.
            for (int c = 1; c < rows[0].Count && rows[0][c].Trim().Length > 0; c++)
                table.ParameterNames.Add(rows[0][c].Trim());
            if (table.ParameterNames.Count == 0)
                throw new UserException($"{Path.GetFileName(path)}: row 1 has no parameter names after 'Designator'.");

            // Data rows: up to the first row with an empty designator cell.
            for (int r = 1; r < rows.Count; r++)
            {
                List<string> row = rows[r];
                string designators = row.Count > 0 ? row[0].Trim() : "";
                if (designators.Length == 0)
                    break;

                var values = new List<string>();
                for (int c = 1; c <= table.ParameterNames.Count; c++)
                    values.Add(c < row.Count ? row[c] : "");

                List<string> expanded = ExpandDesignators(designators)
                    ?? throw new UserException($"{Path.GetFileName(path)}, row {r + 1}: '{designators}' is not a valid designator list " +
                                               "(use R1, R1,R3 or R1-R5 with the same prefix and an increasing number).");
                table.Rows++;
                foreach (string designator in expanded)
                {
                    if (table.Values.ContainsKey(designator))
                        table.DuplicateDesignators.Add(designator);
                    else
                        table.Values[designator] = values;
                }
            }
            return table;
        }

        /// <summary>
        /// Expands "R1-R5,C3 C7" into R1..R5, C3, C7, or returns null when a range is malformed. Separators are
        /// commas and spaces. A range needs the same
        /// prefix on both ends and a first number lower than the last.
        /// </summary>
        public static List<string> ExpandDesignators(string text)
        {
            var result = new List<string>();
            foreach (string token in text.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int dash = token.IndexOf('-');
                if (dash < 0)
                {
                    result.Add(token);
                    continue;
                }

                string first = token.Substring(0, dash), last = token.Substring(dash + 1);
                int prefixLength = PrefixLength(first);
                // A missing number or prefix (such as 'R-R5') is rejected up front.
                if (prefixLength == 0 || prefixLength == first.Length || prefixLength != PrefixLength(last) || prefixLength == last.Length)
                    return null;
                string prefix = first.Substring(0, prefixLength);
                if (!string.Equals(prefix, last.Substring(0, prefixLength), StringComparison.Ordinal))
                    return null;
                if (!int.TryParse(first.Substring(prefixLength), NumberStyles.None, CultureInfo.InvariantCulture, out int from) ||
                    !int.TryParse(last.Substring(prefixLength), NumberStyles.None, CultureInfo.InvariantCulture, out int to) ||
                    from >= to)
                    return null;
                for (int n = from; n <= to; n++)
                    result.Add(prefix + n.ToString(CultureInfo.InvariantCulture));
            }
            return result.Count == 0 ? null : result;
        }

        /// <summary>Length of the text before the trailing digits.</summary>
        private static int PrefixLength(string s)
        {
            int i = s.Length;
            while (i > 0 && s[i - 1] >= '0' && s[i - 1] <= '9')
                i--;
            return i;
        }

        /// <summary>Delimiter=, ; tab (or the character); without it: tab or ';' when row 1 has one and no ','.</summary>
        private static char Delimiter(string value, string text)
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (value.Equals("tab", StringComparison.OrdinalIgnoreCase) || value == "\\t")
                    return '\t';
                if (value.Length == 1)
                    return value[0];
                throw new UserException($"Delimiter must be one character or 'tab', not '{value}'.");
            }
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            string header = end < 0 ? text : text.Substring(0, end);
            if (header.Contains('\t')) return '\t';
            if (header.Contains(';') && !header.Contains(',')) return ';';
            return ',';
        }

        private static string Show(char c) => c == '\t' ? "tab" : c.ToString();

        /// <summary>Minimal RFC 4180 reader: quoted fields may hold the delimiter, quotes ("") and line breaks.</summary>
        private static List<List<string>> ParseCsv(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
