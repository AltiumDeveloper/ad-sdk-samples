# Apply a schematic sheet template to the active sheet

Applies a sheet template (`.SchDot`) to the active schematic sheet by running AD's own `Sch:ChangeCurrentTemplate`
process. It shows how to read the schematic template folder from the workspace preferences, and how to run a
built-in process with `DXP.Utils.RunCommand` and check its effect on the document.

## What it shows
- `EDP.IWorkspace.DM_Preferences` / `EDP.IWorkspacePreferences.GetTemplatePath_Sch`: the schematic template folder
  set in the workspace preferences. The workspace comes from `DXP.GlobalVars.Client.GetDXPWorkspace()` (an extension
  method, so it needs `using DXP;`), with no P/Invoke into `WorkspaceManager.dll`.
- `DXP.Utils.RunCommand`: runs the `Sch:ChangeCurrentTemplate` process with `FileName=<path>|Silent=True`. The
  2-argument form acts on the focused document. The process name and its parameters are not in the SDK
  reference.
- `SCH.ISch_Document.TemplateFileName`: read before and after, because `RunCommand` returns nothing.
- `SCH.ISch_ServerInterface.GetCurrentSchDocument` and `SCH.ISch_BasicContainer.ObjectId`: the active schematic
  document, and a check that it is a sheet, not a library.

## Run it
1. Open a schematic sheet, for example a copy of `SL_Power.SchDoc` from the SpiritLevel-SL1 example that ships with
   Altium Designer, and make it the active document.
2. **ChangeSchTemplate › Change Template**, or
   `tools/ad.ps1 -Sample ChangeSchTemplate run ChangeTemplate "Template=ISO A3 Landscape.SchDot"`
3. The command returns the sheet's template file name (a full path) before and after, and the sheet shows the new
   title block. The document is marked modified but is not saved.

**Pass a `Template=`**: the default is `A4.SchDot`, and Altium Designer does not ship that file. Run the
command once without parameters and the error lists the `.SchDot` files that *are* in the template folder (for
example `ANSI B/C/D Landscape.SchDot`, `ISO A2/A3 Landscape.SchDot`).

| Command | Parameters | Result |
|---|---|---|
| `ChangeTemplate` | `Template=<file name or full path>` (default `A4.SchDot`), `Silent=<true\|false>` (default `true`) | `<sheet>: template changed, '<old>' -> '<new>' (applied <path>). Not saved.` |

## How it works
`ChangeSchTemplateCommands.cs` first gets the active document from `SCH.GlobalVars.SchServer` and rejects a
missing document or a schematic library with a `UserException`. `ResolveTemplate` uses a rooted `Template` path
as given. A bare file name is combined with `GetTemplatePath_Sch()` of the workspace preferences. The file must
exist. The command then reads `GetState_TemplateFileName()`, runs `Sch:ChangeCurrentTemplate` through
`DXP.Utils.RunCommand`, and reads the template file name again. It reports `changed` or `unchanged`. If the file
is missing, the error also lists the `.SchDot` files that are in the folder.

## Notes
- The change is made in memory only: the document is marked modified (`*`) and the file on disk is untouched, so
  save it to keep the change.
- `RunCommand` is synchronous here: `TemplateFileName` read immediately after the call already has the new value.
- `Sch:ChangeCurrentTemplate`, `FileName` and `Silent` are not documented in the SDK reference. With `Silent=True`
  no dialog appears. Through `tools/ad.ps1` the sample refuses `Silent=false`, because a modal dialog there would
  block AD; use it from the AD menu.
- `GetTemplatePath_Sch` has no description in the reference. It returns an expanded absolute path,
  `%PUBLIC%\Documents\Altium\<installation>\Templates`. This sample treats an empty result as "no folder configured"
  and asks for a full path.
- The template name read back is a **full path**, and the value stored in a sheet may point at a folder that no
  longer exists (the SpiritLevel-SL1 sheets store a path into an Altium 2004 installation); applying a new
  template over it works. Re-applying the current template is accepted and reports `unchanged`.
- A template file name with spaces needs no quoting in the `Key=Value|Key=Value` parameter string.
- The template is resolved before the `Silent` check, so a missing template is reported first.
