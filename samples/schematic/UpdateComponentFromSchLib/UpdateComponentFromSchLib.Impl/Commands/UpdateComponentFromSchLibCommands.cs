using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.UpdateComponentFromSchLib
{
    /// <summary>
    /// Replaces a placed schematic component with a fresh copy of its symbol from the source .SchLib. The SDK
    /// lessons, in order: find the component under the right-click (ISch_Document.PopupMenuHitTest), resolve its
    /// SourceLibraryName to a file, load a free copy of the symbol (ISch_ServerInterface.LoadComponentFromLibrary),
    /// and swap it for the placed one (RemoveSchObject / AddSchObject, MoveToXY, SetState_xSizeySize) with each
    /// change announced to the editor through the robot manager (SCHMPrimitiveRegistration) inside
    /// PreProcess/PostProcess.
    /// </summary>
    internal static class UpdateComponentFromSchLibCommands
    {
        /// <summary>
        /// UpdateComponentFromSchLib ["Designator=&lt;x&gt;|LibraryFolders=&lt;dir;dir&gt;|KeepUniqueId=false"]:
        /// reloads the component from its library and puts the copy where the old one was.
        /// </summary>
        [HotCommand("UpdateComponentFromSchLib", "Reload a placed component from its source SchLib")]
        public static string UpdateComponentFromSchLib(CommandContext ctx)
        {
            string designator = ctx.Get("Designator", "").Trim();
            // Default true; KeepUniqueId=false gives the reloaded copy AD's new ID.
            bool keepUniqueId = !string.Equals(ctx.Get("KeepUniqueId", "true").Trim(), "false", StringComparison.OrdinalIgnoreCase);

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            string sheetFile = Path.GetFileName(sheet.GetState_DocumentName());

            // 1. The component: the right-click hit, or Designator=... for the pipe and the main menu.
            (ISch_Component component, string foundBy) = FindComponent(sheet, designator);
            string libRef = component.GetState_LibReference();
            string sourceLibrary = component.GetState_SourceLibraryName();
            string libraryPath = component.GetState_LibraryPath();
            if (string.IsNullOrWhiteSpace(libRef))
                throw new UserException($"{sheetFile}: component {DesignatorText(component)} has no LibReference. Nothing was changed.");

            // 2. The library file: the search folders are a parameter, and the folder of the sheet is searched too.
            string libPath = ResolveLibrary(ctx, sheet, sourceLibrary, out string searched);

            // 3. A free copy of the symbol, not on any sheet yet.
            ISch_Component newComponent = schServer.LoadComponentFromLibrary(libRef, libPath)
                ?? throw new UserException($"LoadComponentFromLibrary('{libRef}', '{libPath}') returned nothing. Nothing was changed.");
            // Make sure the library gave us the symbol we asked for.
            string newLibRef = newComponent.GetState_LibReference();
            if (!string.Equals(newLibRef, libRef, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"LoadComponentFromLibrary('{libRef}', ...) returned '{newLibRef}'. Nothing was changed.");

            // Read everything needed from the placed component before it leaves the sheet: whether a removed
            // object stays readable is not documented.
            ComponentState before = ComponentState.Of(component);

            // 4. The swap, in one PreProcess/PostProcess bracket. The removal is registered as the docs example
            // ISch_BasicContainer.RemoveSchObject does; the addition is add, move, recompute the
            // size, register. Which robot
            // messages AD expects for each change is not documented.
            ISch_RobotManager robot = schServer.GetState_RobotManager();
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            processControl.PreProcess(sheet, "");
            try
            {
                sheet.RemoveSchObject(component);
                robot.SendMessage(sheet, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, component);

                sheet.AddSchObject(newComponent);
                newComponent.MoveToXY(before.X, before.Y);
                // A loaded copy has the library's default designator, part 1 and no rotation,
                // and a new unique ID (which the PCB uses to match the component). Carry the placed values over.
                newComponent.SetState_CurrentPartID(before.PartId);
                newComponent.SetState_Orientation(before.Orientation);
                newComponent.SetState_IsMirrored(before.Mirrored);
                ISch_Designator newDesignator = newComponent.GetState_SchDesignator();
                if (newDesignator != null && before.Designator != NoDesignator)
                    newDesignator.SetState_Text(before.Designator);
                // A copy from a .SchLib carries the SourceLibraryName stored in the
                // library symbol itself ('PassiveSMD.IntLib' in SpiritLevel-SL1), not the file it was loaded from, so
                // without this the sheet component loses the link to the .SchLib and a second update fails.
                newComponent.SetState_SourceLibraryName(sourceLibrary);
                newComponent.SetState_LibraryPath(libraryPath);
                if (keepUniqueId && !string.IsNullOrEmpty(before.UniqueId))
                    newComponent.SetState_UniqueId(before.UniqueId);
                newComponent.SetState_xSizeySize();
                robot.SendMessage(sheet, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, newComponent);
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }
            sheet.GraphicallyInvalidate();

            // 5. Read back from the sheet rather than trusting the steps above.
            List<ISch_Component> matches = SheetComponents(sheet)
                .Where(c => string.Equals(DesignatorText(c), before.Designator, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ISch_Component readBack = matches.FirstOrDefault(c => c.GetState_CurrentPartID() == before.PartId) ?? matches.FirstOrDefault();

            var text = new StringBuilder();
            text.AppendLine($"{sheetFile}: replaced component {before.Designator} '{libRef}' (found by {foundBy}) with a copy from {libPath} " +
                            $"(searched: {searched}).");
            text.AppendLine("Before: " + before.Describe());
            text.Append("After:  ");
            text.Append(readBack == null
                ? $"no component with designator {before.Designator} found on the sheet after the swap."
                : ComponentState.Of(readBack).Describe() + $" ({matches.Count} component(s) with that designator).");
            text.AppendLine();
            text.Append($"Unique ID {(keepUniqueId ? "carried over" : "not carried over (KeepUniqueId=false)")}. " +
                        "Parameter values changed on the sheet are not carried over. The sheet change is in memory only.");
            return text.ToString();
        }

        /// <summary>
        /// ComponentInfo ["Designator=&lt;x&gt;"]: helper (read-only). The component's
        /// library, part, orientation, location, pin count and unique ID, to check an update.
        /// </summary>
        [HotCommand("ComponentInfo", "Report a placed component's library, part, location and unique ID")]
        public static string ComponentInfo(CommandContext ctx)
        {
            ISch_Document sheet = CurrentSheet(SchServer());
            (ISch_Component component, string foundBy) = FindComponent(sheet, ctx.Get("Designator", "").Trim());
            ComponentState state = ComponentState.Of(component);
            return $"{Path.GetFileName(sheet.GetState_DocumentName())}: {state.Describe()} " +
                   $"SourceLibraryName='{component.GetState_SourceLibraryName()}' LibraryPath='{component.GetState_LibraryPath()}' " +
                   $"(found by {foundBy}).";
        }

        /// <summary>What the update reads from the placed component, and what the result reports.</summary>
        private sealed class ComponentState
        {
            public string Designator, LibReference, UniqueId;
            public int X, Y, PartId, Pins;
            public TRotationBy90 Orientation;
            public bool Mirrored;

            public static ComponentState Of(ISch_Component c)
            {
                DXP.Point location = c.GetState_Location();
                return new ComponentState
                {
                    Designator = DesignatorText(c),
                    LibReference = c.GetState_LibReference(),
                    UniqueId = c.GetState_UniqueId(),
                    X = location.X,
                    Y = location.Y,
                    PartId = c.GetState_CurrentPartID(),
                    Orientation = c.GetState_Orientation(),
                    Mirrored = c.GetState_IsMirrored(),
                    Pins = PinCount(c),
                };
            }

            public string Describe() =>
                $"{Designator} '{LibReference}' at ({Mils(X)}, {Mils(Y)}) mils, part {PartId}, {Orientation}" +
                $"{(Mirrored ? ", mirrored" : "")}, {Pins} pins, UniqueId {UniqueId}.";
        }

        /// <summary>
        /// Designator=... when given (the pipe has no cursor), else the component under the right-click, else the
        /// one selected component.
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
                // The parts of a multi-part component share one designator; the first one is used.
                return (matches[0], matches.Count == 1 ? "Designator" : $"Designator, first of {matches.Count} parts");
            }

            // The objects under the cursor when the schematic context menu opened. In .NET PopupMenuHitTest is a
            // member of ISch_Document.
            ISch_HitTest hitTest = sheet.PopupMenuHitTest();
            int count = hitTest?.GetState_HitTestCount() ?? 0;
            for (int i = 0; i < count; i++)
            {
                ISch_GraphicalObject obj = hitTest.GetState_HitObject(i);
                // A C# type test succeeds for every SDK object: branch on ObjectId, then cast.
                if (obj != null && obj.GetState_ObjectId() == TObjectId.eSchComponent)
                    return ((ISch_Component)obj, "PopupMenuHitTest");
            }

            List<ISch_Component> selected = SheetComponents(sheet).Where(c => c.GetState_Selection()).ToList();
            if (selected.Count == 1)
                return (selected[0], "selection");

            throw new UserException($"{file}: no component found (PopupMenuHitTest hit {count} object(s), {selected.Count} " +
                                    "component(s) selected). Right-click a component, pass Designator=<designator>, or select one component.");
        }

        /// <summary>
        /// SourceLibraryName is used as it is when it is a full path. Otherwise it is looked up in each folder of
        /// LibraryFolders=a;b, then in the sheet's folder and its Libraries
        /// subfolder.
        /// </summary>
        private static string ResolveLibrary(CommandContext ctx, ISch_Document sheet, string sourceLibrary, out string searched)
        {
            if (string.IsNullOrWhiteSpace(sourceLibrary))
                throw new UserException("The component has no SourceLibraryName. Nothing was changed.");
            if (!sourceLibrary.EndsWith(".SchLib", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The component's source library is '{sourceLibrary}', not a .SchLib file. Nothing was changed.");

            var candidates = new List<string>();
            if (Path.IsPathRooted(sourceLibrary))
            {
                candidates.Add(sourceLibrary);
            }
            else
            {
                IEnumerable<string> folders = ctx.Get("LibraryFolders", "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(f => f.Trim('"'));
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
                                                    "Pass LibraryFolders=<folder;folder>. Nothing was changed.");
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

        /// <summary>Pins of all parts and display modes, through a child iterator.</summary>
        private static int PinCount(ISch_Component component)
        {
            int count = 0;
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator == null)
                return -1;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.ePin });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    count++;
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return count;
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
            // A schematic library is an ISch_Document too: tell them apart by the object ID.
            if (document.GetState_ObjectId() == TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        // Invariant culture so the pipe output does not depend on the Windows locale. EDP.Utils converts with
        // 10,000 internal units per mil, which matches schematic coordinates.
        private static string Mils(int coord) =>
            EDP.Utils.CoordToMils(coord).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
