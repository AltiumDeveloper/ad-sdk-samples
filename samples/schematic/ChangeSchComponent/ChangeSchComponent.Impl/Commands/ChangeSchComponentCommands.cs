using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DXP;
using EDP;
using SCH;

namespace AdSamples.ChangeSchComponent
{
    /// <summary>
    /// Replaces the components of the current schematic sheet with a component loaded from a library. The SDK
    /// lesson: ISch_ServerInterface loads a library component as a free object (LoadComponentFromLibrary or
    /// LoadComponent), which is then added to the sheet with AddSchObject, positioned, and announced to the editor
    /// through the robot manager; the old component is taken off the sheet with RemoveSchObject.
    /// </summary>
    internal static class ChangeSchComponentCommands
    {
        // The defaults pass the library by file name. A bare file name does not load from a library that is not
        // installed (README Notes); pass the library's full path then.
        private const string DefaultLibRef = "Battery";
        private const string DefaultLibrary = "Miscellaneous Devices.IntLib";

        /// <summary>
        /// ChangeSchComponent ["LibRef=&lt;name&gt;|Library=&lt;.IntLib/.SchLib name or path&gt;|Loader=Library|FullPath|Any|NameNoType|NameWithType|Max=&lt;n&gt;"]:
        /// replaces the components of the current sheet with the library component, each at the old component's
        /// location, and returns what was replaced plus a read-back of the sheet.
        /// </summary>
        [HotCommand("ChangeSchComponent", "Replace every component on the current sheet with a library component")]
        public static string ChangeSchComponent(CommandContext ctx)
        {
            string libRef = ctx.Get("LibRef", DefaultLibRef).Trim();
            string library = ctx.Get("Library", DefaultLibrary).Trim();
            string loader = ctx.Get("Loader", "Library").Trim();
            int max = ParseMax(ctx.Get("Max", int.MaxValue.ToString(CultureInfo.InvariantCulture)));
            if (libRef.Length == 0)
                throw new UserException("LibRef is empty. Pass the library reference of the new component, e.g. LibRef=Battery.");
            if (library.Length == 0)
                throw new UserException("Library is empty. Pass the library file name or full path, e.g. Library=Miscellaneous Devices.IntLib.");
            // A rooted path that doesn't exist can only fail; say so before touching the sheet.
            if (Path.IsPathRooted(library) && !File.Exists(library))
                throw new UserException($"Library file not found: {library}");

            ISch_ServerInterface schServer = SchServer();
            ISch_Document sheet = CurrentSheet(schServer);
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            // Collect first, change afterwards. Removing a component while an iterator is still walking the
            // sheet changes the container under the iterator.
            List<ISch_Component> oldComponents = SheetComponents(sheet);
            if (oldComponents.Count == 0)
                return $"{file}: no components on the sheet, nothing changed.";

            // Load one replacement before changing anything, so a wrong LibRef/Library leaves the sheet untouched.
            ISch_Component first = Load(schServer, loader, libRef, library)
                ?? throw new UserException($"Could not load '{libRef}' from '{library}' (Loader={loader}). The sheet is unchanged. " +
                                           "Check the names, pass the library's full path, or try another Loader.");

            ISch_RobotManager robot = schServer.GetState_RobotManager();
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            var replaced = new List<string>();
            string stopReason = null;

            // PreProcess/PostProcess bracket the whole replacement, as the docs example IProcessControl.PreProcess
            // does for a new object on a sheet.
            processControl.PreProcess(sheet, "");
            try
            {
                for (int i = 0; i < oldComponents.Count && i < max; i++)
                {
                    ISch_Component newComponent = i == 0 ? first : Load(schServer, loader, libRef, library);
                    if (newComponent == null)
                    {
                        stopReason = $"loading '{libRef}' failed for component #{i + 1}; the rest are unchanged.";
                        break;
                    }

                    ISch_Component old = oldComponents[i];
                    // Read what the report needs before the old component leaves the sheet.
                    DXP.Point location = old.GetState_Location();
                    int x = location.X, y = location.Y;
                    string before = $"{Designator(old)} '{old.GetState_LibReference()}'";

                    // Removal: ISch_RobotManager.SendMessage's reference names SCHMPrimitiveRegistration for a removed
                    // object (that table is not confirmed in every case).
                    sheet.RemoveSchObject(old);
                    robot.SendMessage(sheet, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, old);

                    // Addition: add, move to the old origin, recompute the size, register.
                    sheet.AddSchObject(newComponent);
                    newComponent.MoveToXY(x, y);
                    newComponent.SetState_xSizeySize();
                    robot.SendMessage(sheet, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, newComponent);
                    newComponent.GraphicallyInvalidate();

                    replaced.Add($"{before} at ({x}, {y}) -> '{newComponent.GetState_LibReference()}'");
                    if ((i + 1) % 100 == 0)
                        ctx.Log($"{i + 1} components replaced...");
                }
            }
            finally
            {
                processControl.PostProcess(sheet, "");
            }
            sheet.GraphicallyInvalidate();

            // Read back from the sheet rather than trusting the loop.
            List<ISch_Component> after = SheetComponents(sheet);
            int matching = after.Count(c => string.Equals(c.GetState_LibReference(), libRef, StringComparison.OrdinalIgnoreCase));

            var text = new StringBuilder();
            text.AppendLine($"{file}: replaced {replaced.Count} of {oldComponents.Count} component(s) with '{libRef}' from '{library}' (Loader={loader}).");
            foreach (string line in replaced.Take(10))
                text.AppendLine("  " + line);
            if (replaced.Count > 10)
                text.AppendLine($"  ... {replaced.Count - 10} more.");
            if (stopReason != null)
                text.AppendLine("Stopped: " + stopReason);
            text.AppendLine($"Read back: {after.Count} component(s) on the sheet, {matching} with LibReference '{libRef}'.");
            text.Append("The change is in memory only: close the sheet without saving to discard it.");
            return text.ToString();
        }

