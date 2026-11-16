# Show a data grid with programmatic filters in a form

Opens a modal WinForms form from an extension command: a data-bound grid of sample customers and a combo box of
nine named filters that are applied to the grid by code. It shows how an extension hosts its own form, owned by
an Altium Designer window, and how to keep a modal form off automated (pipe) calls.

## What it shows
- `DXP.GlobalVars.Client` / `DXP.IClient.GetMainWindowHandle`: a window handle of Altium Designer, used as the
  owner of the modal form (see Notes for which window it is).
- `CommandContext.IsInteractive` (sample framework): show the form only when a user started the command; on the
  pipe the same filters run without UI and the result is returned as text.
- `SampleHooks.OnUnloadCore` (sample framework): close a form that is still open when the implementation unloads.
- `System.Data.DataView.RowFilter` behind a `BindingSource`: grid filter criteria in .NET (greater than, LIKE,
  equal, in list, AND/OR groups).

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample GridFilterForm` and Altium Designer restarted.

1. No document is needed. Any editor (or none) can be active.
2. **GridFilterForm › Show UI**, or `tools/ad.ps1 -Sample GridFilterForm run ShowUI` (no form on the pipe).
3. From the menu: a form "GridFilterForm: filter a grid by code" opens with 20 customers, the filter
   "COPIES > 3" preselected and "7 of 20 rows" in the status line. Pick another filter in the combo box, type an
   expression and press Enter, or right-click a cell and choose **Filter by this value**: the combo box then
   shows "Custom filter". Close the form with **Close** or Esc. From the pipe: one line per filter with its row count.

| Command | Parameters | Result |
|---|---|---|
| `ShowUI` | none | Menu: the modal form. Pipe: `20 customers; ...` and one line `<Key>: <rows>  [<RowFilter>]` per filter |
| `ShowUI` | `Filter=<Key>` (`None`, `Custom`, `Simple`, `Like`, `TwoField`, `Between`, `OnlyGrids`, `Group`, `List`), or its description, or its index 0-8 | Menu: the form with that filter preselected. Pipe: the header and the line for that filter only |

An unknown filter fails with `Unknown filter '<value>'` and the list of keys.

Row counts for the built-in data: None 20, Simple 7, Like 5, TwoField 3, Between 10, OnlyGrids 12,
Group 7, List 10.

## How it works
`Commands/GridFilterFormCommands.cs` resolves the optional `Filter` parameter and builds the customer table.
On the pipe (`ctx.IsInteractive` is false) it applies each scenario's `RowFilter` to a `DataView` and returns
the counts. Otherwise it creates `GridFilterDialog` and calls `ShowDialog` with an `IWin32Window` wrapping
`DXP.GlobalVars.Client.GetMainWindowHandle()`. The open form is kept in a static field so that
`SampleHooks.OnUnloadCore` can close it. The command returns no text, so no message box follows the form.

`CustomerData.cs` holds the data (BCL only) and the nine filter scenarios with their descriptions and
product/payment IDs, each expressed as a `RowFilter` expression.

`GridFilterDialog.cs` is the form, built in code. Choosing a scenario replaces the filter (`SetFilter`). A filter
the user builds (expression box or cell context menu) switches the combo box to "Custom filter", or "No filter"
when empty. A `_lock` flag prevents a feedback loop between the combo box and the filter. Payment type and product
columns are lookup columns that show names for the stored IDs.

## Notes
- The shim is the framework's unchanged: a modal form needs no extra shim code, `.ins` or `.rcs` entries.
- The form is modal: AD does not respond until it is closed. That is why the pipe path never shows it.
- Owner window: `GetMainWindowHandle()` and `GetApplicationHandle()` return the **same**
  handle, and it is the invisible, zero-size `TApplication` window of Altium Designer, not the visible main frame.
  It works as the owner of the form, but because its rectangle is a point, `FormStartPosition.CenterParent` does
  not centre the form on the Altium Designer window.
- WinForms runs on AD's main thread: the form opens inside the AD process with its grid, lookup columns and status
  line, and closes cleanly.
- Hot reload: the host defers a reload while `ShowUI` is still running, so a rebuild applies after the form
  closes. `OnUnloadCore` only covers an unload that happens anyway.
- Data: the 20 customers and 11 products are made up and built in code, so the sample needs no data files.
  Four product IDs (3, 7, 9, 14) are grid products, and the payment type values are Cash 1, Visa 2,
  Master Card 3, American Express 4.
- The grid is read-only. Edits to the in-memory data would be lost when the form closes anyway.
- `RowFilter` date literals are `#M/d/yyyy#` whatever the user's locale; LIKE uses `*` as the wildcard. Both
  evaluate the same inside AD.
- A `Filter` description containing spaces can be passed unquoted as a `Key=Value` parameter.
