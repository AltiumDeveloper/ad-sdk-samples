using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP; // the IClient.OpenDocument/GetDocumentByPath extension methods live in namespace DXP
using SCH;

namespace AdSamples.EditSchComponent
{
    /// <summary>
    /// Edits the library symbol of a placed schematic component and brings the change back onto the sheet. The SDK
    /// lessons, in order: find a component from the right-click hit test (ISch_Document.PopupMenuHitTest), resolve
    /// and open its source .SchLib (IClient.OpenDocument, ISch_ServerInterface.GetSchDocumentByPath), find the
    /// symbol (ISch_Lib.SchComponentByLibRef), add a pin made by SchObjectFactory, save the library
    /// (IServerDocument.DoFileSave), then load the symbol again (LoadComponentFromLibrary) and swap it for the
    /// placed component (RemoveSchObject / AddSchObject) under PreProcess/PostProcess.
    /// </summary>
    internal static class EditSchComponentCommands
    {
        // Defaults of the pin parameters; the location is in mils (400, 330).
        private const string DefaultPinDesignator = "0";
        private const string DefaultPinName = "0";
        private const double DefaultPinXMils = 400;
        private const double DefaultPinYMils = 330;

        /// <summary>
        /// EditSchLibComponent ["Designator=&lt;x&gt;|Library=&lt;.SchLib path&gt;|LibraryFolder=&lt;folder&gt;|
        /// PinDesignator=0|PinName=0|PinX=400|PinY=330"]: adds a pin to the source library symbol of a component on
        /// the current sheet, saves the library, and replaces the placed component with the reloaded symbol.
        /// </summary>
        [HotCommand("EditSchLibComponent", "Add a pin to a component's library symbol and reload it")]
        public static string EditSchLibComponent(CommandContext ctx)
        {
            string designator = ctx.Get("Designator", "").Trim();
            string pinDesignator = ctx.Get("PinDesignator", DefaultPinDesignator).Trim();
            string pinName = ctx.Get("PinName", DefaultPinName).Trim();
            double pinXMils = ParseMils("PinX", ctx.Get("PinX", ""), DefaultPinXMils);
            double pinYMils = ParseMils("PinY", ctx.Get("PinY", ""), DefaultPinYMils);
            if (pinDesignator.Length == 0)
                throw new UserException("PinDesignator is empty. Pass the designator of the new pin, e.g. PinDesignator=99.");

            ISch_ServerInterface schServer = SchServer();
            // Keep the sheet reference from the start: opening the library may change which document is current.
            ISch_Document sheet = CurrentSheet(schServer);
            string sheetFile = Path.GetFileName(sheet.GetState_DocumentName());

            // 1. The component.
            (ISch_Component component, string foundBy) = FindComponent(sheet, designator);
            string oldDesignator = DesignatorText(component);
            string libRef = component.GetState_LibReference();
            string sourceLibrary = component.GetState_SourceLibraryName();
            if (string.IsNullOrWhiteSpace(libRef))
                throw new UserException($"{sheetFile}: component {oldDesignator} has no LibReference, so its library symbol cannot be found.");

            // 2. The library file.
            string libPath = ResolveLibrary(ctx, sheet, sourceLibrary, out string searched);

            // 3. Open the library (or reuse it when it is already open) and find the symbol.
            IClient client = DXP.GlobalVars.Client;
            IServerDocument libDocument = client.GetDocumentByPath(libPath);
            bool openedHere = false;
            if (libDocument == null)
            {
                ctx.Log($"Opening {libPath}");
                // "SCHLIB" is the document kind of a schematic library for OpenDocument.
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

            ISch_Component libComponent = library.GetState_SchComponentByLibRef(libRef)
                ?? throw new UserException($"{Path.GetFileName(libPath)} has no component '{libRef}'. Nothing was changed.");

            // Adding the same pin twice gives the symbol two pins with one designator. Refuse,
            // so that a second run does not silently damage the library.
            List<ISch_Pin> libPinsBefore = Pins(libComponent);
            if (libPinsBefore.Any(p => string.Equals(p.GetState_Designator(), pinDesignator, StringComparison.OrdinalIgnoreCase)))
                throw new UserException($"'{libRef}' in {Path.GetFileName(libPath)} already has a pin '{pinDesignator}'. " +
                                        "Nothing was changed. Pass another PinDesignator.");

            // 4. Add the pin to the library symbol. PreProcess/PostProcess bracket the change and the symbol, which
            // is the object that changes, gets the SCHMBeginModify/SCHMEndModify pair (docs/guides/schematic-basics.mdx
            // "Changing objects"). Which robot messages AD expects for a new child object is not documented.
            ISch_RobotManager robot = schServer.GetState_RobotManager();
            IProcessControl processControl = client.GetProcessControl();
            ISch_Pin pin;
            processControl.PreProcess(library, "");
            try
            {
                pin = schServer.SchObjectFactory(TObjectId.ePin, TObjectCreationMode.eCreate_Default) as ISch_Pin
                    ?? throw new UserException("SchObjectFactory did not create a pin.");
                pin.SetState_Location(new DXP.Point(new SPoint
                {
                    X = EDP.Utils.MilsToCoord(pinXMils),
                    Y = EDP.Utils.MilsToCoord(pinYMils),
                }));
                // 0 is black.
                pin.SetState_Color(0x000000);
                pin.SetState_Orientation(TRotationBy90.eRotate180);
                pin.SetState_Designator(pinDesignator);
                pin.SetState_Name(pinName);
                // A pin belongs to one part and display mode of the symbol: use the symbol's current ones.
                pin.SetState_OwnerPartId(libComponent.GetState_CurrentPartID());
                pin.SetState_OwnerPartDisplayMode(libComponent.GetState_DisplayMode());

                robot.SendMessage(libComponent, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                try
                {
                    libComponent.AddSchObject(pin);
                }
                finally
                {
                    robot.SendMessage(libComponent, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                }

                library.SetState_Current_SchComponent(libComponent);
            }
            finally
            {
                processControl.PostProcess(library, "");
            }
            library.GraphicallyInvalidate();
            int libPinsAfter = Pins(libComponent).Count;

            // 5. Save the library. If DoFileSave fails, stop before touching the sheet: reloading the symbol would
            // read a file that might not contain the pin.
            libDocument.SetModified(true);
            ctx.Log($"Saving {libPath}");
            bool saved = libDocument.DoFileSave("SCHLIB");
            if (!saved)
                throw new UserException($"DoFileSave('SCHLIB') returned false for {libPath}. The pin was added in memory " +
                                        "(close the library without saving to discard it); the sheet is unchanged.");

            // 6. Load the symbol again from the saved library: a free component, not yet on any sheet.
            ISch_Component newComponent = schServer.LoadComponentFromLibrary(libRef, libPath)
                ?? throw new UserException($"The library was saved with the new pin, but LoadComponentFromLibrary('{libRef}', " +
                                           $"'{libPath}') returned nothing. The sheet is unchanged.");

            // Read everything needed from the placed component before it leaves the sheet.
            DXP.Point location = component.GetState_Location();
            int x = location.X, y = location.Y;
            int partId = component.GetState_CurrentPartID();
            TRotationBy90 orientation = component.GetState_Orientation();
            bool mirrored = component.GetState_IsMirrored();
            int sheetPinsBefore = Pins(component).Count;

            // 7. Swap the placed component for the reloaded one, inside one PreProcess/PostProcess bracket. Removal is
            // registered as the docs example ISch_BasicContainer.RemoveSchObject does; the addition is add, move,
            // recompute the size, register.
            processControl.PreProcess(sheet, "");
            try
            {
                sheet.RemoveSchObject(component);
                robot.SendMessage(sheet, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, component);

                sheet.AddSchObject(newComponent);
                newComponent.MoveToXY(x, y);
                // A freshly loaded component has the library's default designator, part 1,
                // and no rotation. Carry the placed component's values over so the sheet keeps its meaning.
                newComponent.SetState_CurrentPartID(partId);
                newComponent.SetState_Orientation(orientation);
                newComponent.SetState_IsMirrored(mirrored);
                ISch_Designator newDesignator = newComponent.GetState_SchDesignator();
                if (newDesignator != null && oldDesignator != NoDesignator)
                    newDesignator.SetState_Text(oldDesignator);
                newComponent.SetState_xSizeySize();
                robot.SendMessage(sheet, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, newComponent);
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }
            sheet.GraphicallyInvalidate();

            // 8. Read back from the sheet rather than trusting the steps above.
            List<ISch_Component> matches = SheetComponents(sheet)
                .Where(c => string.Equals(DesignatorText(c), oldDesignator, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ISch_Component readBack = matches.FirstOrDefault(c => c.GetState_CurrentPartID() == partId) ?? matches.FirstOrDefault();

            var text = new StringBuilder();
            text.AppendLine($"{sheetFile}: component {oldDesignator} '{libRef}' (found by {foundBy}).");
            text.AppendLine($"Library: {libPath} ({(openedHere ? "opened by this command, left open" : "was already open")}; searched: {searched}).");
            text.AppendLine($"Added pin '{pinDesignator}' name '{pinName}' at ({Mils(pin.GetState_Location().X)}, {Mils(pin.GetState_Location().Y)}) mils, " +
                            $"orientation {pin.GetState_Orientation()}, part {pin.GetState_OwnerPartId()}: library symbol pins {libPinsBefore.Count} -> {libPinsAfter}. " +
                            "DoFileSave('SCHLIB') returned true.");
            if (readBack == null)
            {
                text.Append($"Read back: no component with designator {oldDesignator} found on the sheet after the swap.");
            }
            else
            {
                DXP.Point at = readBack.GetState_Location();
                text.Append($"Read back: {DesignatorText(readBack)} '{readBack.GetState_LibReference()}' at ({Mils(at.X)}, {Mils(at.Y)}) mils, " +
                            $"part {readBack.GetState_CurrentPartID()}, pins {sheetPinsBefore} -> {Pins(readBack).Count} " +
                            $"({matches.Count} component(s) with that designator). " +
                            "The sheet change is in memory only; the library is saved.");
            }
            return text.ToString();
        }

        /// <summary>
        /// ListSheetComponents ["Designator=&lt;x&gt;|Max=&lt;n&gt;"]: helper (read-only).
        /// Lists designator, LibReference, SourceLibraryName, part, pin count and location of each component.
        /// </summary>
        [HotCommand("ListSheetComponents", "List the components on the current sheet with their library and pin count")]
        public static string ListSheetComponents(CommandContext ctx)
        {
            string designator = ctx.Get("Designator", "").Trim();
            int max = ParseMax(ctx.Get("Max", ""), 50);
            ISch_Document sheet = CurrentSheet(SchServer());
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            List<ISch_Component> components = SheetComponents(sheet);
            if (designator.Length > 0)
                components = components
                    .Where(c => string.Equals(DesignatorText(c), designator, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (components.Count == 0)
                return designator.Length > 0 ? $"{file}: no component with designator '{designator}'." : $"{file}: no components.";

            var text = new StringBuilder();
            text.Append($"{file}: {components.Count} component(s)");
            text.AppendLine(components.Count > max ? $", first {max} shown:" : ":");
            foreach (ISch_Component c in components.Take(max))
            {
                DXP.Point at = c.GetState_Location();
                text.AppendLine($"  {DesignatorText(c)} LibReference='{c.GetState_LibReference()}' SourceLibrary='{c.GetState_SourceLibraryName()}' " +
                                $"part {c.GetState_CurrentPartID()} pins {Pins(c).Count} at ({Mils(at.X)}, {Mils(at.Y)}) mils");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// The component to edit: Designator=… when given (the pipe has no cursor), else the component under the
        /// right-click, else the one selected component.
        /// </summary>
        private static (ISch_Component Component, string FoundBy) FindComponent(ISch_Document sheet, string designator)
        {
            string file = Path.GetFileName(sheet.GetState_DocumentName());
            if (designator.Length > 0)
            {
                List<ISch_Component> matches = SheetComponents(sheet)
                    .Where(c => string.Equals(DesignatorText(c), designator, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 0)
                    throw new UserException($"{file}: no component with designator '{designator}'. Nothing was changed.");
                // The parts of a multi-part component share one designator; all of them use the same symbol.
                return (matches[0], matches.Count == 1 ? "Designator" : $"Designator, first of {matches.Count} parts");
            }

            // The objects under the cursor when the schematic context menu opened. Outside
            // a context menu there may be no hit list at all.
            ISch_HitTest hitTest = sheet.PopupMenuHitTest();
            int count = hitTest?.GetState_HitTestCount() ?? 0;
            for (int i = 0; i < count; i++)
            {
                ISch_GraphicalObject obj = hitTest.GetState_HitObject(i);
                // Identify the object by ObjectId: a C# type test succeeds for every SDK object.
                if (obj != null && obj.GetState_ObjectId() == TObjectId.eSchComponent)
                    return ((ISch_Component)obj, "PopupMenuHitTest");
            }

            List<ISch_Component> selected = SheetComponents(sheet).Where(c => c.GetState_Selection()).ToList();
            if (selected.Count == 1)
                return (selected[0], "selection");

            throw new UserException($"{file}: no component found (PopupMenuHitTest hit {count} object(s), " +
                                    $"{selected.Count} component(s) selected). Pass Designator=<designator>, or select exactly one component.");
        }

        /// <summary>
        /// The .SchLib to edit. Library=… wins. Otherwise SourceLibraryName is used as it is when it is a full path,
        /// and else looked up in LibraryFolder=…, the sheet's folder and the sheet's Libraries subfolder, in that order.
        /// </summary>
        private static string ResolveLibrary(CommandContext ctx, ISch_Document sheet, string sourceLibrary, out string searched)
        {
            string explicitLibrary = ctx.Get("Library", "").Trim().Trim('"');
            if (explicitLibrary.Length > 0)
            {
                string path = Path.GetFullPath(explicitLibrary);
                searched = "Library parameter";
                if (!File.Exists(path))
                    throw new UserException($"Library file not found: {path}. Nothing was changed.");
                return path;
            }

            if (string.IsNullOrWhiteSpace(sourceLibrary))
                throw new UserException("The component has no SourceLibraryName. Pass Library=<path to the .SchLib>. Nothing was changed.");
            if (!sourceLibrary.EndsWith(".SchLib", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The component's source library is '{sourceLibrary}', not a .SchLib; this sample edits " +
                                        "schematic libraries only. Pass Library=<path to a .SchLib>. Nothing was changed.");

            var candidates = new List<string>();
            if (Path.IsPathRooted(sourceLibrary))
            {
                candidates.Add(sourceLibrary);
            }
            else
            {
                string folder = ctx.Get("LibraryFolder", "").Trim().Trim('"');
                if (folder.Length > 0)
                    candidates.Add(Path.Combine(folder, sourceLibrary));
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
                                                    "Pass LibraryFolder=<folder> or Library=<path to the .SchLib>. Nothing was changed.");
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

        /// <summary>The pins of a component (of all its parts and display modes), through a child iterator.</summary>
        private static List<ISch_Pin> Pins(ISch_Component component)
        {
            var list = new List<ISch_Pin>();
            ISch_Iterator iterator = component.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the component.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.ePin });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    list.Add((ISch_Pin)obj);
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return list;
        }

        private const string NoDesignator = "(no designator)";

        private static string DesignatorText(ISch_Component component)
        {
            string text = component.GetState_SchDesignator()?.GetState_Text();
            return string.IsNullOrEmpty(text) ? NoDesignator : text;
        }

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer
            ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");

        private static ISch_Document CurrentSheet(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            // A schematic library is an ISch_Document too; tell them apart by the object ID
            // (docs/guides/schematic-basics.mdx "The current document").
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        // Invariant culture so the pipe output does not depend on the Windows locale. EDP.Utils assumes 10,000
        // internal units per mil; this holds for schematic coordinates too.
        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);

        private static double ParseMils(string name, string text, double defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new UserException($"{name} must be a number of mils, got '{text}'.");
            return value;
        }

        private static int ParseMax(string text, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text))
                return defaultValue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive number (got '{text}').");
            return max;
        }
    }
}
