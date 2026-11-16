namespace AdSamples
{
    /// <summary>Panel commands, compiled in when the sample has Ui\PanelContent.cs. (Shared framework file.)</summary>
    internal static class PanelCommands
    {
        [HotCommand("Panel.Show", "Open the sample's panel")]
        public static string Show(CommandContext ctx)
        {
            SamplePanel.Show();
            return ctx.IsInteractive ? null : SampleInfo.Name + " panel shown.";
        }

        /// <summary>Called by the shim's panel the first time it is shown, possibly before any other command ran.</summary>
        [HotCommand("Panel.Attach", "Fill the sample's panel (the panel calls this itself)")]
        public static string Attach(CommandContext ctx)
        {
            SamplePanel.Attach();
            return null;
        }
    }
}
