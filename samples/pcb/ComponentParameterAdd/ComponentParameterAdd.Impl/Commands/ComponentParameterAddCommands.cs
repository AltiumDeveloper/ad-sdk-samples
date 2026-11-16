using System.Runtime.InteropServices;
using PCB;

namespace AdSamples.ComponentParameterAdd
{
    /// <summary>
    /// Adds (or updates) a named parameter on every component of the current board, through
    /// IPCB_ParametersFactory.CreateParameter and IPCB_PrimitiveParameters.AddOrSetParameter.
    /// </summary>
    internal static class ComponentParameterAddCommands
    {
        private const string DefaultName = "OpenBOMPartNumber";
        private const string DefaultValue = "123456";

        /// <summary>
        /// AddParamToComponent "Name=&lt;text&gt;|Value=&lt;text&gt;": stores the parameter on every component on a
        /// signal layer and returns how many were added, updated and read back with the new value.
        /// </summary>
        [HotCommand("AddParamToComponent", "Add or update a parameter on every component")]
        public static string AddParamToComponent(CommandContext ctx)
        {
            string name = ctx.Get("Name", DefaultName);
            string value = ctx.Get("Value", DefaultValue);
            if (string.IsNullOrEmpty(name))
                throw new UserException("The parameter name is empty. Pass Name=<text> (default OpenBOMPartNumber).");

            IPCB_ServerInterface pcbServer = PCB.GlobalVars.PCBServer
                ?? throw new UserException("The PCB editor is not running. Open a .PcbDoc first.");
            IPCB_Board board = pcbServer.GetCurrentPCBBoard()
                ?? throw new UserException("No PCB document is active. Open and focus a .PcbDoc first.");
            IPCB_ParametersFactory factory = pcbServer.GetParametersFactory();

            int added = 0, updated = 0, readBack = 0, viaFactory = 0;
            pcbServer.PreProcess();
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                // Components on signal layers (top and bottom are signal layers).
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.eComponentObject });
                iterator.AddFilter_LayerSet(PCBConstant.V6SignalLayersSet);
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);

                for (var component = iterator.FirstPCBObject() as IPCB_Component;
                     component != null;
                     component = iterator.NextPCBObject() as IPCB_Component)
                {
                    component.BeginModify();
                    try
                    {
                        // A new IPCB_Parameter per component: AddOrSetParameter copies the
                        // name, value and inherited mark, so the object could also be shared.
                        // argIsInherited = false: an own value, not "inherit from the default variant".
                        // (It is not a visibility switch.)
                        IPCB_Parameter parameter = factory.CreateParameter(name, value, false);

                        bool existed = Store(component, parameter, factory, out IPCB_PrimitiveParameters parameters, ref viaFactory);
                        if (existed) updated++; else added++;

                        // Read back through the same view, so the result shows the change really landed.
                        if (parameters.GetValueByNameDef(name, null) == value)
                            readBack++;
                    }
                    finally
                    {
                        component.EndModify();
                    }

                    if ((added + updated) % 100 == 0)
                        ctx.Log($"{added + updated} components processed...");
                }
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
                pcbServer.PostProcess();
            }

            int processed = added + updated;
            int total = CountAllComponents(board);
            if (processed == 0)
                return $"No components on signal layers ({total} components on the board). Nothing changed.";

            string route = viaFactory == 0 ? "cast" : $"CreatePrimitiveParameters for {viaFactory}";
            return $"{name}='{value}' on {processed} of {total} components: {added} added, {updated} updated, " +
                   $"{readBack} read back with the new value (route: {route}). Save the document to keep it.";
        }

        /// <summary>
        /// Stores the parameter on the component and reports whether a parameter of that name already existed.
        /// </summary>
        private static bool Store(IPCB_Component component, IPCB_Parameter parameter, IPCB_ParametersFactory factory,
                                  out IPCB_PrimitiveParameters parameters, ref int viaFactory)
        {
            // Cast the component to IPCB_PrimitiveParameters; the reference
            // says a primitive "can be cast" and the cast then works on the current variant. In .NET the cast
            // always succeeds (every SDK interface is IDispatch), so an unsupported cast would only show as a
            // COMException on the first call. In that case use the factory's view, also on the current variant
            // (empty variant name), and count it so the result tells which route worked.
            parameters = (IPCB_PrimitiveParameters)component;
            try
            {
                return Apply(parameters, parameter);
            }
            catch (COMException)
            {
                parameters = factory.CreatePrimitiveParameters(component, "");
                viaFactory++;
                return Apply(parameters, parameter);
            }
        }

        private static bool Apply(IPCB_PrimitiveParameters parameters, IPCB_Parameter parameter)
        {
            bool existed = parameters.TryGetValueByName(parameter.GetName(), out _);
            // AddOrSetParameter replaces a parameter of the same name (compared without regard to case), so
            // running the command twice does not create duplicates.
            parameters.AddOrSetParameter(parameter);
            return existed;
        }

        /// <summary>Counts every component without a layer filter, to show whether the signal-layer filter missed any.</summary>
        private static int CountAllComponents(IPCB_Board board)
        {
            int count = 0;
            IPCB_BoardIterator iterator = board.BoardIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new PCB.TObjectSet { PCB.TObjectId.eComponentObject });
                iterator.AddFilter_AllLayers();
                iterator.AddFilter_Method(TIterationMethod.eProcessAll);
                for (var obj = iterator.FirstPCBObject(); obj != null; obj = iterator.NextPCBObject())
                    count++;
            }
            finally
            {
                board.BoardIterator_Destroy(ref iterator);
            }
            return count;
        }
    }
}
