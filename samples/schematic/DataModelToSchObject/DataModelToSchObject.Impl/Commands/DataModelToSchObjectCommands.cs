using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DXP;
using EDP;
using SCH;

namespace AdSamples.DataModelToSchObject
{
    /// <summary>
    /// The bridge from the compiled data model (IComponent, INet, ILine: connectivity, read-only) to the live,
    /// editable schematic objects (ISch_Component, ISch_Wire, ISch_Bus). Every data model object has a schematic
    /// handle (IDMObject.DM_SchHandle, a string); IUnifiedDataModel.SupportsInterface turns that handle into the
    /// schematic object, whose kind is then checked with ISch_BasicContainer.ObjectId before it is used as a
    /// typed interface.
    /// </summary>
    internal static class DataModelToSchObjectCommands
    {
        // Red is 0x0000FF: colours are BGR integers (docs: ISch_GraphicalObject.Color).
        private const int DefaultColor = 0x0000FF;

        /// <summary>
        /// ListVaultComponents ["Project=&lt;file name or path&gt;|All=true"]: compiles the project, resolves each
        /// component of each schematic document to its ISch_Component and lists those with a VaultGUID. All=true also
        /// lists the components without one, so the bridge can be checked on designs without vault components.
        /// </summary>
        [HotCommand("ListVaultComponents", "List the vault components of the focused project via their schematic objects")]
        public static string ListVaultComponents(CommandContext ctx)
        {
            bool all = ParseBool("All", ctx.Get("All", "false"));
            IProject project = FindProject(ctx.Get("Project"));
            string compileText = Compile(ctx, project);

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            var lines = new List<string>();
            var tally = new Tally();
            int sheets = 0, vault = 0;

            // All access to the compiled model, including the handle lookups, inside one read bracket.
            // Reading properties of the resolved schematic objects is done inside it too.
            dataModel.BeginRead();
            try
            {
                int documentCount = project.DM_PhysicalDocumentCount();
                for (int j = 0; j < documentCount; j++)
                {
                    IDocument document = project.DM_PhysicalDocuments(j);
                    if (document == null || !string.Equals(document.DM_DocumentKind(), "SCH", StringComparison.OrdinalIgnoreCase))
                        continue;

                    sheets++;
                    int componentCount = document.DM_ComponentCount();
                    lines.Add($"{document.DM_FileName()}: {componentCount} component(s)");

                    for (int k = 0; k < componentCount; k++)
                    {
                        IComponent component = document.DM_Components(k);
                        if (component == null)
                            continue;

                        ISch_BasicContainer obj = Resolve(dataModel, component.DM_SchHandle(), out SCH.TObjectId kind, tally);
                        if (obj == null)
                            continue;
                        if (kind != SCH.TObjectId.eSchComponent)
                        {
                            tally.OtherKind++;
                            continue;
                        }

                        // A cast after the ObjectId check gives the typed interface.
                        var schComponent = (ISch_Component)obj;
                        tally.Typed++;
                        string guid = schComponent.GetState_VaultGUID();
                        bool isVault = !string.IsNullOrEmpty(guid);
                        if (isVault)
                            vault++;
                        if (isVault || all)
                            lines.Add($"  {component.DM_PhysicalDesignator()}: " +
                                      (isVault ? $"vault component, VaultGUID={guid}" : "not a vault component") +
                                      $", LibReference={schComponent.GetState_LibReference()}");
                    }
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            var text = new StringBuilder();
            text.AppendLine($"{project.DM_ProjectFileName()}: {compileText}; {sheets} schematic document(s), " +
                            $"{tally.Handles} component handle(s), {tally.Resolved} resolved, {tally.Typed} ISch_Component, " +
                            $"{tally.OtherKind} other kind, {tally.Unresolved} not resolved; {vault} vault component(s).");
            if (tally.Unresolved > 0)
                text.AppendLine(UnresolvedHint);
            foreach (string line in lines)
                text.AppendLine(line);
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// RecolorNetObjects "Net=&lt;name&gt;|Color=&lt;BGR&gt;|Project=&lt;file name or path&gt;": compiles the project,
        /// resolves every line of the named net to its ISch_Wire or ISch_Bus and sets its Color. Returns each
        /// object's previous colour, so running it again with Color=&lt;previous&gt; puts it back.
        /// </summary>
        [HotCommand("RecolorNetObjects", "Recolour the schematic wires and buses of a compiled net")]
        public static string RecolorNetObjects(CommandContext ctx)
        {
            string netName = ctx.Get("Net");
            if (string.IsNullOrWhiteSpace(netName))
                throw new UserException("No net specified. Pass it as a parameter: Net=<net name>, for example Net=LEDS[7..0].");
            int color = ParseColor(ctx.Get("Color"));

            IProject project = FindProject(ctx.Get("Project"));
            string compileText = Compile(ctx, project);

            IUnifiedDataModel dataModel = project.DM_DataModel()
                ?? throw new UserException("The project has no data model. Compile the project first.");

            // Phase 1, inside the read bracket: walk the compiled nets and collect the schematic objects.
            var targets = new List<Target>();
            var tally = new Tally();
            var netNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int matchingNets = 0, duplicates = 0;

            dataModel.BeginRead();
            try
            {
                int documentCount = project.DM_PhysicalDocumentCount();
                for (int j = 0; j < documentCount; j++)
                {
                    IDocument document = project.DM_PhysicalDocuments(j);
                    if (document == null || !string.Equals(document.DM_DocumentKind(), "SCH", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int netCount = document.DM_NetCount();
                    for (int l = 0; l < netCount; l++)
                    {
                        INet net = document.DM_Nets(l);
                        if (net == null)
                            continue;
                        string name = net.DM_NetName();
                        netNames.Add(name);
                        if (!string.Equals(name, netName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        matchingNets++;
                        int lineCount = net.DM_LineCount();
                        for (int k = 0; k < lineCount; k++)
                        {
                            ILine line = net.DM_Lines(k);
                            if (line == null)
                                continue;

                            ISch_BasicContainer obj = Resolve(dataModel, line.DM_SchHandle(), out SCH.TObjectId kind, tally);
                            if (obj == null)
                                continue;
                            // ISch_Bus derives from ISch_Wire, so both are set through ISch_Wire.Color; the kind is
                            // kept for the report.
                            if (kind != SCH.TObjectId.eWire && kind != SCH.TObjectId.eBus)
                            {
                                tally.OtherKind++;
                                continue;
                            }
                            // Two lines of one net can resolve to the same bus (SpiritLevel, LEDS[7..0] on
                            // SL_LCD_SW_LED_2E.SchDoc). Without this check the bus would be recoloured twice and
                            // its second "previous" colour would already be the new one.
                            string handle = line.DM_SchHandle();
                            if (targets.Any(t => t.Handle == handle || ReferenceEquals(t.Wire, obj)))
                            {
                                duplicates++;
                                ctx.Log($"Line {k} of '{name}' on {document.DM_FileName()} resolves to an object already listed (handle {handle}).");
                                continue;
                            }
                            tally.Typed++;
                            targets.Add(new Target((ISch_Wire)obj, kind, handle, document.DM_FullPath(), document.DM_FileName()));
                        }
                    }
                }
            }
            finally
            {
                dataModel.EndRead();
            }

            if (matchingNets == 0)
                throw new UserException($"No net named '{netName}' on the schematic documents of {project.DM_ProjectFileName()}. " +
                                        "Nets with a similar name: " + SimilarNames(netNames, netName) + ".");

            // Phase 2, after EndRead: change the schematic objects the editor's way (docs: Working with schematics,
            // "Changing objects"), with the editor notified of each change.
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running.");
            ISch_RobotManager robot = schServer.GetState_RobotManager();
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();

            var report = new List<string>();
            foreach (IGrouping<string, Target> sheetTargets in targets.GroupBy(t => t.SheetPath, StringComparer.OrdinalIgnoreCase))
            {
                ISch_Document sheet = schServer.GetSchDocumentByPath(sheetTargets.Key);
                if (sheet != null)
                    processControl.PreProcess(sheet, "");
                try
                {
                    foreach (Target target in sheetTargets)
                    {
                        ISch_Wire wire = target.Wire;
                        int previous = wire.GetState_Color();
                        robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                        wire.SetState_Color(color);
                        robot.SendMessage(wire, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                        wire.GraphicallyInvalidate();
                        report.Add($"  {target.SheetName}: {(target.Kind == SCH.TObjectId.eBus ? "bus" : "wire")} " +
                                   $"Color {Hex(previous)} -> {Hex(wire.GetState_Color())}");
                    }
                }
                finally
                {
                    if (sheet != null)
                        processControl.PostProcess(sheet, "");
                }
            }

            int wires = targets.Count(t => t.Kind == SCH.TObjectId.eWire);
            int buses = targets.Count - wires;
            var text = new StringBuilder();
            text.AppendLine($"{project.DM_ProjectFileName()}: {compileText}; net '{netName}' found {matchingNets} time(s), " +
                            $"{tally.Handles} line handle(s), {tally.Resolved} resolved, {tally.OtherKind} other kind, " +
                            $"{tally.Unresolved} not resolved, {duplicates} on an object already listed; {wires} wire(s) and {buses} bus(es) set to {Hex(color)}.");
            if (tally.Unresolved > 0)
                text.AppendLine(UnresolvedHint);
            foreach (string line in report)
                text.AppendLine(line);
            text.Append(targets.Count == 0
                ? "Nothing changed."
                : "The change is in memory only: save the documents to keep it, or run again with Color=<previous colour>.");
            return text.ToString();
        }

        // ---- the bridge ------------------------------------------------------------------------------------------

        /// <summary>
        /// DM_SchHandle -> IUnifiedDataModel.SupportsInterface -> ISch_BasicContainer, with its ObjectId. Returns null
        /// (and counts why) when the handle does not resolve to a schematic object.
        /// </summary>
        private static ISch_BasicContainer Resolve(IUnifiedDataModel dataModel, string handle, out SCH.TObjectId kind, Tally tally)
        {
            kind = default;
            tally.Handles++;
            // SupportsInterface returns false for objects on sheets that are
            // not open in an editor.
            if (string.IsNullOrEmpty(handle) || !dataModel.SupportsInterface(handle, out object obj) || obj == null)
            {
                tally.Unresolved++;
                return null;
            }

            // The out parameter is a plain object, used as ISch_BasicContainer; a failed cast or
            // a failing first call is counted as not resolved. ObjectId then tells which typed interface applies
            // (eSchComponent, eWire, eBus, ...).
            var container = obj as ISch_BasicContainer;
            if (container == null)
            {
                tally.Unresolved++;
                return null;
            }
            try
            {
                kind = container.GetState_ObjectId();
            }
            catch (COMException)
            {
                tally.Unresolved++;
                return null;
            }
            tally.Resolved++;
            return container;
        }

        private const string UnresolvedHint =
            "Handles resolve only for sheets open in an editor: open the project's sheets to resolve all objects.";

        private sealed class Tally
        {
            public int Handles, Resolved, Unresolved, OtherKind, Typed;
        }

        private sealed class Target
        {
            public Target(ISch_Wire wire, SCH.TObjectId kind, string handle, string sheetPath, string sheetName)
            {
                Wire = wire;
                Kind = kind;
                Handle = handle;
                SheetPath = sheetPath;
                SheetName = sheetName;
            }

            public ISch_Wire Wire { get; }
            public SCH.TObjectId Kind { get; }
            public string Handle { get; }
            public string SheetPath { get; }
            public string SheetName { get; }
        }

        // ---- project, compile, parameters ------------------------------------------------------------------------

        /// <summary>The project named by Project (file name or full path of an open project), else the focused one.</summary>
        private static IProject FindProject(string name)
        {
            var workspace = DXP.GlobalVars.Client.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("The workspace is not available.");

            IProject project;
            if (string.IsNullOrWhiteSpace(name))
            {
                project = workspace.DM_FocusedProject()
                    ?? throw new UserException("No project is focused. Open a schematic project and focus one of its documents.");
            }
            else
            {
                project = null;
                int count = workspace.DM_ProjectCount();
                for (int i = 0; i < count && project == null; i++)
                {
                    IProject candidate = workspace.DM_Projects(i);
                    if (candidate != null &&
                        (string.Equals(candidate.DM_ProjectFullPath(), name, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetFileName(candidate.DM_ProjectFileName()), Path.GetFileName(name), StringComparison.OrdinalIgnoreCase)))
                        project = candidate;
                }
                if (project == null)
                    throw new UserException($"No open project named '{name}'.");
            }

            // Free documents belong to a virtual project that cannot be compiled into nets and components.
            if (string.Equals(Path.GetFileNameWithoutExtension(project.DM_ProjectFileName()), "Free Documents", StringComparison.OrdinalIgnoreCase))
                throw new UserException("The focused document is a free document. Focus a document of a schematic project.");
            return project;
        }

        private static string Compile(CommandContext ctx, IProject project)
        {
            // Components, nets and lines exist only in the compiled model. Both results of
            // DM_CompileEx are reported; the meaning of the first argument is not documented.
            ctx.Log("Compiling " + project.DM_ProjectFileName() + " ...");
            bool cancelled = false;
            bool result = project.DM_CompileEx(true, ref cancelled);
            return $"DM_CompileEx returned {result}, cancelled={cancelled}";
        }

        /// <summary>Decimal (255) or hexadecimal with 0x or # (0x0000FF), a BGR value.</summary>
        private static int ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultColor;
            string v = value.Trim();
            bool ok;
            int color;
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || v.StartsWith("#", StringComparison.Ordinal))
                ok = int.TryParse(v.Substring(v[0] == '#' ? 1 : 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);
            else
                ok = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out color);
            if (!ok || color < 0 || color > 0xFFFFFF)
                throw new UserException($"Color='{value}' is not a BGR colour: use a decimal or 0x-hex value up to 0xFFFFFF (0x0000FF is red).");
            return color;
        }

        private static bool ParseBool(string name, string value)
        {
            if (bool.TryParse(value, out bool result))
                return result;
            throw new UserException($"{name}='{value}' is not true or false.");
        }

        private static string SimilarNames(IEnumerable<string> names, string wanted)
        {
            // A bus net such as LEDS[7..0] may be listed under its member names (LEDS0...): match on the prefix too.
            string stem = new string(wanted.TakeWhile(char.IsLetterOrDigit).ToArray());
            List<string> similar = names
                .Where(n => n.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (stem.Length > 0 && n.StartsWith(stem, StringComparison.OrdinalIgnoreCase)))
                .Take(20).ToList();
            if (similar.Count == 0)
                similar = names.Take(20).ToList();
            return similar.Count == 0 ? "(no nets)" : string.Join(", ", similar);
        }

        private static string Hex(int bgr) => "0x" + bgr.ToString("X6", CultureInfo.InvariantCulture);
    }
}
