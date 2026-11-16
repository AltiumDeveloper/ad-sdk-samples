using System;
using SCH;

namespace AdSamples.DocumentOpenSaveNotification
{
    /// <summary>
    /// Find, add and remove one named parameter on every component of a schematic document.
    /// Used by the commands and by the notification handler.
    /// </summary>
    internal static class ComponentParameters
    {
        // Readable defaults; both can be changed with Name=/Value= parameters.
        public const string DefaultName = "SampleParameter";
        public const string DefaultValue = "SampleValue";

        /// <summary>The component's parameter called <paramref name="name"/> (case-insensitive, as SameText), or null.</summary>
        public static ISch_Parameter Find(ISch_Component component, string name)
        {
            ISch_Iterator iterator = component.SchIterator_Create();
            if (iterator == null)
                return null;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eParameter });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                {
                    var parameter = (ISch_Parameter)obj; // the filter guarantees parameters
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

        /// <summary>
        /// SetSchComponentParam for every component: creates the hidden parameter where it is missing (at the
        /// component's location) and sets its text everywhere. Returns (components, created, updated).
        /// </summary>
        public static (int Components, int Created, int Updated) AddToAll(ISch_Document document, string name, string value)
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer;
            ISch_RobotManager robot = schServer.GetState_RobotManager();
            int components = 0, created = 0, updated = 0;

            ForEachComponent(document, component =>
            {
                components++;
                ISch_Parameter parameter = Find(component, name);
                robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                if (parameter == null)
                {
                    // Set the properties first, then add it to its owner (docs: Working with schematics, "Creating
                    // objects").
                    parameter = (ISch_Parameter)schServer.SchObjectFactory(SCH.TObjectId.eParameter, SCH.TObjectCreationMode.eCreate_Default);
                    parameter.SetState_Name(name);
                    parameter.SetState_Text(value);
                    parameter.SetState_IsHidden(true);
                    parameter.SetState_Location(component.GetState_Location());
                    component.AddSchObject(parameter);
                    // Tell the editor about the new child object, so undo and the inspector know it.
                    robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMPrimitiveRegistration, parameter);
                    created++;
                }
                else
                {
                    robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                    parameter.SetState_Text(value);
                    robot.SendMessage(parameter, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                    updated++;
                }
                robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
            });
            return (components, created, updated);
        }

        /// <summary>RemoveComponentParameters: removes the parameter from every component that has it. Returns (components, removed).</summary>
        public static (int Components, int Removed) RemoveFromAll(ISch_Document document, string name)
        {
            ISch_RobotManager robot = SCH.GlobalVars.SchServer.GetState_RobotManager();
            int components = 0, removed = 0;

            ForEachComponent(document, component =>
            {
                components++;
                ISch_Parameter parameter = Find(component, name);
                if (parameter == null)
                    return;
                robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMBeginModify, SCHConstant.NoEventData);
                component.RemoveSchObject(parameter);
                robot.SendMessage(component, SCHConstant.BroadCast, SCHConstant.SCHMEndModify, SCHConstant.NoEventData);
                removed++;
            });
            return (components, removed);
        }

        /// <summary>Returns (components, components that carry the parameter, of those with the value <paramref name="value"/>).</summary>
        public static (int Components, int WithParameter, int WithValue) Count(ISch_Document document, string name, string value)
        {
            int components = 0, withParameter = 0, withValue = 0;
            ForEachComponent(document, component =>
            {
                components++;
                ISch_Parameter parameter = Find(component, name);
                if (parameter == null)
                    return;
                withParameter++;
                if (parameter.GetState_Text() == value)
                    withValue++;
            });
            return (components, withParameter, withValue);
        }

        /// <summary>
        /// Calls <paramref name="action"/> for every component of the document. The document iterator is destroyed
        /// before returning, also when the action throws; each component's own iterator is destroyed in Find.
        /// </summary>
        private static void ForEachComponent(ISch_Document document, Action<ISch_Component> action)
        {
            ISch_Iterator iterator = document.SchIterator_Create()
                ?? throw new UserException("Could not create an iterator on the schematic document.");
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet { SCH.TObjectId.eSchComponent });
                for (ISch_BasicContainer obj = iterator.FirstSchObject(); obj != null; obj = iterator.NextSchObject())
                    action((ISch_Component)obj);
            }
            finally
            {
                document.SchIterator_Destroy(ref iterator);
            }
        }
    }
}