        /// <summary>ListSheetComponents ["Max=&lt;n&gt;"]: designator, LibReference, library and location of each component (read-only).</summary>
        [HotCommand("ListSheetComponents", "List the components on the current sheet")]
        public static string ListSheetComponents(CommandContext ctx)
        {
            int max = ParseMax(ctx.Get("Max", "200"));
            ISch_Document sheet = CurrentSheet(SchServer());
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            List<ISch_Component> components = SheetComponents(sheet);
            if (components.Count == 0)
                return $"{file}: no components.";

            var text = new StringBuilder();
            text.Append($"{file}: {components.Count} component(s)");
            text.AppendLine(components.Count > max ? $", first {max} shown:" : ":");
            foreach (ISch_Component c in components.Take(max))
            {
                DXP.Point location = c.GetState_Location();
                text.AppendLine($"  {Designator(c)} LibReference='{c.GetState_LibReference()}' DesignItemId='{c.GetState_DesignItemId()}' " +
                                $"SourceLibrary='{c.GetState_SourceLibraryName()}' at ({location.X}, {location.Y})");
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Loads one copy of the component. "Library" is LoadComponentFromLibrary(libReference, libraryName); the
        /// other values call LoadComponent with that TLibIdentifierKind and the LibRef as design item ID.
        /// LoadComponentFromDatabaseLibrary in the .NET API takes a database table and keys
        /// (a .DbLib loader), so it is not offered here.
        /// </summary>
        private static ISch_Component Load(ISch_ServerInterface schServer, string loader, string libRef, string library)
        {
            if (loader.Equals("Library", StringComparison.OrdinalIgnoreCase))
                return schServer.LoadComponentFromLibrary(libRef, library);
            if (!Enum.TryParse("eLibIdentifierKind_" + loader, true, out TLibIdentifierKind kind) ||
                !Enum.IsDefined(typeof(TLibIdentifierKind), kind))
                throw new UserException($"Loader must be Library, FullPath, Any, NameNoType or NameWithType, got '{loader}'.");
            return schServer.LoadComponent(kind, library, libRef);
        }

        private static List<ISch_Component> SheetComponents(ISch_Document sheet)
        {
            var list = new List<ISch_Component>();
            ISch_Iterator iterator = sheet.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the current sheet.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });
                // The filter guarantees components; a type test would not help (it succeeds for every SDK object).
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    list.Add((ISch_Component)obj);
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return list;
        }

        private static string Designator(ISch_Component component)
        {
            string text = component.GetState_SchDesignator()?.GetState_Text();
            return string.IsNullOrEmpty(text) ? "(no designator)" : text;
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
            if (document.GetState_ObjectId() == SCH.TObjectId.eSchLib)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static int ParseMax(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) || max < 1)
                throw new UserException($"Max must be a positive integer, got '{value}'.");
            return max;
        }
    }
}
