using System.Collections.Generic;
using System.IO;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.CreateSchComponent
{
    /// <summary>
    /// Creates a component with one pin in the current schematic library. The SDK lesson: new schematic objects come
    /// from ISch_ServerInterface.SchObjectFactory and are then added to their owner (the library for a component,
    /// the component for a pin), as in docs/guides/schematic-basics.mdx "Creating objects".
    /// </summary>
    internal static class CreateSchComponentCommands
    {
        /// <summary>
        /// CreateSchComponent ["LibReference=&lt;name&gt;"]: adds a component with one pin at the origin to the
        /// current schematic library, makes it the current component and returns a summary read back from the library.
        /// </summary>
        [HotCommand("CreateSchComponent", "Create a component with one pin in the current schematic library")]
        public static string CreateSchComponent(CommandContext ctx)
        {
            // An optional name. Without it the component keeps whatever name the factory gives it (see README
            // Notes).
            string libReference = ctx.Get("LibReference", "");

            ISch_ServerInterface schServer = SchServer();
            ISch_Lib library = CurrentLibrary(schServer);

            if (libReference.Length > 0 && library.GetState_SchComponentByLibRef(libReference) != null)
                throw new UserException($"The library already has a component named '{libReference}'. Pass another LibReference.");

            int before = CountComponents(library);

            // PreProcess/PostProcess bracket the change and the robot manager is told about the new component, as
            // the docs example IProcessControl.PreProcess does for a new port on a sheet.
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            ISch_Component component;
            processControl.PreProcess(library, "");
            try
            {
                component = schServer.SchObjectFactory(TObjectId.eSchComponent, TObjectCreationMode.eCreate_Default) as ISch_Component
                    ?? throw new UserException("SchObjectFactory did not create a component.");
                component.MoveToXY(0, 0);
                // The library's current part and display mode.
                component.SetState_OwnerPartId(library.GetState_OwnerPartId());
                component.SetState_OwnerPartDisplayMode(library.GetState_OwnerPartDisplayMode());
                if (libReference.Length > 0)
                    component.SetState_LibReference(libReference);

                library.AddSchComponent(component);
                schServer.GetState_RobotManager().SendMessage(library, SCHConstant.BroadCast,
                    SCHConstant.SCHMPrimitiveRegistration, component);

                // Give the pin the component's current part and display mode (a pin belongs to one part of a
                // multi-part component), as the guide does.
                var pin = schServer.SchObjectFactory(TObjectId.ePin, TObjectCreationMode.eCreate_GlobalCopy) as ISch_Pin
                    ?? throw new UserException("SchObjectFactory did not create a pin.");
                pin.MoveToXY(0, 0);
                pin.SetState_OwnerPartId(component.GetState_CurrentPartID());
                pin.SetState_OwnerPartDisplayMode(component.GetState_DisplayMode());
                component.AddSchObject(pin);

                // Show the new component in the library editor and redraw.
                library.SetState_Current_SchComponent(component);
            }
            finally
            {
                processControl.PostProcess(library, "");
            }
            library.GraphicallyInvalidate();

            int after = CountComponents(library);
            ISch_Component current = library.GetState_Current_SchComponent();
            return $"{Path.GetFileName(library.GetState_DocumentName())}: created component '{component.GetState_LibReference()}' " +
                   $"with {CountPins(component)} pin(s); components {before} -> {after}; current component " +
                   $"'{current?.GetState_LibReference()}'. The change is in memory only: save the library to keep it.";
        }

        /// <summary>ListComponents ["Max=&lt;n&gt;"]: the components of the current schematic library (read-only).</summary>
        [HotCommand("ListComponents", "List the components of the current schematic library")]
        public static string ListComponents(CommandContext ctx)
        {
            if (!int.TryParse(ctx.Get("Max", "200"), out int max) || max < 1)
                throw new UserException("Max must be a positive whole number.");

            ISch_Lib library = CurrentLibrary(SchServer());
            var lines = new List<string>();
            ForEachComponent(library, c => lines.Add($"{c.GetState_LibReference()} ({CountPins(c)} pin(s))"));

            var text = new StringBuilder();
            ISch_Component current = library.GetState_Current_SchComponent();
            text.AppendLine($"{Path.GetFileName(library.GetState_DocumentName())}: {lines.Count} component(s), " +
                            $"current '{current?.GetState_LibReference()}'.");
            for (int i = 0; i < lines.Count && i < max; i++)
                text.AppendLine("  " + lines[i]);
            if (lines.Count > max)
                text.AppendLine($"  ... {lines.Count - max} more (raise Max to see them).");
            return text.ToString().TrimEnd();
        }

        private static ISch_ServerInterface SchServer() =>
            SCH.GlobalVars.SchServer ?? throw new UserException("The schematic editor is not running. Open a .SchLib first.");

        private static ISch_Lib CurrentLibrary(ISch_ServerInterface schServer)
        {
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchLib first.");
            // Test the object kind: "as ISch_Lib" succeeds for every schematic document
            // (docs/guides/schematic-basics.mdx), so it would also accept a .SchDoc.
            if (document.GetState_ObjectId() != TObjectId.eSchLib)
                throw new UserException("The active document is not a schematic library. Open and focus a .SchLib first.");
            return (ISch_Lib)document;
        }

        private static int CountComponents(ISch_Lib library)
        {
            int n = 0;
            ForEachComponent(library, _ => n++);
            return n;
        }

        private static void ForEachComponent(ISch_Lib library, System.Action<ISch_Component> action)
        {
            ISch_Iterator iterator = library.SchLibIterator_Create()
                ?? throw new UserException("Could not create a library iterator.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    action((ISch_Component)obj);
            }
            finally
            {
                library.SchIterator_Destroy(ref iterator);
            }
        }

        private static int CountPins(ISch_Component component)
        {
            int n = 0;
            ISch_Iterator iterator = component.SchIterator_Create()
                ?? throw new UserException("Could not create a component iterator.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.ePin });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    n++;
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
            return n;
        }
    }
}
