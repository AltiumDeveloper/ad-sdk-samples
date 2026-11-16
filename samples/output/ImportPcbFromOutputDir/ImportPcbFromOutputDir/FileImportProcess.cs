using DXP;

namespace CSharpPlugin
{
    /// <summary>
    /// Shim part of the sample (tier 3): the AD process "ImportPcbFromOutputDir:FileImportDirectory". An extension
    /// can name a command as the Command of OutputHandler entries in its .ins, so that AD (not a menu)
    /// calls it with "BaseExportPath=&lt;export folder&gt;". A process name is what such an .ins entry
    /// refers to, so the sample registers one, with that parameter string, and forwards it to the
    /// implementation command of the same name, exactly like the generic "Run" process does.
    /// </summary>
    public partial class PluginServerModule
    {
        // Template hook RegisterCommands: registers processes next to Run, when Altium Designer creates the module.
        // FileImportDirectory is the process the .ins OutputHandler entries name.
        partial void RegisterCommands(CommandLauncher launcher)
        {
            launcher.RegisterCommand("FileImportDirectory", FileImportProcess.FileImportDirectory, Commands.GetState_Always);
        }
    }

    internal static class FileImportProcess
    {
        /// <summary>
        /// ImportPcbFromOutputDir:FileImportDirectory  Params: BaseExportPath=&lt;path containing 'Unit Output files'&gt;.
        /// Shows the result (or the error) in a message box, through Commands.Run.
        /// </summary>
        public static void FileImportDirectory(IServerDocumentView view, ref string parameters)
        {
            string forwarded = "Cmd=FileImportDirectory|" + (parameters ?? string.Empty);
            Commands.Run(view, ref forwarded);
        }
    }
}
