using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AdSamples.BomReport
{
    /// <summary>
    /// One line of the bill of materials: the designators it covers and its column values. Designator and Quantity
    /// are computed from the designators; every other column comes from the first component of the line.
    /// </summary>
    internal sealed class BomLine
    {
        public List<string> Designators { get; } = new List<string>();
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>False for lines whose Designator and Quantity are written as the BOM engine gives them.</summary>
        public bool ComputeDesignators { get; set; } = true;

        public int DesignatorCount => ComputeDesignators
            ? Designators.Count
            : Value(BomTable.DesignatorColumn).Split(',').Count(d => d.Trim().Length > 0);

        public string Value(string column)
        {
            if (!ComputeDesignators)
                return Values.TryGetValue(column, out string raw) ? raw : string.Empty;
            if (column == BomTable.DesignatorColumn)
                return string.Join(",", Designators.OrderBy(d => d, NaturalComparer.Instance));
            if (column == BomTable.QuantityColumn)
                return Designators.Count.ToString();
            return Values.TryGetValue(column, out string value) ? value : string.Empty;
        }
    }

    /// <summary>
    /// The BOM: its columns in order and its lines. All four sources fill one of these, so they share the output
    /// format: one row per line, "|Name=Value" for every column, the row starting with '|'.
    /// </summary>
    internal sealed class BomTable
    {
        public const string DesignatorColumn = "Designator";
        public const string QuantityColumn = "Quantity";

        public List<string> Columns { get; } = new List<string>();
        public List<BomLine> Lines { get; } = new List<BomLine>();

        public void AddColumn(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Columns.Contains(name))
                Columns.Add(name);
        }

        public IEnumerable<string> Rows()
        {
            foreach (BomLine line in Lines)
            {
                var row = new StringBuilder();
                foreach (string column in Columns)
                    row.Append('|').Append(column).Append('=').Append(line.Value(column));
                yield return row.ToString();
            }
        }

        /// <summary>
        /// Builds the table from single components (Project and Pcb sources): components whose GroupBy values are
        /// equal share a line. The lines keep the order of their first component. Columns: Designator, Quantity, then
        /// every parameter name in order of first appearance.
        /// </summary>
        public static BomTable Group(IEnumerable<(string Designator, Dictionary<string, string> Values)> components,
                                     IReadOnlyList<string> groupBy)
        {
            var table = new BomTable();
            table.AddColumn(DesignatorColumn);
            table.AddColumn(QuantityColumn);

            // A dictionary keyed by the GroupBy values gives the lines in order of first appearance.
            var lines = new Dictionary<string, BomLine>(StringComparer.Ordinal);
            foreach (var (designator, values) in components)
            {
                foreach (string name in values.Keys)
                    table.AddColumn(name);

                string key = string.Join("\u0001", groupBy.Select(g => values.TryGetValue(g, out string v) ? v : string.Empty));
                if (!lines.TryGetValue(key, out BomLine line))
                {
                    line = new BomLine();
                    foreach (var pair in values)
                        line.Values[pair.Key] = pair.Value;
                    lines.Add(key, line);
                    table.Lines.Add(line);
                }
                line.Designators.Add(designator);
            }
            return table;
        }
    }

    /// <summary>Orders designators as a person would: R2 before R10.</summary>
    internal sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();

        public int Compare(string x, string y)
        {
            x ??= string.Empty;
            y ??= string.Empty;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    string a = x.Substring(si, i - si).TrimStart('0'), b = y.Substring(sj, j - sj).TrimStart('0');
                    int c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            int rest = (x.Length - i).CompareTo(y.Length - j);
            return rest != 0 ? rest : string.CompareOrdinal(x, y);
        }
    }
}
