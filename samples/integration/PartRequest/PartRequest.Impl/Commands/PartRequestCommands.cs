using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DXP;

namespace AdSamples.PartRequest
{
    /// <summary>
    /// Queries the workspace task service for the Part Request tasks of the signed-in user. The SDK lesson: the
    /// EDMS entry point is EDP.Utils.GetDXPServerManager, but every EDMS type (server manager, task service, search
    /// parameters, tasks) lives in the DXP namespace.
    /// </summary>
    internal static class PartRequestCommands
    {
        [HotCommand("GetPartRequests", "List the current user's Part Request tasks")]
        public static string GetPartRequests(CommandContext ctx)
        {
            int max = PositiveInt(ctx, "Max", 50);
            string sortField = ctx.Get("Sort", "CreatedAt");
            bool resolveNames = ctx.Flag("Names");

            IEDMS_DXPServerManager serverManager = EDP.Utils.GetDXPServerManager()
                ?? throw new UserException("The server manager is not available in this Altium Designer session.");
            // Check the sign-in first. Do not sign in from here (LoginWithUI opens a dialog, which would
            // block AD on the pipe path): ask the user to sign in instead.
            if (!serverManager.IsConnected())
                throw new UserException("Not signed in to a workspace. Sign in to Altium 365 or a server first.");

            IEDMS_TaskSearchParams searchParams;
            IInterfaceList tasks;
            string errorMsg;
            bool ok;
            try
            {
                // Every COM call from here to the search is inside the try, so a COMException becomes a
                // UserException message instead of a raw exception.
                IEDMS_TaskService taskService = serverManager.GetTaskService()
                    ?? throw new UserException("The connected workspace has no task service.");

                // ItemsPerPage = -1 means "all items"; the reference documents neither that value nor the sort
                // field names.
                searchParams = taskService.CreateSearchParams()
                    ?? throw new UserException("IEDMS_TaskService.CreateSearchParams returned null.");
                searchParams.SetForCurrentUser(true);
                searchParams.SetSortField(sortField);
                searchParams.SetItemsPerPage(-1);
                searchParams.SetPageIndex(0);
                searchParams.SetTaskType(TTaskType.ttPartRequest);

                // A ref argument of type IEDMS_TaskSearchParams selects the typed (extension) overload; the
                // interface's own overload takes ref object.
                ok = taskService.GetPartRequestTasks(out tasks, out errorMsg, ref searchParams);
            }
            catch (COMException ex)
            {
                throw new UserException($"Task search failed: {ex.Message} (0x{ex.HResult:X8})");
            }
            if (!ok)
                throw new UserException("GetPartRequestTasks failed: " + (string.IsNullOrEmpty(errorMsg) ? "(no error message)" : errorMsg));

            int count = tasks?.GetCount() ?? 0;
            if (count == 0)
                return "No part requests for the current user.";

            var text = new StringBuilder();
            text.AppendLine($"{count} part request(s) for the current user (sorted by '{sortField}', " +
                            $"total records reported by the search: {TotalRecords(searchParams)}):");
            int shown = Math.Min(count, max);
            for (int i = 0; i < shown; i++)
            {
                text.AppendLine(DescribeTask(tasks, i, resolveNames ? serverManager : null));
                if (i > 0 && i % 100 == 0)
                    ctx.Log($"{i} of {shown} tasks read");
            }
            if (count > shown)
                text.AppendLine($"... {count - shown} more (raise Max to see them).");
            return text.ToString().TrimEnd();
        }

        // One line per task. A COM error while reading one task is reported in its line, not as a failure of the command.
        private static string DescribeTask(IInterfaceList tasks, int i, IEDMS_DXPServerManager names)
        {
            try
            {
                object item = tasks.Get(i);
                // IInterfaceList.Get returns object: the cast is a COM QueryInterface. IEDMS_TaskExt is the expected
                // type; IEDMS_TaskBase (its base) is the fallback, since the fields read here are on it.
                IEDMS_TaskBase task = item as IEDMS_TaskExt ?? item as IEDMS_TaskBase;
                if (task == null)
                    return $"[{i}] skipped: item is {(item == null ? "null" : "not an IEDMS_TaskBase")}";

                string assignee = task.GetAssigneeGUID();
                string assigneeText = string.IsNullOrEmpty(assignee) ? "(none)" : assignee;
                if (names != null && !string.IsNullOrEmpty(assignee))
                    assigneeText += $" '{names.GetUserNameByGUID(assignee)}'";

                string kind = item is IEDMS_TaskExt ? "" : " (IEDMS_TaskBase only)";
                return $"[{i}] {task.GetHRID()} GUID {task.GetGUID()} state {task.GetTaskState()}{kind}: " +
                       $"'{OneLine(task.GetDescription())}' assignee {assigneeText}";
            }
            catch (COMException ex)
            {
                return $"[{i}] error: {ex.Message} (0x{ex.HResult:X8})";
            }
        }

        // Whether the service fills in the total on the search parameters is not documented; reported as read.
        private static string TotalRecords(IEDMS_TaskSearchParams searchParams)
        {
            try { return searchParams.GetTotalRecords().ToString(CultureInfo.InvariantCulture); }
            catch (COMException ex) { return $"error 0x{ex.HResult:X8}"; }
        }

        private static string OneLine(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();

        private static int PositiveInt(CommandContext ctx, string name, int defaultValue)
        {
            string value = ctx.Get(name);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
                throw new UserException($"{name}='{value}' must be a whole number of at least 1.");
            return n;
        }
    }
}
