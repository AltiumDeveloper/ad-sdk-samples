using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace AdSamples.GridFilterForm
{
    /// <summary>
    /// A data-bound grid of customers and a combo box of named filters.
    /// Choosing a filter applies it by code (SetFilter); when the user changes the filter
    /// another way, the combo box switches to "Custom filter".
    /// DataGridView has no column header filter drop-downs, so the user filters through an
    /// editable expression box and a "Filter by this value" context menu on the cells instead.
    /// Built in code, no designer file.
    /// </summary>
    internal sealed class GridFilterDialog : Form
    {
        private readonly DataView _view;
        private readonly BindingSource _binding;
        private readonly DataGridView _grid;
        private readonly ComboBox _filters;
        private readonly TextBox _expression;
        private readonly Label _status;
        private bool _lock; // no feedback loop between the combo box and the filter

        public GridFilterDialog(DataTable customers, FilterScenario initial)
        {
            Text = "GridFilterForm: filter a grid by code";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 520);
            MinimizeBox = false;
            ShowInTaskbar = false;

            _view = new DataView(customers);
            _binding = new BindingSource { DataSource = _view };

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(6) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _filters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (var scenario in CustomerData.Scenarios)
                _filters.Items.Add(scenario);
            _filters.SelectedIndexChanged += (_, _) => OnScenarioSelected();

            _expression = new TextBox { Dock = DockStyle.Fill };
            _expression.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { ApplyUserFilter(_expression.Text); e.SuppressKeyPress = true; }
            };
            var apply = new Button { Text = "Apply", AutoSize = true };
            apply.Click += (_, _) => ApplyUserFilter(_expression.Text);

            top.Controls.Add(new Label { Text = "Current active filter:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            top.Controls.Add(_filters, 1, 0);
            top.Controls.Add(new Label { Text = "RowFilter expression (edit, then Enter or Apply):", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
            top.Controls.Add(_expression, 0, 1);
            top.SetColumnSpan(_expression, 3);
            top.Controls.Add(apply, 3, 1);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, // OptionsView.ColumnAutoWidth
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                RowHeadersVisible = false,
            };
            AddColumns();
            _grid.DataSource = _binding;
            _grid.CellMouseDown += OnCellMouseDown;

            _status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            top.Controls.Add(close, 3, 0);
            CancelButton = close; // Esc closes the form

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(_status);

            // Fill the list, preselect a scenario and apply it.
            SelectScenario(initial ?? CustomerData.Default, apply: true);
        }

        private void AddColumns()
        {
            DataGridViewTextBoxColumn Text(string field, string caption, string format = null) => new()
            {
                DataPropertyName = field, HeaderText = caption, Name = field,
                DefaultCellStyle = { Format = format },
            };

            // Lookup columns: the cells hold IDs, the grid shows names.
            DataGridViewComboBoxColumn Lookup(string field, string caption, System.Collections.Generic.IReadOnlyList<(int Id, string Name)> items)
            {
                var source = new DataTable();
                source.Columns.Add("Id", typeof(int));
                source.Columns.Add("Name", typeof(string));
                foreach (var (id, name) in items)
                    source.Rows.Add(id, name);
                return new DataGridViewComboBoxColumn
                {
                    DataPropertyName = field, HeaderText = caption, Name = field,
                    DataSource = source, ValueMember = "Id", DisplayMember = "Name",
                    DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                };
            }

            _grid.Columns.AddRange(
                Text("FIRSTNAME", "FirstName"),
                Text("LASTNAME", "LastName"),
                Text("COMPANYNAME", "Company"),
                Lookup("PAYMENTTYPE", "PaymentType", CustomerData.PaymentTypes),
                Lookup("PRODUCTID", "Product", CustomerData.Products),
                new DataGridViewCheckBoxColumn { DataPropertyName = "CUSTOMER", HeaderText = "Customer", Name = "CUSTOMER" },
                Text("PURCHASEDATE", "PurchaseDate", "d"),
                Text("PAYMENTAMOUNT", "PaymentAmount", "C0"),
                Text("COPIES", "Copies"));
        }

        /// <summary>A scenario chosen in the combo box is applied by code.</summary>
        private void OnScenarioSelected()
        {
            if (_lock || _filters.SelectedItem is not FilterScenario scenario)
                return;
            SetFilter(scenario);
        }

        /// <summary>Replace the whole filter with the scenario's.</summary>
        private void SetFilter(FilterScenario scenario)
        {
            _lock = true;
            try
            {
                if (scenario.IsCustom)
                {
                    // Clear the filter and show the hint in the form.
                    _binding.RemoveFilter();
                    _expression.Text = "";
                    _expression.Focus();
                    Report("Custom filter: type a RowFilter expression above, or right-click a cell and choose 'Filter by this value'.");
                    return;
                }
                _binding.Filter = scenario.Expression;
                _expression.Text = scenario.Expression;
                Report(null);
            }
            finally
            {
                _lock = false;
            }
        }

        /// <summary>
        /// A filter the user built. The combo box then shows
        /// "Custom filter", or "No filter" when the filter is empty.
        /// </summary>
        private void ApplyUserFilter(string expression)
        {
            try
            {
                _binding.Filter = string.IsNullOrWhiteSpace(expression) ? null : expression.Trim();
            }
            // Any exception: the text is user input, and an exception escaping this event handler would surface on
            // AD's UI thread. Which types RowFilter raises for every bad input (type mismatch, unknown column) was
            // not enumerated, so the handler does not try to name them.
            catch (Exception ex)
            {
                Report("Invalid filter: " + ex.Message);
                return;
            }
            SelectScenario(CustomerData.Find(string.IsNullOrWhiteSpace(expression) ? "None" : "Custom"), apply: false);
            _expression.Text = _binding.Filter ?? "";
            Report(null);
        }

        private void SelectScenario(FilterScenario scenario, bool apply)
        {
            _lock = true;
            _filters.SelectedItem = scenario;
            _lock = false;
            if (apply)
                SetFilter(scenario);
        }

        /// <summary>Right-click a cell: filter (or narrow the filter) to that cell's value.</summary>
        private void OnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            var column = _grid.Columns[e.ColumnIndex];
            var value = ((DataRowView)_grid.Rows[e.RowIndex].DataBoundItem)[column.DataPropertyName];
            var condition = $"{column.DataPropertyName} = {Literal(value)}";
            var current = _binding.Filter;
            var display = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue;

            var menu = new ContextMenuStrip();
            menu.Items.Add($"Filter by this value ({column.HeaderText} = {display})", null, (_, _) => ApplyUserFilter(condition));
            if (!string.IsNullOrEmpty(current))
                menu.Items.Add("Add to the current filter (AND)", null, (_, _) => ApplyUserFilter($"({current}) AND {condition}"));
            menu.Items.Add("Clear filter", null, (_, _) => ApplyUserFilter(""));
            menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(_grid, _grid.PointToClient(Cursor.Position));
        }

        /// <summary>A value as a RowFilter literal.</summary>
        private static string Literal(object value) => value switch
        {
            string s => "'" + s.Replace("'", "''") + "'",
            DateTime d => "#" + d.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture) + "#",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => "'" + value + "'",
        };

        private void Report(string message)
        {
            var counts = $"{_view.Count} of {_view.Table.Rows.Count} rows";
            _status.Text = message == null ? counts : counts + ".  " + message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _binding.Dispose();
                _view.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
