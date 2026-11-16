using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DXP;

namespace AdSamples.SimetrixExporter
{
    /// <summary>
    /// Loads SIMetrixExporter.DLL, calls its ExportToSimetrix export and unloads it again, on every call (the
    /// equivalent of LoadLibrary / GetProcAddress / FreeLibrary).
    /// </summary>
    internal static class SimetrixDll
    {
        private const string DllName = "SIMetrixExporter.DLL";
        private const string ExportName = "ExportToSimetrix";

        /// <summary>
        /// The export's declaration: <c>Function (FileName : WideString; Format : WideString;
        /// Document : IServerDocument) : Boolean; StdCall;</c>. A WideString is a BSTR and a Boolean is
        /// one byte. The document is a native interface pointer (see <see cref="DocumentPointer"/>).
        /// The DLL is not part of this sample.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool ExportToSimetrixFn(
            [MarshalAs(UnmanagedType.BStr)] string fileName,
            [MarshalAs(UnmanagedType.BStr)] string format,
            IntPtr document);

        public static string Export(CommandContext ctx, string target, string format, IServerDocument document, string sourceName)
        {
            string what = $"{sourceName} as {format} to '{target}'";
            string dllPath;
            try
            {
                dllPath = Locate(ctx.Get("Dll"));
            }
            catch (UserException ex)
            {
                // Name what would have been exported: without the DLL this is all a run can show.
                throw new UserException($"Cannot export {what}: {ex.Message}");
            }
            ctx.Log($"Exporting {what} with {dllPath}");

            // A missing DLL, a DLL that does not load and a missing export are each reported. LoadLibrary failures surface as DllNotFoundException / BadImageFormatException.
            IntPtr module;
            try
            {
                // UseDllDirectoryForDependencies: the DLL's own dependencies are looked up in its folder too.
                module = NativeLibrary.Load(dllPath, typeof(SimetrixDll).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException)
            {
                throw new UserException($"Could not load '{dllPath}': {ex.Message}");
            }

            try
            {
                if (!NativeLibrary.TryGetExport(module, ExportName, out IntPtr address))
                    throw new UserException($"'{dllPath}' has no export named {ExportName}.");

                var exportToSimetrix = Marshal.GetDelegateForFunctionPointer<ExportToSimetrixFn>(address);
                IntPtr documentPointer = DocumentPointer(document, ctx.Get("DocumentIid"));
                bool ok;
                try
                {
                    // A crash inside the native DLL (an access violation) cannot be caught here: it ends the
                    // Altium Designer process.
                    ok = exportToSimetrix(target, format, documentPointer);
                }
                finally
                {
                    Marshal.Release(documentPointer);
                }

                // The DLL's result is returned.
                return ok
                    ? $"{ExportName}('{target}', '{format}', {sourceName}) returned True."
                    : throw new UserException($"{ExportName}('{target}', '{format}', {sourceName}) returned False.");
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }

        /// <summary>
        /// The pointer handed to the DLL. The .NET SDK interfaces are dispatch interfaces (they all carry the
        /// IDispatch IID), so .NET does not know the IID of the native IServerDocument interface that the export's
        /// declaration expects. By default the document's IDispatch pointer is passed; with DocumentIid=&lt;guid&gt;
        /// the native object is asked (QueryInterface) for that interface instead. Which of the two the DLL needs
        /// is an open point.
        /// </summary>
        private static IntPtr DocumentPointer(IServerDocument document, string iid)
        {
            if (string.IsNullOrWhiteSpace(iid))
                return Marshal.GetIDispatchForObject(document);

            if (!Guid.TryParse(iid, out Guid guid))
                throw new UserException($"DocumentIid '{iid}' is not a GUID.");

            IntPtr unknown = Marshal.GetIUnknownForObject(document);
            try
            {
                int hr = Marshal.QueryInterface(unknown, in guid, out IntPtr pointer);
                if (hr != 0 || pointer == IntPtr.Zero)
                    throw new UserException($"The document does not support the interface {guid:B} (HRESULT 0x{hr:X8}).");
                return pointer;
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        /// <summary>
        /// Dll=&lt;file or folder&gt;, else the folder of the extension. No .NET helper in the SDK returns the
        /// extension's folder (SpecialFolder_Extension has no .NET counterpart). The extension's folder is taken from the loaded
        /// shim assembly (SimetrixExporter.dll): in Debug builds the hot reload host loads this Impl assembly from
        /// a stream, so its own Location is empty.
        /// </summary>
        private static string Locate(string dll)
        {
            if (!string.IsNullOrWhiteSpace(dll))
            {
                string path = Directory.Exists(dll) ? Path.Combine(dll, DllName) : dll;
                return File.Exists(path) ? Path.GetFullPath(path) : throw new UserException($"'{path}' does not exist.");
            }

            var folders = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name == "SimetrixExporter")
                .Append(typeof(SimetrixDll).Assembly)
                .Select(SafeLocation)
                .Where(location => !string.IsNullOrEmpty(location))
                .Select(Path.GetDirectoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, DllName);
                if (File.Exists(path))
                    return path;
            }

            throw new UserException(
                $"{DllName} not found (looked in: {(folders.Count == 0 ? "no extension folder found" : string.Join("; ", folders))}). " +
                "It is not part of this sample: copy it from the SIMetrix/SIMPLIS integration package next to the extension, or pass Dll=<path>.");
        }

        private static string SafeLocation(Assembly assembly)
        {
            try
            {
                return assembly.Location;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}
