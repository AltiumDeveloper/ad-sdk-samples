using System;
using System.Runtime.InteropServices;
using System.Text;
using SCH;

namespace AdSamples.LibraryManagersInfo
{
    /// <summary>
    /// Reads four library and component-data managers that EDP.Utils hands out: the integrated library manager,
    /// the model type manager, the device sheet manager and the supplier manager.
    /// </summary>
    internal static class LibraryManagersInfoCommands
    {
        /// <summary>
        /// ListLibraryManagers ["ChooseDevice=true"] ["Max=&lt;n&gt;"]: lists the available libraries, the model types
        /// and the supplier sources. With ChooseDevice (menu only) it first opens the device sheet folder picker.
        /// </summary>
        [HotCommand("ListLibraryManagers", "List integrated libraries, model types and supplier sources")]
        public static string ListLibraryManagers(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max", "50"));
            var report = new StringBuilder();

            AppendLibraries(report, max);
            AppendModelTypes(report, max);
            if (ctx.Flag("ChooseDevice"))
                AppendDeviceFolderChoice(report, ctx);
            AppendSuppliers(report, max);

            return report.ToString().TrimEnd();
        }

        private static void AppendLibraries(StringBuilder report, int max)
        {
            // The accessor's .NET name is LoadIntegratedLibraryManager (the reference text calls it IntegratedLibraryManager).
            IIntegratedLibraryManager libraries = EDP.Utils.LoadIntegratedLibraryManager();
            if (libraries == null)
            {
                report.AppendLine("Available libraries: manager not available (EDP.Utils.LoadIntegratedLibraryManager returned null).");
                return;
            }

            // Indexes run from 0 to Count - 1 (reference: "the first installed library ... is indexed zero").
            int count = libraries.AvailableLibraryCount();
            report.AppendLine($"Available libraries: {count}");
            for (int i = 0; i < count && i < max; i++)
                report.AppendLine($"  [{i}] {libraries.AvailableLibraryPath(i)}");
            AppendMore(report, count, max);
        }

        private static void AppendModelTypes(StringBuilder report, int max)
        {
            IModelTypeManager modelTypes = EDP.Utils.LoadModelTypeManager();
            if (modelTypes == null)
            {
                report.AppendLine("Model types: manager not available (EDP.Utils.LoadModelTypeManager returned null).");
                return;
            }

            int count = modelTypes.ModelTypeCount();
            report.AppendLine($"Model types: {count}");
            for (int i = 0; i < count && i < max; i++)
            {
                // ModelTypeAt is an extension method of the .NET API (it wraps the COM call and types the result).
                IModelType modelType = modelTypes.ModelTypeAt(i);
                report.AppendLine(modelType == null
                    ? $"  [{i}] (null)"
                    : $"  [{i}] {modelType.Name()} ({modelType.Description()})");
            }
            AppendMore(report, count, max);
        }

        private static void AppendDeviceFolderChoice(StringBuilder report, CommandContext ctx)
        {
            // ChooseDeviceFolder opens a modal dialog: never on the pipe, where no one can close it.
            if (!ctx.IsInteractive)
            {
                report.AppendLine("Device sheet folder: skipped (ChooseDevice opens a modal dialog; use the menu item).");
                return;
            }

            IDeviceSheetManager deviceSheets = EDP.Utils.GetDeviceSheetManager();
            if (deviceSheets == null)
            {
                report.AppendLine("Device sheet folder: manager not available (EDP.Utils.GetDeviceSheetManager returned null).");
                return;
            }

            // Start with an empty folder. The result is false when the dialog is cancelled.
            string folder = string.Empty;
            bool chosen = deviceSheets.ChooseDeviceFolder(ref folder);
            report.AppendLine(chosen
                ? $"Device sheet folder chosen: {folder}"
                : "Device sheet folder: dialog cancelled.");
        }

        private static void AppendSuppliers(StringBuilder report, int max)
        {
            ISupplierManager suppliers = EDP.Utils.GetSupplierManager();
            if (suppliers == null)
            {
                report.AppendLine("Supplier sources: manager not available (EDP.Utils.GetSupplierManager returned null).");
                return;
            }

            // The supplier-search part of ISupplierManager is retired: GetSupplierSourceCount throws a
            // COMException (0x8000FFFF) "Supplier Search functionality of ISupplierManager is obsolete. Please use
            // interface IPartSearchManager". IPartSearchManager offers only interactive dialogs and no
            // list of sources, so the section reports the error instead of failing the whole command.
            int count;
            try
            {
                count = suppliers.GetSupplierSourceCount();
            }
            catch (COMException ex)
            {
                report.AppendLine($"Supplier sources: not available ({ex.Message})");
                return;
            }
            report.AppendLine($"Supplier sources: {count}");
            for (int i = 0; i < count && i < max; i++)
            {
                // The SDK marks this extension method [Obsolete] (CS0612) without a message or a replacement; it is
                // the only public way to reach a supplier source by index, so the warning is suppressed here only.
#pragma warning disable CS0612
                ISupplier supplier = suppliers.GetSupplierSource(i);
#pragma warning restore CS0612
                report.AppendLine(supplier == null ? $"  [{i}] (null)" : $"  [{i}] {supplier.SourceName()}");
            }
            AppendMore(report, count, max);
        }

        private static void AppendMore(StringBuilder report, int count, int max)
        {
            if (count > max)
                report.AppendLine($"  ... {count - max} more (pass Max=<n> to list more)");
        }

        private static int ParseMax(string text)
        {
            if (!int.TryParse(text, out int max) || max < 0)
                throw new UserException($"Max must be a non-negative whole number, got '{text}'.");
            return max;
        }
    }
}
