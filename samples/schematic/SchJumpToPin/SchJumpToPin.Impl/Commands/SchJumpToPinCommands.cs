using System;
using System.Collections.Generic;
using System.IO;
using SCH;

namespace AdSamples.SchJumpToPin
{
    /// <summary>
    /// Jumps to a pin of a placed component. The default method drives the schematic editor with one of its own
    /// processes, Sch:CrossProbeNotify, launched through DXP.Utils.RunCommand. The alternative finds the pin with a
    /// sheet iterator and a component iterator and selects it.
    /// </summary>
    internal static class SchJumpToPinCommands
    {
        /// <summary>The schematic server process that does the jump. It is not part of the SDK reference.</summary>
        private const string CrossProbeProcess = "Sch:CrossProbeNotify";

        /// <summary>
        /// JumpToPin ["Designator=&lt;text&gt;|Pin=&lt;pin designator&gt;|Method=CrossProbe|Select"].
        /// Returns what was found on the current sheet, which method ran and the pin's selection state afterwards.
        /// </summary>
        [HotCommand("JumpToPin", "Jump to a component pin (cross-probe)")]
        public static string JumpToPin(CommandContext ctx)
        {
            // Defaults: component HDR3, pin 8.
            string designator = ctx.Get("Designator", "HDR3");
            string pinDesignator = ctx.Get("Pin", "8");
            if (string.IsNullOrWhiteSpace(designator) || string.IsNullOrWhiteSpace(pinDesignator))
                throw new UserException("Designator and Pin must not be empty.");
            bool select = ParseMethod(ctx.Get("Method", "CrossProbe"));

            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document sheet = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            string file = Path.GetFileName(sheet.GetState_DocumentName());

            // Both methods look the pin up first, so the result can say whether it exists on this sheet.
            PinLookup lookup = FindPin(sheet, designator, pinDesignator);
            string found = Describe(file, designator, pinDesignator, lookup);

            if (select)
            {
                if (lookup.Pin == null)
                    return found + " Method=Select: nothing selected.";

                // Selection is view state, not design data: no SCHMBeginModify/SCHMEndModify.
                // GraphicallyInvalidate redraws the pin so the
                // selection shows without another refresh. Other selected objects stay selected.
                lookup.Pin.SetState_Selection(true);
                lookup.Pin.GraphicallyInvalidate();
                return found + $" Method=Select: pin selected = {lookup.Pin.GetState_Selection()}. The view is not moved.";
            }

            // Id is "<pin designator>:<component designator>", for example 'Kind=Pin|Id=8:HDR3'. The SDK docs do not
            // describe this process or its parameters.
            string parameters = $"Kind=Pin|Id={pinDesignator}:{designator}";
            // RunCommand returns nothing, so success can't be read back. The pin's Selection afterwards is reported
            // as a hint only: whether the cross-probe selects the pin is not documented.
            DXP.Utils.RunCommand(CrossProbeProcess, parameters);
            string after = lookup.Pin == null ? "" : $" Pin selected afterwards = {lookup.Pin.GetState_Selection()}.";
            return found + $" Ran {CrossProbeProcess} '{parameters}'.{after}";
        }

        private sealed class PinLookup
        {
            public int ComponentCount;
            public bool ComponentFound;
            public ISch_Pin Pin;
            public readonly List<string> PinsOfComponent = new List<string>();
        }

        /// <summary>
        /// Components of the sheet, then the pins of the first component whose
        /// designator matches (case-insensitive).
        /// </summary>
        private static PinLookup FindPin(ISch_Document sheet, string designator, string pinDesignator)
        {
            var result = new PinLookup();
            ISch_Iterator components = sheet.SchIterator_Create();
            try
            {
                components.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.eSchComponent });

                // FirstSchObject/NextSchObject return ISch_BasicContainer: the filter makes the cast safe.
                for (var component = components.FirstSchObject() as ISch_Component;
                     component != null;
                     component = components.NextSchObject() as ISch_Component)
                {
                    result.ComponentCount++;
                    ISch_Designator schDesignator = component.GetState_SchDesignator();
                    if (schDesignator == null ||
                        !string.Equals(schDesignator.GetState_Text(), designator, StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.ComponentFound = true;
                    ISch_Iterator pins = component.SchIterator_Create();
                    try
                    {
                        pins.AddFilter_ObjectSet(new SCH.TObjectSet { TObjectId.ePin });
                        // All levels, so pins of every part of a multi-part component are found.
                        pins.SetState_IterationDepth(TIterationDepth.eIterateAllLevels);
                        for (var pin = pins.FirstSchObject() as ISch_Pin; pin != null; pin = pins.NextSchObject() as ISch_Pin)
                        {
                            result.PinsOfComponent.Add(pin.GetState_Designator());
                            if (result.Pin == null &&
                                string.Equals(pin.GetState_Designator(), pinDesignator, StringComparison.OrdinalIgnoreCase))
                                result.Pin = pin;
                        }
                    }
                    finally
                    {
                        component.SchIterator_Destroy(ref pins);
                    }
                    break; // first matching component wins
                }
            }
            finally
            {
                sheet.SchIterator_Destroy(ref components);
            }
            return result;
        }

        private static string Describe(string file, string designator, string pinDesignator, PinLookup lookup)
        {
            if (!lookup.ComponentFound)
                return $"{file}: no component '{designator}' among {lookup.ComponentCount} component(s) on this sheet.";
            if (lookup.Pin == null)
                return $"{file}: component '{designator}' has no pin '{pinDesignator}' " +
                       $"(pins: {string.Join(", ", lookup.PinsOfComponent)}).";
            return $"{file}: found pin '{lookup.Pin.GetState_Designator()}' (name '{lookup.Pin.GetState_Name()}') " +
                   $"of '{designator}', part {lookup.Pin.GetState_OwnerPartId()}.";
        }

        /// <summary>true for Select, false for CrossProbe.</summary>
        private static bool ParseMethod(string value)
        {
            if (string.Equals(value, "CrossProbe", StringComparison.OrdinalIgnoreCase) || value.Length == 0)
                return false;
            if (string.Equals(value, "Select", StringComparison.OrdinalIgnoreCase))
                return true;
            throw new UserException($"Method must be CrossProbe or Select, got '{value}'.");
        }
    }
}
