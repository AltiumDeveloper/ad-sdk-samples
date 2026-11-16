using System;
using System.Collections.Generic;
using System.Globalization;
using SCH;

namespace AdSamples.ComponentManagerPlaceComponent
{
    /// <summary>
    /// The schematic half of the sample: stamp a placed component with the server's metadata as parameters and
    /// link the downloaded footprint as its current PCB model.
    /// </summary>
    internal static class SchPlacement
    {
        public const string IdParameter = "ComponentManager_ID";
        private const string PcbModelType = "PCBLIB";

        /// <summary>A component on the sheet, described for the before/after comparison and the result text.</summary>
        internal sealed class ComponentInfo
        {
            public ISch_Component Component;
            public string Handle;
            public string Designator;
            public string LibReference;
            public string DesignItemId;
            public string SourceLibraryName;
            public string CurrentFootprint;
            public bool HasIdParameter;
        }

        public static List<ComponentInfo> ComponentsOf(ISch_Document sheet)
        {
            var result = new List<ComponentInfo>();
            ISch_Iterator iterator = sheet.SchIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });
                for (var component = iterator.FirstSchObject() as ISch_Component; component != null;
                     component = iterator.NextSchObject() as ISch_Component)
                {
                    result.Add(new ComponentInfo
                    {
                        Component = component,
                        Handle = component.GetState_Handle(),
                        Designator = component.GetState_SchDesignator()?.GetState_Text(),
                        LibReference = component.GetState_LibReference(),
                        DesignItemId = component.GetState_DesignItemId(),
                        SourceLibraryName = component.GetState_SourceLibraryName(),
                        CurrentFootprint = CurrentPcbModel(component)?.GetState_ModelName(),
                        HasIdParameter = FindParameter(component, IdParameter) != null,
                    });
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref iterator);
            }
            return result;
        }

        /// <summary>
        /// Update the parameter of that name (case-insensitive), or create it.
        /// Returns true if it was created. The caller brackets the component with SCHMBeginModify/SCHMEndModify.
        /// </summary>
        public static bool AddOrSetParameter(ISch_ServerInterface schServer, ISch_RobotManager robot, ISch_Component component,
                                             string name, string type, string value)
        {
            ISch_Parameter parameter = FindParameter(component, name);
            if (parameter != null)
            {
                robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                parameter.SetState_Text(value);
                parameter.SetState_ParamType(ParameterType(type));
                robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                return false;
            }

            // eCreate_GlobalCopy, as the schematic guide uses for objects added to a component.
            parameter = (ISch_Parameter)schServer.SchObjectFactory(SCH.TObjectId.eParameter, SCH.TObjectCreationMode.eCreate_GlobalCopy);
            parameter.SetState_Name(name);
            parameter.SetState_Text(value);
            parameter.SetState_ParamType(ParameterType(type));
            // Hidden and at the component's location, so a dozen metadata texts do not appear
            // at the sheet origin.
            parameter.SetState_IsHidden(true);
            parameter.SetState_Location(component.GetState_Location());
            component.AddSchObject(parameter);
            robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, parameter);
            return true;
        }

        /// <summary>
        /// Make the existing PCB models non-current, then add a PCBLIB
        /// implementation that links the footprint in the downloaded PcbLib and make it current.
        /// </summary>
        public static void AddFootprintModel(ISch_Component component, string footprint, string pcbLibPath)
        {
            ISch_Iterator iterator = component.SchIterator_Create();
            try
            {
                iterator.SetState_IterationDepth(TIterationDepth.eIterateFirstLevel);
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eImplementation });
                for (var model = iterator.FirstSchObject() as ISch_Implementation; model != null;
                     model = iterator.NextSchObject() as ISch_Implementation)
                {
                    if (string.Equals(model.GetState_ModelType(), PcbModelType, StringComparison.OrdinalIgnoreCase))
                        model.SetState_IsCurrent(false);
                }
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }

            ISch_Implementation implementation = component.AddSchImplementation()
                ?? throw new UserException("AddSchImplementation returned null; the footprint model was not added.");
            implementation.SetState_ModelName(footprint);
            implementation.SetState_ModelType(PcbModelType);
            // Entity name, location (the library file), file kind: the argument order of the reference.
            implementation.AddDataFileLink(footprint, pcbLibPath, PcbModelType);
            implementation.SetState_IsCurrent(true);
        }

        /// <summary>"name=value; ..." of the parameters, and "type:model*" of the models, for the result text.</summary>
        public static string Describe(ISch_Component component)
        {
            var parameters = new List<string>();
            var models = new List<string>();
            ISch_Iterator iterator = component.SchIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eParameter, SCH.TObjectId.eImplementation });
                for (ISch_BasicContainer item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                {
                    // Dispatch on ObjectId, not on a C# type test: "item is ISch_Parameter" can be
                    // true for an object whose GetState_Name then fails with "Interface not supported".
                    TObjectId kind = item.GetState_ObjectId();
                    if (kind == TObjectId.eParameter)
                    {
                        var p = (ISch_Parameter)item;
                        string name = p.GetState_Name();
                        parameters.Add(name.StartsWith("ComponentManager_", StringComparison.OrdinalIgnoreCase)
                            ? $"{name}={p.GetState_Text()}"
                            : $"{name}={p.GetState_Text()} ({p.GetState_ParamType()})");
                    }
                    else if (kind == TObjectId.eImplementation && item is ISch_Implementation m)
                        models.Add($"{m.GetState_ModelType()}:{m.GetState_ModelName()}{(m.GetState_IsCurrent() ? "*" : "")}" +
                                   (m.GetState_DatafileLinkCount() > 0 ? $" -> {m.GetState_SchDatafileLink(0)?.GetState_Location()}" : ""));
                }
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }

            DXP.Point location = component.GetState_Location();
            return $"{component.GetState_SchDesignator()?.GetState_Text()} '{component.GetState_LibReference()}' " +
                   $"(DesignItemId '{component.GetState_DesignItemId()}', SourceLibraryName '{component.GetState_SourceLibraryName()}') " +
                   $"at ({Mils(location.X)}, {Mils(location.Y)}) mil; models [{string.Join(", ", models)}]; " +
                   $"parameters [{string.Join("; ", parameters)}]";
        }

        /// <summary>The protocol's type names (case-insensitive); anything else is a string parameter.</summary>
        private static TParameterType ParameterType(string type) => (type ?? "").ToUpperInvariant() switch
        {
            "BOOLEAN" => TParameterType.eParameterType_Boolean,
            "INTEGER" => TParameterType.eParameterType_Integer,
            "FLOAT" => TParameterType.eParameterType_Float,
            _ => TParameterType.eParameterType_String,
        };

        private static ISch_Parameter FindParameter(ISch_Component component, string name)
        {
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator == null)
                return null;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eParameter });
                for (var parameter = iterator.FirstSchObject() as ISch_Parameter; parameter != null;
                     parameter = iterator.NextSchObject() as ISch_Parameter)
                {
                    if (string.Equals(parameter.GetState_Name(), name, StringComparison.OrdinalIgnoreCase))
                        return parameter;
                }
                return null;
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
        }

        private static ISch_Implementation CurrentPcbModel(ISch_Component component)
        {
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator == null)
                return null;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eImplementation });
                for (var model = iterator.FirstSchObject() as ISch_Implementation; model != null;
                     model = iterator.NextSchObject() as ISch_Implementation)
                {
                    if (model.GetState_IsCurrent() &&
                        string.Equals(model.GetState_ModelType(), PcbModelType, StringComparison.OrdinalIgnoreCase))
                        return model;
                }
                return null;
            }
            finally
            {
                component.SchIterator_Destroy(ref iterator);
            }
        }

        // Schematic coordinates are shown with EDP.Utils.CoordToMils, as the PlaceLibraryComponent sample does; the
        // unit of schematic coordinates is not stated in the reference.
        private static string Mils(int coord) => EDP.Utils.CoordToMils(coord).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
