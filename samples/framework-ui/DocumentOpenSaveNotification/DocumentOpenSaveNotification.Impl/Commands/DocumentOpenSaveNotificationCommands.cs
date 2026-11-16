using System;
using System.IO;
using System.Text;
using DXP;
using SCH;

namespace AdSamples.DocumentOpenSaveNotification
{
    /// <summary>
    /// The switch for the notification handler, and the handler's two actions as commands of their own, so that each
    /// step can be run and checked on the focused schematic.
    /// </summary>
    internal static class DocumentOpenSaveNotificationCommands
    {
        /// <summary>
        /// EnableNotifications "State=on|off|status|Name=&lt;text&gt;|Value=&lt;text&gt;|Under=&lt;folder&gt;|Reset":
        /// turns the load/save handler on or off and reports what it saw.
        /// </summary>
        [HotCommand("EnableNotifications", "Turn the document load/save handler on or off, or show its state and the notifications it saw")]
        public static string EnableNotifications(CommandContext ctx)
        {
            string state = ctx.Get("State", "status").ToLowerInvariant();
            if (state != "on" && state != "off" && state != "status")
                throw new UserException($"State='{state}': use State=on, State=off or State=status.");

            if (state == "on")
            {
                string name = ctx.Get("Name", NotificationState.Name);
                if (string.IsNullOrWhiteSpace(name))
                    throw new UserException("The parameter name is empty. Pass Name=<text> (default SampleParameter).");
                NotificationState.Name = name;
                NotificationState.Value = ctx.Get("Value", NotificationState.Value);
                string under = ctx.Get("Under", "");
                if (under.Length > 0 && !Directory.Exists(under))
                    throw new UserException($"Under='{under}' is not an existing folder.");
                NotificationState.Under = under;
                NotificationState.Enabled = true;
            }
            else if (state == "off")
            {
                NotificationState.Enabled = false;
            }
            if (ctx.Flag("Reset"))
                NotificationState.ResetCounters();

            return Status();
        }

        /// <summary>AddParameters "Name=&lt;text&gt;|Value=&lt;text&gt;": what the load handler does, on the focused schematic.</summary>
        [HotCommand("AddParameters", "Add the hidden sample parameter to every component of the focused schematic")]
        public static string AddParameters(CommandContext ctx)
        {
            string name = RequireName(ctx);
            string value = ctx.Get("Value", ComponentParameters.DefaultValue);
            ISch_Document sheet = CurrentSheet();

            var (components, created, updated) = InUndoStep(sheet, () => ComponentParameters.AddToAll(sheet, name, value));
            return $"{FileOf(sheet)}: '{name}'='{value}' added to {created} of {components} components, {updated} updated. " +
                   "In memory only: save the document to keep it.";
        }

        /// <summary>RemoveParameters "Name=&lt;text&gt;": what the save handler does before it saves again.</summary>
        [HotCommand("RemoveParameters", "Remove the sample parameter from every component of the focused schematic")]
        public static string RemoveParameters(CommandContext ctx)
        {
            string name = RequireName(ctx);
            ISch_Document sheet = CurrentSheet();

            var (components, removed) = InUndoStep(sheet, () => ComponentParameters.RemoveFromAll(sheet, name));
            return $"{FileOf(sheet)}: '{name}' removed from {removed} of {components} components.";
        }

        /// <summary>CountParameters "Name=&lt;text&gt;|Value=&lt;text&gt;": the read-back for the other commands and the handler.</summary>
        [HotCommand("CountParameters", "Count the components of the focused schematic that carry the sample parameter")]
        public static string CountParameters(CommandContext ctx)
        {
            string name = RequireName(ctx);
            string value = ctx.Get("Value", ComponentParameters.DefaultValue);
            ISch_Document sheet = CurrentSheet();

            var (components, withParameter, withValue) = ComponentParameters.Count(sheet, name, value);
            IServerDocument document = ServerDocumentOf(sheet);
            string modified = document == null ? "unknown" : document.GetModified() ? "yes" : "no";
            return $"{FileOf(sheet)}: {withParameter} of {components} components carry '{name}' ({withValue} with value '{value}'). " +
                   $"Document modified: {modified}.";
        }

