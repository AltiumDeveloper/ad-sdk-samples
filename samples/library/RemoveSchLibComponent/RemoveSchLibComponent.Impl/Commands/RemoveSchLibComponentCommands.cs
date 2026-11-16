using SCH;

namespace AdSamples.RemoveSchLibComponent
{
    /// <summary>
    /// Deletes a component, given by its library reference, from the active schematic library. The SDK lesson: find
    /// the component with ISch_Lib.SchComponentByLibRef, then let the built-in process Sch:DeleteComponentFromLibrary
    /// do the deletion (launched with DXP.Utils.RunCommand), instead of removing the object yourself.
    /// </summary>
    internal static class RemoveSchLibComponentCommands
    {
        // The SDK reference does not document this process or its parameters.
        private const string DeleteProcess = "Sch:DeleteComponentFromLibrary";

        /// <summary>RemoveComponent "Name=&lt;library reference&gt;": deletes that component from the active SchLib.</summary>
        [HotCommand("RemoveComponent", "Delete a named component from the active schematic library")]
        public static string RemoveComponent(CommandContext ctx)
        {
            string name = ctx.Get("Name");
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("No component specified. Pass its library reference as a parameter: Name=<library reference>.");
            // The name is forwarded inside a process parameter string, where '|' separates parameters.
            if (name.Contains("|"))
                throw new UserException($"The name '{name}' contains '|', which cannot be passed to {DeleteProcess}.");

            ISch_Lib library = CurrentLibrary();
            if (library.LibIsEmpty())
                throw new UserException("The schematic library is empty.");

            int before = CountComponents(library, out string examples);
            if (library.GetState_SchComponentByLibRef(name) == null)
                throw new UserException($"No component '{name}' in the library ({before} components, for example: {examples}).");

            // NoDialog=True asks the process to skip its confirmation prompt.
            DXP.Utils.RunCommand(DeleteProcess, $"Name={name}|NoDialog=True");

            // RunCommand returns nothing, so read the library again to report what really happened.
            ISch_Lib after = CurrentLibrary();
            int remaining = CountComponents(after, out _);
            bool stillThere = after.GetState_SchComponentByLibRef(name) != null;

            string counts = $"components: {before} before, {remaining} after";
            if (stillThere)
                return $"'{name}' is still in the library after {DeleteProcess} ({counts}).";
            return $"Deleted '{name}' from the library ({counts}). The library is changed in memory only: save it to keep the change, or close it without saving.";
        }

        private static ISch_Lib CurrentLibrary()
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchLib first.");
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchLib first.");

            // `document as ISch_Lib` succeeds for every schematic document (docs: schematic basics), so check the
            // object ID. The type test does not tell a sheet from a library.
            if (document.GetState_ObjectId() != TObjectId.eSchLib)
                throw new UserException("The active document is not a schematic library. Open and focus a .SchLib first.");
            return (ISch_Lib)document;
        }

        /// <summary>Counts the library's components; <paramref name="examples"/> gets the first few library references.</summary>
        private static int CountComponents(ISch_Lib library, out string examples)
        {
            const int MaxExamples = 10;
            var names = new System.Collections.Generic.List<string>();

            // The library iterator walks the library's components (reference: ISch_Lib remarks).
            ISch_Iterator iterator = library.SchLibIterator_Create();
            try
            {
                iterator.AddFilter_ObjectSet(new TObjectSet { TObjectId.eSchComponent });
                int count = 0;
                for (ISch_BasicContainer item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                {
                    count++;
                    // The filter admits components only, so the cast is safe (a type test would prove nothing:
                    // it succeeds for every object, docs: interfaces-at-run-time.mdx#type-tests-succeed-for-every-object).
                    if (names.Count < MaxExamples)
                        names.Add(((ISch_Component)item).GetState_LibReference());
                }
                examples = names.Count == 0 ? "none" : string.Join(", ", names) + (count > names.Count ? ", ..." : "");
                return count;
            }
            finally
            {
                library.SchIterator_Destroy(ref iterator);
            }
        }
    }
}
