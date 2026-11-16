using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // the IClient.OpenDocument/GetDocumentByPath/ShowDocument extension methods live in namespace DXP
using SCH;

namespace AdSamples.NavigateToSchLibComp
{
    /// <summary>
    /// Opens the source schematic library of a placed component and shows its symbol in the library editor. The SDK
    /// lessons, in order: find the component (ISch_Document.PopupMenuHitTest from the context menu), resolve its
    /// SourceLibraryName to a .SchLib file, open it (IClient.OpenDocument with kind "SCHLIB"), get the library
    /// object (ISch_ServerInterface.GetSchDocumentByPath), find the symbol (ISch_Lib.SchComponentByLibRef), make
    /// it the library's current component (ISch_Lib.Current_SchComponent) and show the document
    /// (IClient.ShowDocument). Nothing in the schematic or the library is changed.
    /// </summary>
    internal static class NavigateToSchLibCompCommands
    {
        /// <summary>
        /// NavigateToSchLibComp ["Designator=&lt;x&gt;|Component=popup|selected|LibraryFolders=&lt;a;b&gt;"]: opens the
        /// .SchLib of a component on the current sheet and shows its symbol. Workspace state only (a document opens).
        /// </summary>
        [HotCommand("NavigateToSchLibComp", "Open the source SchLib of a schematic component and show its symbol")]
        public static string NavigateToSchLibComp(CommandContext ctx)
        {
            string designator = ctx.Get("Designator", "").Trim();
            string mode = ctx.Get("Component", "selected").Trim().ToLowerInvariant();
            if (mode != "popup" && mode != "selected")
                throw new UserException($"Component must be 'popup' or 'selected', got '{mode}'.");

            ISch_ServerInterface schServer = SchServer();
            // Keep the sheet reference from the start: showing the library changes the current document.
            ISch_Document sheet = CurrentSheet(schServer);
            string sheetFile = Path.GetFileName(sheet.GetState_DocumentName());

            // 1. The component.
            (ISch_Component component, string foundBy) = FindComponent(sheet, designator, mode);
            string componentDesignator = DesignatorText(component);
            string libRef = component.GetState_LibReference();
            string sourceLibrary = component.GetState_SourceLibraryName();
            if (string.IsNullOrWhiteSpace(libRef))
                throw new UserException($"{sheetFile}: component {componentDesignator} has no LibReference, so its library symbol cannot be found.");

            // 2. The library file.
            string libPath = ResolveLibrary(ctx, sheet, sourceLibrary, out string searched);

            // 3. Open the library, or reuse it when it is already open.
            IClient client = DXP.GlobalVars.Client;
            IServerDocument libDocument = client.GetDocumentByPath(libPath);
            bool openedHere = false;
            if (libDocument == null)
            {
                ctx.Log($"Opening {libPath}");
                // "SCHLIB" is the document kind of a schematic library.
                libDocument = client.OpenDocument("SCHLIB", libPath)
                    ?? throw new UserException($"Altium Designer could not open {libPath} as a schematic library.");
                openedHere = true;
            }

            ISch_Document libAsDocument = schServer.GetSchDocumentByPath(libPath)
                ?? throw new UserException($"The schematic editor has no document for {libPath} after opening it.");
            // A type test would succeed for any schematic document (docs/guides/schematic-basics.mdx): check the ID.
            if (libAsDocument.GetState_ObjectId() != TObjectId.eSchLib)
                throw new UserException($"{libPath} is not a schematic library.");
            var library = (ISch_Lib)libAsDocument;

            // 4. The symbol, by the placed component's LibReference.
            ISch_Component libComponent = library.GetState_SchComponentByLibRef(libRef)
                ?? throw new UserException($"{Path.GetFileName(libPath)} has no component '{libRef}' " +
                                           $"({(openedHere ? "the library was opened and left open" : "the library was already open")}).");

            // 5. Make it the library's current component, redraw, and bring the library to the front, in that
            // order. Selecting a component changes only the editor's view, not the library.
            library.SetState_Current_SchComponent(libComponent);
            library.GraphicallyInvalidate();
            client.ShowDocument(libDocument);

            // 6. Read back rather than trusting the calls above.
            string current = library.GetState_Current_SchComponent()?.GetState_LibReference() ?? "(none)";
            var text = new StringBuilder();
            text.AppendLine($"{sheetFile}: component {componentDesignator} '{libRef}' (found by {foundBy}), source library '{sourceLibrary}'.");
            text.AppendLine($"Library: {libPath} ({(openedHere ? "opened by this command" : "was already open")}; searched: {searched}).");
            text.Append($"Shown symbol: current component of the library is '{current}'" +
                        (string.Equals(current, libRef, StringComparison.OrdinalIgnoreCase) ? "." : $", expected '{libRef}'."));
            return text.ToString();
        }