        /// <summary>
        /// SaveDocument "Via=dofilesave|process": saves the focused schematic with IServerDocument.DoFileSave, or with
        /// Via=process through AD's WorkSpaceManager:SaveObject; only the latter raises DocumentSaved, so only it runs
        /// the save handler.
        /// </summary>
        [HotCommand("SaveDocument", "Save the focused schematic (runs the save handler when it is on)")]
        public static string SaveDocument(CommandContext ctx)
        {
            ISch_Document sheet = CurrentSheet();
            IServerDocument document = ServerDocumentOf(sheet)
                ?? throw new UserException($"{FileOf(sheet)} is not open as a server document.");

            int savesBefore = NotificationState.Saves;
            // Via=process runs AD's own save process on the focused document, as File > Save does. The default,
            // IServerDocument.DoFileSave, writes the file but raises no DocumentSaved (10) notification,
            // so only the process path reaches the save handler.
            string via = ctx.Get("Via", "dofilesave").ToLowerInvariant();
            if (via == "process")
            {
                DXP.Utils.RunCommand("WorkSpaceManager:SaveObject", "ObjectKind=Document");
                string ran = !NotificationState.Enabled
                    ? "The handler is off."
                    : NotificationState.Saves > savesBefore
                        ? "The save handler ran: " + (NotificationState.RecentEvents.Count > 0 ? NotificationState.RecentEvents[^1] : "")
                        : "The handler is on but did not handle this save (see EnableNotifications).";
                return $"{FileOf(sheet)}: WorkSpaceManager:SaveObject ran. {ran}";
            }
            if (via != "dofilesave")
                throw new UserException($"Via='{via}': use Via=dofilesave (default) or Via=process.");

            bool saved = document.DoFileSave("");
            string handler = !NotificationState.Enabled
                ? "The handler is off."
                : NotificationState.Saves > savesBefore
                    ? "The save handler ran: " + (NotificationState.RecentEvents.Count > 0 ? NotificationState.RecentEvents[^1] : "")
                    : "The handler is on but did not handle this save (see EnableNotifications).";
            return $"{FileOf(sheet)}: DoFileSave returned {saved}. {handler}";
        }

        private static string Status()
        {
            var text = new StringBuilder();
            string under = NotificationState.Under.Length == 0 ? "every schematic" : "schematics under " + NotificationState.Under;
            text.AppendLine($"Notifications: {(NotificationState.Enabled ? "on" : "off")} ('{NotificationState.Name}'='{NotificationState.Value}', {under}).");
            text.AppendLine($"Notifications received while on: {NotificationState.Received} (any type).");
            text.AppendLine($"Document notification codes: {NotificationState.CodeSummary()}.");
            text.AppendLine($"Loads handled: {NotificationState.Loads}, saves handled: {NotificationState.Saves}, own re-saves ignored: {NotificationState.NestedSaves}, " +
                            $"loads pending: {NotificationState.PendingLoads.Count}.");
            text.Append($"Last error: {NotificationState.LastError ?? "none"}.");
            foreach (string line in NotificationState.RecentEvents)
                text.Append("\n  ").Append(line);
            return text.ToString();
        }

        private static string RequireName(CommandContext ctx)
        {
            string name = ctx.Get("Name", NotificationState.Name);
            if (string.IsNullOrWhiteSpace(name))
                throw new UserException("The parameter name is empty. Pass Name=<text> (default SampleParameter).");
            return name;
        }

        private static ISch_Document CurrentSheet()
        {
            ISch_ServerInterface schServer = SCH.GlobalVars.SchServer
                ?? throw new UserException("The schematic editor is not running. Open a .SchDoc first.");
            ISch_Document document = schServer.GetCurrentSchDocument()
                ?? throw new UserException("No schematic document is active. Open and focus a .SchDoc first.");
            if (document.GetState_ObjectId() != SCH.TObjectId.eSheet)
                throw new UserException("The active document is a schematic library. Focus a .SchDoc sheet.");
            return document;
        }

        private static IServerDocument ServerDocumentOf(ISch_Document sheet) =>
            DXP.GlobalVars.Client.GetDocumentByPath(sheet.GetState_DocumentName());

        private static string FileOf(ISch_Document sheet) => Path.GetFileName(sheet.GetState_DocumentName());

        /// <summary>PreProcess/PostProcess around a command's changes make them one undoable step (as ModifySchWireColor).</summary>
        private static T InUndoStep<T>(ISch_Document sheet, Func<T> change)
        {
            IProcessControl processControl = DXP.GlobalVars.Client.GetProcessControl();
            processControl.PreProcess(sheet, "");
            try
            {
                return change();
            }
            finally
            {
                processControl.PostProcess(sheet, "");
                sheet.GraphicallyInvalidate();
            }
        }
    }
}
