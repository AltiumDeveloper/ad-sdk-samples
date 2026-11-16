using System;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AdSamples.GridFilterForm
{
    /// <summary>
    /// ShowUI: opens a modal form with a filterable customer grid.
    /// Without a user to close the form (the pipe), it applies the filters without UI and returns the row counts.
    /// </summary>
    internal static class GridFilterFormCommands
    {
        /// <summary>The open form, so an unload of this generation can close it (SampleHooks below).</summary>
        internal static Form OpenForm;

        [HotCommand("ShowUI", "Show a customer grid filtered by code")]
        public static string ShowUI(CommandContext ctx)
        {
            var requested = ctx.Get("Filter");
            var scenario = CustomerData.Find(requested);
            if (!string.IsNullOrEmpty(requested) && scenario == null)
                throw new UserException($"Unknown filter '{requested}'. Use one of: {string.Join(", ", CustomerData.Scenarios.Select(s => s.Key))}.");

            using var customers = CustomerData.CreateCustomers();

            // A modal form blocks AD until someone closes it: only when a user started the command.
            if (!ctx.IsInteractive)
                return Summary(customers, scenario);

            if (OpenForm != null)
                throw new UserException("The GridFilterForm form is already open.");

            using var form = new GridFilterDialog(customers, scenario);
            OpenForm = form;
            try
            {
                // Owned by the window IClient hands out. That is the invisible, zero-size TApplication window,
                // not the visible AD frame - and GetApplicationHandle returns the same handle. So the form stays
                // with AD, but FormStartPosition.CenterParent has no AD rectangle to centre on. Without a handle
                // the form is shown unowned.
                var owner = MainWindow();
                if (owner != null)
                    form.ShowDialog(owner);
                else
                    form.ShowDialog();
            }
            finally
            {
                OpenForm = null;
            }
            return null; // the form was the result; no message box after it
        }

        /// <summary>The non-interactive result: each scenario's RowFilter and how many rows it keeps.</summary>
        private static string Summary(DataTable customers, FilterScenario only)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{customers.Rows.Count} customers; filter scenarios (key: rows kept, RowFilter):");
            using var view = new DataView(customers);
            foreach (var s in only == null ? CustomerData.Scenarios : new[] { only })
            {
                if (s.IsCustom)
                {
                    sb.AppendLine($"  {s.Key}: built by the user in the form");
                    continue;
                }
                view.RowFilter = s.Expression;
                sb.AppendLine($"  {s.Key}: {view.Count}  [{(s.Expression.Length == 0 ? "no filter" : s.Expression)}]");
            }
            return sb.ToString().TrimEnd();
        }

        private static IWin32Window MainWindow()
        {
            var handle = DXP.GlobalVars.Client?.GetMainWindowHandle() ?? 0;
            return handle == 0 ? null : new WindowHandle(new IntPtr(handle));
        }

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}

namespace AdSamples
{
    internal static partial class SampleHooks
    {
        /// <summary>
        /// Debug hot reload: close a form still open from this generation so it does not keep the old code alive.
        /// A safety net: the hot reload host defers a reload while a command of the module is still running
        /// (HotReloadHost.cs in the template), and ShowUI runs until the modal form is closed.
        /// </summary>
        static partial void OnUnloadCore()
        {
            var form = GridFilterForm.GridFilterFormCommands.OpenForm;
            if (form != null && !form.IsDisposed)
                form.Close();
        }
    }
}
