using System;
using System.Collections.Generic;
using System.Globalization;
using DXP;
using EDP; // DM_Preferences, GetVariantOption and the typed IVariantOption getters are extension methods in EDP

namespace AdSamples.VariantDisplayOptions
{
    /// <summary>
    /// Reads the workspace-level variant display preferences: how schematic sheets and PCB documents mark components
    /// that a variant does not fit or alters. The chain is IClient.GetDXPWorkspace (cast to IWorkspace) ›
    /// IWorkspace.DM_Preferences › IWorkspacePreferences.GetVariantOption › IVariantOption.DM_* getters.
    /// IVariantOption has only getters in the .NET API, so the sample is read-only.
    /// </summary>
    internal static class VariantDisplayOptionsCommands
    {
        /// <summary>
        /// GetVariantOptions: returns the schematic "Use Text" flag first, then every other getter of
        /// IVariantOption grouped by editor. Values are printed raw (no unit conversion), because the reference
        /// does not give the units of the size, height, width and colour values.
        /// </summary>
        [HotCommand("GetVariantOptions", "Report the workspace's variant display options")]
        public static string GetVariantOptions(CommandContext ctx)
        {
            IVariantOption option = CurrentVariantOption();

            var lines = new List<string>
            {
                "Schematic Drawing Options : Use Text = " + Read(() => option.DM_SCHUseText()),
                "",
                "Schematic:",
            };

            Add(lines, "UseText", () => option.DM_SCHUseText());
            Add(lines, "UseGraphics", () => option.DM_SCHUseGraphics());
            Add(lines, "Graphics", () => option.DM_SCHGraphics());
            Add(lines, "Text", () => option.DM_SCHText());
            Add(lines, "TextFontName", () => option.DM_SCHTextFontName());
            Add(lines, "TextSize", () => option.DM_SCHTextSize());
            Add(lines, "TextColor", () => Color(option.DM_SCHTextColor()));
            Add(lines, "TextBold", () => option.DM_SCHTextBold());
            Add(lines, "TextItalic", () => option.DM_SCHTextItalic());
            Add(lines, "TextUnderline", () => option.DM_SCHTextUnderline());
            Add(lines, "TextStrikeOut", () => option.DM_SCHTextStrikeOut());
            Add(lines, "ParamModifyText", () => option.DM_SCHParamModifyText());
            Add(lines, "ParamSuffix", () => option.DM_SCHParamSuffix());
            Add(lines, "ParamFontName", () => option.DM_SCHParamFontName());
            Add(lines, "ParamSize", () => option.DM_SCHParamSize());
            Add(lines, "ParamColor", () => Color(option.DM_SCHParamColor()));
            Add(lines, "ParamBold", () => option.DM_SCHParamBold());
            Add(lines, "ParamItalic", () => option.DM_SCHParamItalic());
            Add(lines, "ParamUnderline", () => option.DM_SCHParamUnderline());
            Add(lines, "ParamStrikeOut", () => option.DM_SCHParamStrikeOut());

            lines.Add("PCB:");
            Add(lines, "UseText", () => option.DM_PCBUseText());
            Add(lines, "UseGraphics", () => option.DM_PCBUseGraphics());
            Add(lines, "Graphics", () => option.DM_PCBGraphics());
            Add(lines, "GraphicsColor", () => Color(option.DM_PCBGraphicsColor()));
            Add(lines, "ShowComponent", () => option.DM_PCBShowComponent());
            Add(lines, "StrikeThroughDesignator", () => option.DM_PCBStrikeThroughDesignator());
            Add(lines, "Text", () => option.DM_PCBText());
            Add(lines, "TextColor", () => Color(option.DM_PCBTextColor()));
            Add(lines, "TextHeight", () => option.DM_PCBTextHeight());
            Add(lines, "TextUseTTFont", () => option.DM_PCBTextUseTTFont());
            Add(lines, "TextStrokeFontName", () => option.DM_PCBTextStrokeFontName());
            Add(lines, "TextStrokeWidth", () => option.DM_PCBTextStrokeWidth());
            Add(lines, "TextTTFontName", () => option.DM_PCBTextTTFontName());
            Add(lines, "TextTTBold", () => option.DM_PCBTextTTBold());
            Add(lines, "TextTTItalic", () => option.DM_PCBTextTTItalic());
            Add(lines, "TextTTInverted", () => option.DM_PCBTextTTInverted());
            Add(lines, "TextTTInvertedBorder", () => option.DM_PCBTextTTInvertedBorder());

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Each step of the chain is checked for null, so a missing
        /// workspace or preferences object gives a clear message instead of a NullReferenceException.
        /// </summary>
        private static IVariantOption CurrentVariantOption()
        {
            var workspace = DXP.GlobalVars.Client?.GetDXPWorkspace() as IWorkspace
                ?? throw new UserException("No workspace is available.");
            IWorkspacePreferences preferences = workspace.DM_Preferences()
                ?? throw new UserException("The workspace returned no preferences object.");
            return preferences.GetVariantOption()
                ?? throw new UserException("The workspace preferences returned no variant options.");
        }

        private static void Add(List<string> lines, string name, Func<object> getter) =>
            lines.Add($"    {name} = {Read(getter)}");

        /// <summary>
        /// Reads one value. A getter that throws is reported in its line, so one failing member does not hide the
        /// others.
        /// </summary>
        private static string Read(Func<object> getter)
        {
            try
            {
                object value = getter();
                return value switch
                {
                    null => "(null)",
                    string s => "'" + s + "'",
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                };
            }
            catch (Exception ex)
            {
                return $"<error: {ex.GetType().Name}: {ex.Message}>";
            }
        }

        /// <summary>
        /// Colours are returned as int. The byte order is not documented, so the value is shown as decimal and hex
        /// without interpreting it.
        /// </summary>
        private static RawColor Color(int value) => new RawColor(value);

        private readonly record struct RawColor(int Value)
        {
            public override string ToString() =>
                string.Format(CultureInfo.InvariantCulture, "{0} (0x{0:X8})", Value);
        }
    }
}
