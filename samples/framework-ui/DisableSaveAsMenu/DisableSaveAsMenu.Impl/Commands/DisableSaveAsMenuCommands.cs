namespace AdSamples.DisableSaveAsMenu
{
    /// <summary>
    /// The sample's effect is not in code: Installation\DisableSaveAsMenu.rcs holds two Deletion elements that remove
    /// the Save As and Save As Copy links from the schematic editor's File menu (tree MNSchematic_File, defined in
    /// AD's System\advsch.rcs). AD applies them when it merges the menus at start-up, so a change to them needs a
    /// redeploy and an AD restart; hot reload cannot show it.
    ///
    /// This command is a stub. No entry of AD's File
    /// menu runs it; the sample's own menu does. It only reports what
    /// the .rcs removes, whether or not the start-up merge applied it.
    /// </summary>
    internal static class DisableSaveAsMenuCommands
    {
        // Must match the DeleteID values in Installation\DisableSaveAsMenu.rcs. In System\advsch.rcs they are
        // the links to PLWorkspaceManager:SaveDocumentAs and PLWorkspaceManager:SaveDocumentAsCopy. These are AD's
        // internal menu IDs, not a published contract: they may change between releases.
        private const string TargetMenu = "MNSchematic_File";
        private static readonly string[] DeletedIds = { "MNSchematic_File90", "MNSchematic_File100" };

        [HotCommand("DisableSaveAs", "Report that Save As is disabled and which schematic File menu entries this extension removes")]
        public static string DisableSaveAs(CommandContext ctx)
        {
            return "Save As is disabled: this extension's .rcs deletes "
                + string.Join(" and ", DeletedIds)
                + " (Save As, Save As Copy) from " + TargetMenu
                + " (schematic File menu) when Altium Designer merges its menus at start-up. "
                + "Only that menu is changed: other editors' File menus and any other route to Save As are left as they are.";
        }
    }
}