        /// <summary>
        /// ListSheetComponents ["Designator=&lt;x&gt;|Max=&lt;n&gt;"]: helper (read-only).
        /// Lists designator, LibReference and SourceLibraryName of the components on the current sheet.
        /// </summary>
        [HotCommand("ListSheetComponents", "List the components on the current sheet with their source library")]
        public static string ListSheetComponents(CommandContext ctx)
        {
            string designator = ctx.Get("Designator", "").Trim();
            int max = ParseMax(ctx.Get("Max", ""), 50);
            ISch_Document sheet = CurrentSheet(SchServer());
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            List<ISch_Component> components = SheetComponents(sheet);
            if (designator.Length > 0)
                components = components.Where(c => SameText(DesignatorText(c), designator)).ToList();
            if (components.Count == 0)
                return designator.Length > 0 ? $"{file}: no component with designator '{designator}'." : $"{file}: no components.";

            var text = new StringBuilder();
            text.Append($"{file}: {components.Count} component(s)");
            text.AppendLine(components.Count > max ? $", first {max} shown:" : ":");
            foreach (ISch_Component c in components.Take(max))
                text.AppendLine($"  {DesignatorText(c)} LibReference='{c.GetState_LibReference()}' " +
                                $"SourceLibrary='{c.GetState_SourceLibraryName()}' selected={c.GetState_Selection()}");
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// CurrentLibraryComponent: helper (read-only). Reports the current schematic
        /// document and, when it is a library, its current component, to check what NavigateToSchLibComp showed.
        /// </summary>
        [HotCommand("CurrentLibraryComponent", "Report the current schematic library and its current component")]
        public static string CurrentLibraryComponent(CommandContext ctx)
        {
            ISch_Document document = SchServer().GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active.");
            string file = Path.GetFileName(document.GetState_DocumentName());
            if (document.GetState_ObjectId() != TObjectId.eSchLib)
                return $"The current schematic document is {file}, not a schematic library.";
            ISch_Component current = ((ISch_Lib)document).GetState_Current_SchComponent();
            return current == null
                ? $"{file}: no current component."
                : $"{file}: current component '{current.GetState_LibReference()}'.";
        }

        /// <summary>
        /// The component: Designator=… when given (the pipe has no cursor). Otherwise Component=popup takes the
        /// first component of the right-click hit list (the context menu item passes it),
        /// and Component=selected (the default, for the main menu) the one selected component.
        /// </summary>
        private static (ISch_Component Component, string FoundBy) FindComponent(ISch_Document sheet, string designator, string mode)
        {
            string file = Path.GetFileName(sheet.GetState_DocumentName());
            if (designator.Length > 0)
            {
                List<ISch_Component> matches = SheetComponents(sheet).Where(c => SameText(DesignatorText(c), designator)).ToList();
                if (matches.Count == 0)
                    throw new UserException($"{file}: no component with designator '{designator}'.");
                // The parts of a multi-part component share one designator and one library symbol.
                return (matches[0], matches.Count == 1 ? "Designator" : $"Designator, first of {matches.Count} parts");
            }

            if (mode == "popup")
            {
                // The objects under the cursor when the schematic context menu opened.
                ISch_HitTest hitTest = sheet.PopupMenuHitTest();
                int count = hitTest?.GetState_HitTestCount() ?? 0;
                for (int i = 0; i < count; i++)
                {
                    ISch_GraphicalObject obj = hitTest.GetState_HitObject(i);
                    // Identify the object by ObjectId: a C# type test succeeds for every SDK object.
                    if (obj != null && obj.GetState_ObjectId() == TObjectId.eSchComponent)
                        return ((ISch_Component)obj, "PopupMenuHitTest");
                }
                throw new UserException($"{file}: no component under the right-click position (PopupMenuHitTest hit {count} object(s)).");
            }

            List<ISch_Component> selected = SheetComponents(sheet).Where(c => c.GetState_Selection()).ToList();
            if (selected.Count == 1)
                return (selected[0], "selection");
            throw new UserException($"{file}: {selected.Count} component(s) selected. Select exactly one component, " +
                                    "right-click it and use Open SchLib Component, or pass Designator=<designator>.");
        }

        /// <summary>
        /// The .SchLib file. SourceLibraryName is used as it is when it is a full path; else it is looked up in each
        /// folder of LibraryFolders=… (';'-separated), then in the sheet's folder
        /// and the sheet's Libraries subfolder, in that order. The first existing file wins.
        /// </summary>
        private static string ResolveLibrary(CommandContext ctx, ISch_Document sheet, string sourceLibrary, out string searched)
        {
            if (string.IsNullOrWhiteSpace(sourceLibrary))
                throw new UserException("The component has no SourceLibraryName, so its library cannot be found.");
            if (!sourceLibrary.EndsWith(".SchLib", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The component's source library is '{sourceLibrary}', not a .SchLib; this sample opens " +
                                        "schematic libraries only.");

            var candidates = new List<string>();
            if (Path.IsPathRooted(sourceLibrary))
            {
                candidates.Add(sourceLibrary);
            }
            else
            {
                IEnumerable<string> folders = ctx.Get("LibraryFolders", "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(f => f.Trim('"'))
                    .Where(f => f.Length > 0);
                candidates.AddRange(folders.Select(f => Path.Combine(f, sourceLibrary)));
                string sheetFolder = Path.GetDirectoryName(sheet.GetState_DocumentName());
                if (!string.IsNullOrEmpty(sheetFolder))
                {
                    candidates.Add(Path.Combine(sheetFolder, sourceLibrary));
                    candidates.Add(Path.Combine(sheetFolder, "Libraries", sourceLibrary));
                }
            }

            searched = string.Join("; ", candidates);
            string found = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
            return found ?? throw new UserException($"Source library '{sourceLibrary}' not found (searched: {searched}). " +
                                                    "Pass LibraryFolders=<folder>[;<folder>...].");
        }

        private static List<ISch_Component> SheetComponents(ISch_Document sheet)
        {
            var list = new List<ISch_Component>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });
                // The filter guarantees components, so cast.
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    list.Add((ISch_Component)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return list;
        }

        private const string NoDesignator = "(no designator)";

        private static string DesignatorText(ISch_Component component)
        {
            string text = component.GetState_SchDesignator()?.GetState_Text();
            return string.IsNullOrEmpty(text) ? NoDesignator : text;
        }

        private static bool SameText(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer
            ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            // A schematic library is an ISch_Document too; tell them apart by the object ID.
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static int ParseMax(string text, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive number (got '{text}').");
            return max;
        }
    }
}
