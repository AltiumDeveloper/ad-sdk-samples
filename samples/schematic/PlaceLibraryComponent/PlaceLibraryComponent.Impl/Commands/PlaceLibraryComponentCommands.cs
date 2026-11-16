using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.PlaceLibraryComponent
{
    /// <summary>
    /// Places a component from a schematic library on the active sheet with
    /// SCH.IIntegratedLibraryManager.PlaceLibraryComponent(libReference, libraryPath, parameters).
    /// Everything beyond "which component from which library" travels in the third argument, an AD style
    /// "Key=Value|Key=Value" string: location, part number, models, parameters. The two commands differ only in that
    /// string, which is why both are in one sample.
    /// </summary>
    internal static class PlaceLibraryComponentCommands
    {
        /// <summary>
        /// PlaceComponent "Lib=&lt;.SchLib/.IntLib&gt;|LibRef=&lt;name&gt;|Models=&lt;footprint@pcblib;...&gt;|Params=&lt;Name=Value;...&gt;|X=&lt;mils&gt;|Y=&lt;mils&gt;|Interactive":
        /// places the component with extra PCB footprint models and parameters, and lists what appeared on the sheet.
        /// </summary>
        [HotCommand("PlaceComponent", "Place a library component with models and parameters")]
        public static string PlaceComponent(CommandContext ctx)
        {
            string libRef = Required(ctx, "LibRef", "RES2");
            string library = LibraryParam(ctx);
            Location location = LocationParam(ctx);

            var parameters = new List<KeyValuePair<string, string>>
            {
                // Not described in the SDK reference; accepted with no visible effect and no dialog.
                Pair("PlacingFromDatabase", "true"),
            };
            parameters.AddRange(ModelEntries(ctx.Get("Models")));
            parameters.AddRange(ParameterEntries(ctx.Get("Params", DemoParameters)));
            parameters.AddRange(location.Entries());

            return Place(ctx, libRef, library, parameters, location);
        }

        /// <summary>
        /// PlacePart "Lib=&lt;.SchLib/.IntLib&gt;|LibRef=&lt;name&gt;|PartID=&lt;n&gt;|X=&lt;mils&gt;|Y=&lt;mils&gt;|Interactive":
        /// places one part of a multi-part component.
        /// </summary>
        [HotCommand("PlacePart", "Place one part of a multi-part component")]
        public static string PlacePart(CommandContext ctx)
        {
            string libRef = Required(ctx, "LibRef", "XC2S300E-6PQ208C");
            string library = LibraryParam(ctx);
            int partId = IntParam(ctx, "PartID", 2);
            if (partId < 1)
                throw new UserException($"PartID={partId}: part numbers start at 1 (part A).");
            Location location = LocationParam(ctx);

            // PartID (1-based) is the only key besides the location. It is not described in the SDK reference; the returned
            // summary shows the CurrentPartID of the placed component so the effect can be checked.
            var parameters = new List<KeyValuePair<string, string>>
            {
                Pair("PartID", partId.ToString(CultureInfo.InvariantCulture)),
            };
            parameters.AddRange(location.Entries());

            return Place(ctx, libRef, library, parameters, location);
        }

        /// <summary>Calls PlaceLibraryComponent and reports the components that appeared on the active sheet.</summary>
        private static string Place(CommandContext ctx, string libRef, string library,
                                    List<KeyValuePair<string, string>> parameters, Location location)
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            // The .NET way to get the integrated library manager (as in the docs' example).
            IIntegratedLibraryManager manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new UserException("The integrated library manager is not available (LoadIntegratedLibraryManager returned null).");
            string resolved = ResolveLibrary(manager, library);

            // PlaceLibraryComponent reports only true/false, so the sheet's components are listed before and after.
            List<string> before = Components(sheet);

            string parameterText = CommandContext.FormatParameters(parameters);
            ctx.Log($"PlaceLibraryComponent('{libRef}', '{resolved}', '{parameterText}')");
            // No SCHMBeginModify/EndModify bracket: the manager adds the component itself and the sheet was
            // marked modified without one.
            bool result = manager.PlaceLibraryComponent(libRef, resolved, parameterText);
            sheet.GraphicallyInvalidate();

            // Re-query the sheet: the call may have changed which document is current.
            ISch_Document after = schServer.GetCurrentSchDocument() ?? sheet;
            List<string> added = Added(before, Components(after));

            var text = new StringBuilder();
            text.Append($"PlaceLibraryComponent('{libRef}', '{Path.GetFileName(resolved)}') on {file} returned {result}. ");
            text.Append($"Parameters: {parameterText}. ");
            if (added.Count > 0)
            {
                text.Append($"{added.Count} new component(s): ").Append(string.Join("; ", added)).Append('.');
            }
            else
            {
                text.Append($"No new component on the sheet ({before.Count} before and after).");
                if (location.Interactive)
                    text.Append(" Interactive placement: the component follows the cursor until you click, so it is" +
                                " not on the sheet yet when the command returns.");
            }
            return text.ToString();
        }

        // ---- the parameter string --------------------------------------------------------------------------------

        /// <summary>
        /// Demo component parameters, used when Params is not passed.
        /// </summary>
        private const string DemoParameters = "PartNumber=931H451;Value=100R;Power=1/4W;Tolerance=5%";

        /// <summary>
        /// "footprint@pcblib path;..." to ModelTypeN=PCBLIB|ModelNameN=footprint|LocationN=path (N from 0).
        /// None of these keys is described in the SDK reference.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> ModelEntries(string models)
        {
            var entries = new List<KeyValuePair<string, string>>();
            int index = 0;
            foreach (string item in SplitList(models))
            {
                int at = item.IndexOf('@');
                if (at <= 0 || at == item.Length - 1)
                    throw new UserException($"Models entry '{item}' is not <footprint>@<path to .PcbLib>.");
                string name = item.Substring(0, at).Trim();
                string path = item.Substring(at + 1).Trim();
                // A missing model library is not an error AD reports back; check it here so the result is clear.
                if (Path.IsPathRooted(path) && !File.Exists(path))
                    throw new UserException($"Models entry '{item}': the file '{path}' does not exist.");
                string n = index.ToString(CultureInfo.InvariantCulture);
                entries.Add(Pair("ModelType" + n, "PCBLIB"));
                entries.Add(Pair("ModelName" + n, name));
                entries.Add(Pair("Location" + n, path));
                index++;
            }
            return entries;
        }

        /// <summary>
        /// "Name=Value;..." to ParameterNameN|ParameterValueN|ParameterVisibleN=False (N from 0),
        /// so every added parameter is hidden. An empty Params= passes no parameters.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> ParameterEntries(string list)
        {
            var entries = new List<KeyValuePair<string, string>>();
            int index = 0;
            foreach (string item in SplitList(list))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0)
                    throw new UserException($"Params entry '{item}' is not <Name>=<Value>.");
                string n = index.ToString(CultureInfo.InvariantCulture);
                entries.Add(Pair("ParameterName" + n, item.Substring(0, eq).Trim()));
                entries.Add(Pair("ParameterValue" + n, item.Substring(eq + 1).Trim()));
                entries.Add(Pair("ParameterVisible" + n, "False"));
                index++;
            }
            return entries;
        }

        /// <summary>Where to place: Location.X/Location.Y in internal units, or nothing for cursor placement.</summary>
        private sealed class Location
        {
            public bool Interactive;
            public double XMils;
            public double YMils;

            public IEnumerable<KeyValuePair<string, string>> Entries()
            {
                if (Interactive)
                    yield break;
                // The reference's description of argParameters: "Normally you will need Location.X and Location.Y".
                // Its example passes internal units (10000000 = 1000 mil), so the mils are converted with MilsToCoord.
                yield return Pair("Location.X", EDP.Utils.MilsToCoord(XMils).ToString(CultureInfo.InvariantCulture));
                yield return Pair("Location.Y", EDP.Utils.MilsToCoord(YMils).ToString(CultureInfo.InvariantCulture));
            }
        }

        private static Location LocationParam(CommandContext ctx)
        {
            if (ctx.Flag("Interactive"))
            {
                // Without a location AD is expected to attach the component to the cursor.
                // That waits for a mouse click, which must never happen on the pipe path: nobody is there to click.
                if (!ctx.IsInteractive)
                    throw new UserException("Interactive placement waits for a mouse click in AD; it is refused on the pipe. " +
                                            "Pass X=<mils>|Y=<mils> instead.");
                return new Location { Interactive = true };
            }
            return new Location { XMils = MilsParam(ctx, "X", 1000), YMils = MilsParam(ctx, "Y", 1000) };
        }

        // ---- the library ---------------------------------------------------------------------------------------

        private static string LibraryParam(CommandContext ctx)
        {
            string library = ctx.Get("Lib");
            if (string.IsNullOrWhiteSpace(library))
                throw new UserException("Pass Lib=<path to a .SchLib or .IntLib>, or the file name of an installed library " +
                                        "(for example Lib=Miscellaneous Devices.IntLib).");
            library = library.Trim().Trim('"');
            string extension = Path.GetExtension(library);
            if (!extension.Equals(".SchLib", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".IntLib", StringComparison.OrdinalIgnoreCase))
                throw new UserException($"Lib='{library}': expected a .SchLib or .IntLib file.");
            return library;
        }

        /// <summary>
        /// A full path must exist; a bare file name must match an installed library. The call is not made with a
        /// library AD cannot find, because what PlaceLibraryComponent does then (dialog, false, nothing) is not documented.
        /// </summary>
        private static string ResolveLibrary(IIntegratedLibraryManager manager, string library)
        {
            if (Path.IsPathRooted(library))
            {
                if (!File.Exists(library))
                    throw new UserException($"Lib='{library}' does not exist.");
                return library;
            }

            int count = manager.InstalledLibraryCount();
            for (int i = 0; i < count; i++)
            {
                string installed = manager.InstalledLibraryPath(i);
                if (string.Equals(Path.GetFileName(installed), library, StringComparison.OrdinalIgnoreCase))
                    return library; // pass the name, as the docs' example does
            }
            throw new UserException($"Lib='{library}' is neither a full path nor the file name of one of the " +
                                    $"{count} installed libraries.");
        }

        // ---- reading the sheet back ----------------------------------------------------------------------------

        /// <summary>One description per component (part) on the sheet: designator, lib reference, part, location, models, parameters.</summary>
        private static List<string> Components(ISch_Document sheet)
        {
            var result = new List<string>();
            ISch_Iterator iterator = sheet.SchIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });
                for (var component = iterator.FirstSchObject() as ISch_Component; component != null;
                     component = iterator.NextSchObject() as ISch_Component)
                {
                    Point location = component.GetState_Location();
                    result.Add($"{component.GetState_SchDesignator()?.GetState_Text()} '{component.GetState_LibReference()}' " +
                               $"part {component.GetState_CurrentPartID()} of {component.GetState_PartCountNoPart0()} " +
                               $"at ({Mils(EDP.Utils.CoordToMils(location.X))}, {Mils(EDP.Utils.CoordToMils(location.Y))}) mils, " +
                               $"models [{Models(component)}], parameters [{Parameters(component)}]");
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return result;
        }

        private static string Models(ISch_Component component)
        {
            var models = new List<string>();
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator == null)
                return string.Empty;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eImplementation });
                for (var model = iterator.FirstSchObject() as ISch_Implementation; model != null;
                     model = iterator.NextSchObject() as ISch_Implementation)
                    models.Add($"{model.GetState_ModelType()}:{model.GetState_ModelName()}{(model.GetState_IsCurrent() ? "*" : "")}");
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return string.Join(", ", models);
        }

        private static string Parameters(ISch_Component component)
        {
            var parameters = new List<string>();
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator == null)
                return string.Empty;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eParameter });
                for (var parameter = iterator.FirstSchObject() as ISch_Parameter; parameter != null;
                     parameter = iterator.NextSchObject() as ISch_Parameter)
                    parameters.Add($"{parameter.GetState_Name()}={parameter.GetState_Text()}{(parameter.GetState_IsHidden() ? " (hidden)" : "")}");
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return string.Join(", ", parameters);
        }

        /// <summary>The entries of <paramref name="after"/> that are not in <paramref name="before"/> (multiset difference).</summary>
        private static List<string> Added(List<string> before, List<string> after)
        {
            var remaining = new List<string>(before);
            var added = new List<string>();
            foreach (string entry in after)
            {
                if (!remaining.Remove(entry))
                    added.Add(entry);
            }
            return added;
        }

        // ---- small helpers ---------------------------------------------------------------------------------------

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        private static IEnumerable<string> SplitList(string list) =>
            (list ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static string Required(CommandContext ctx, string name, string defaultValue)
        {
            string value = ctx.Get(name, defaultValue)?.Trim();
            if (string.IsNullOrEmpty(value))
                throw new UserException($"{name} is empty. Pass {name}=<component name in the library>.");
            return value;
        }

        private static string Mils(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static double MilsParam(CommandContext ctx, string name, double defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrEmpty(text))
                return defaultValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                throw new UserException($"{name}='{text}' is not a non-negative number of mils.");
            return value;
        }

        private static int IntParam(CommandContext ctx, string name, int defaultValue)
        {
            string text = ctx.Get(name);
            if (string.IsNullOrEmpty(text))
                return defaultValue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new UserException($"{name}='{text}' is not a whole number.");
            return value;
        }
    }
}
