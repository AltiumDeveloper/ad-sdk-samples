using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace AdSamples.GridFilterForm
{
    /// <summary>
    /// The grid's data and the named filter scenarios. BCL only (System.Data): nothing here touches Altium Designer,
    /// which is the point: the grid and its filters are ordinary UI code inside an extension.
    /// The sample builds a small in-memory data set, so it needs no data files.
    /// </summary>
    internal static class CustomerData
    {
        // Product IDs the filters refer to (the four grid products) and the payment type values
        // (Cash = 1 ... AmEx = 4).
        public const int ExpressQuantumGrid = 3, XtraGrid = 7, AspxGrid = 9, XpressQuantumGrid = 14;
        public const int Cash = 1, Visa = 2, MasterCard = 3, AmericanExpress = 4;
        public const int CopiesCount = 3;

        public static readonly IReadOnlyList<(int Id, string Name)> Products = new[]
        {
            (1, "Charting Library"), (2, "Report Designer"), (ExpressQuantumGrid, "ExpressQuantumGrid"),
            (4, "Scheduler"), (5, "Spreadsheet"), (6, "Editors Library"), (XtraGrid, "XtraGrid"),
            (8, "PDF Viewer"), (AspxGrid, "ASPX Grid"), (10, "Navigation Bar"), (XpressQuantumGrid, "XpressQuantumGrid"),
        };

        public static readonly IReadOnlyList<(int Id, string Name)> PaymentTypes = new[]
        {
            (Cash, "Cash"), (Visa, "Visa"), (MasterCard, "Master Card"), (AmericanExpress, "American Express"),
        };

        // FirstName, LastName, Company, PaymentType, ProductId, Customer, PurchaseDate, PaymentAmount, Copies
        private static readonly (string, string, string, int, int, bool, DateTime, decimal, int)[] Rows =
        {
            ("Anna",    "Becker",   "Smith & Sons",          Cash,            ExpressQuantumGrid, true,  new DateTime(2000, 1, 14),  1299m, 4),
            ("Brian",   "Cole",     "Northwind Traders",     Visa,            XtraGrid,           true,  new DateTime(2000, 3, 2),   899m,  1),
            ("Carla",   "Diaz",     "Barnes & Co",           AmericanExpress, AspxGrid,           false, new DateTime(1999, 11, 30), 499m,  2),
            ("David",   "Evans",    "Contoso Ltd",           MasterCard,      1,                  true,  new DateTime(2000, 6, 18),  299m,  5),
            ("Elena",   "Fischer",  "Fabrikam Inc",          Visa,            XpressQuantumGrid,  false, new DateTime(2001, 2, 9),   1499m, 3),
            ("Frank",   "Garcia",   "Tailspin Toys",         Cash,            4,                  true,  new DateTime(2000, 12, 31), 399m,  1),
            ("Grace",   "Hughes",   "Wide World Importers",  AmericanExpress, ExpressQuantumGrid, true,  new DateTime(2001, 7, 4),   1299m, 6),
            ("Henry",   "Ito",      "Litware & Partners",    Visa,            2,                  false, new DateTime(2000, 9, 21),  599m,  2),
            ("Irene",   "Jensen",   "Adventure Works",       MasterCard,      XtraGrid,           true,  new DateTime(1999, 5, 5),   899m,  4),
            ("Jack",    "Klein",    "Proseware",             Cash,            5,                  false, new DateTime(2002, 1, 17),  349m,  1),
            ("Karen",   "Lopez",    "Coho Winery",           AmericanExpress, 6,                  true,  new DateTime(2000, 4, 11),  199m,  8),
            ("Liam",    "Moore",    "Alpine Ski House",      Visa,            AspxGrid,           true,  new DateTime(2000, 8, 30),  499m,  3),
            ("Maria",   "Novak",    "Blue Yonder & Co",      Cash,            XpressQuantumGrid,  true,  new DateTime(2001, 10, 1),  1499m, 2),
            ("Noah",    "Olsen",    "City Power & Light",    MasterCard,      ExpressQuantumGrid, false, new DateTime(2000, 2, 29),  1299m, 1),
            ("Olivia",  "Perez",    "Graphic Design Inst.",  AmericanExpress, 8,                  true,  new DateTime(1999, 12, 12), 249m,  2),
            ("Peter",   "Quinn",    "Margie's Travel",       Visa,            ExpressQuantumGrid, true,  new DateTime(2002, 5, 23),  1299m, 10),
            ("Rosa",    "Schmidt",  "Humongous Insurance",   Cash,            10,                 false, new DateTime(2000, 7, 7),   149m,  1),
            ("Samuel",  "Tanaka",   "Lucerne Publishing",    AmericanExpress, XtraGrid,           false, new DateTime(2001, 3, 15),  899m,  5),
            ("Tina",    "Urban",    "Southridge Video",      MasterCard,      AspxGrid,           true,  new DateTime(2000, 11, 2),  499m,  2),
            ("Victor",  "Weber",    "Trey Research",         Visa,            1,                  true,  new DateTime(2001, 8, 8),   299m,  1),
        };

        /// <summary>A new, filled Customers table.</summary>
        public static DataTable CreateCustomers()
        {
            var table = new DataTable("Customers") { Locale = System.Globalization.CultureInfo.InvariantCulture };
            table.Columns.Add("FIRSTNAME", typeof(string));
            table.Columns.Add("LASTNAME", typeof(string));
            table.Columns.Add("COMPANYNAME", typeof(string));
            table.Columns.Add("PAYMENTTYPE", typeof(int));
            table.Columns.Add("PRODUCTID", typeof(int));
            table.Columns.Add("CUSTOMER", typeof(bool));
            table.Columns.Add("PURCHASEDATE", typeof(DateTime));
            table.Columns.Add("PAYMENTAMOUNT", typeof(decimal));
            table.Columns.Add("COPIES", typeof(int));
            foreach (var r in Rows)
                table.Rows.Add(r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7, r.Item8, r.Item9);
            return table;
        }

        /// <summary>
        /// The named filter scenarios. Each is a DataView.RowFilter expression (System.Data expression syntax).
        /// "Custom" has no expression: the user builds it.
        /// </summary>
        public static readonly IReadOnlyList<FilterScenario> Scenarios = new[]
        {
            new FilterScenario("None", "No filter", ""),
            new FilterScenario("Custom", "Custom filter", null),
            new FilterScenario("Simple", $"COPIES > {CopiesCount}", $"COPIES > {CopiesCount}"),
            // RowFilter LIKE uses * (or %) as the wildcard.
            new FilterScenario("Like", "COMPANY contains \"&\" symbol", "COMPANYNAME LIKE '*&*'"),
            new FilterScenario("TwoField", "Only CUSTOMERs with PRODUCT = ExpressQuantumGrid",
                $"CUSTOMER = true AND PRODUCTID = {ExpressQuantumGrid}"),
            // RowFilter date literals are #M/d/yyyy#, independent of the user's locale.
            new FilterScenario("Between", "All PURCHASEDATEs of 2000 year",
                "PURCHASEDATE >= #1/1/2000# AND PURCHASEDATE <= #12/31/2000#"),
            new FilterScenario("OnlyGrids", "Only grid components", OnlyGrids),
            new FilterScenario("Group", "Only grid components paid by VISA or AmericanExpress",
                $"({OnlyGrids}) AND PAYMENTTYPE IN ({Visa}, {AmericanExpress})"),
            new FilterScenario("List", "PAYMENTTYPE is CASH or AMERICANEXPRESS",
                $"PAYMENTTYPE = {Cash} OR PAYMENTTYPE = {AmericanExpress}"),
        };

        /// <summary>Only grid products: four PRODUCTID = x items joined with OR.</summary>
        private static string OnlyGrids =>
            $"PRODUCTID = {ExpressQuantumGrid} OR PRODUCTID = {XpressQuantumGrid} OR PRODUCTID = {XtraGrid} OR PRODUCTID = {AspxGrid}";

        public static FilterScenario Default => Find("Simple"); // preselected when the form opens

        /// <summary>A scenario by key ("Simple"), description, or index (0-8); null when none matches.</summary>
        public static FilterScenario Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (int.TryParse(name, out var index))
                return index >= 0 && index < Scenarios.Count ? Scenarios[index] : null;
            return Scenarios.FirstOrDefault(s => s.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                                              || s.Description.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>A named filter. <see cref="Expression"/> is a DataView.RowFilter; null for the user-built one.</summary>
    internal sealed record FilterScenario(string Key, string Description, string Expression)
    {
        public bool IsCustom => Expression == null;
        public override string ToString() => Description; // the combo box shows this
    }
}
