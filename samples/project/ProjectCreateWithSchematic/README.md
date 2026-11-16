# Create a PCB project with a blank schematic

Creates a new PCB project, adds a blank schematic to it and opens the schematic. It shows how an extension runs
a built-in workspace process when no API method exists for the task (there is no "create project" method on
`EDP.IWorkspace`), and how to create a document and add it to a project.

## What it shows
- `DXP.IClient.StartServer`: start the schematic server (`SCH`) when `SCH.GlobalVars.SchServer` is still null.
- `DXP.Utils.MessageRouterSendCommandToModule`: send the process `WorkspaceManager:OpenObject` with
  `Kind=PcbProject|ObjectKind=NewAnything` to create a blank PCB project.
- `EDP.IWorkspace.DM_FocusedProject`: the project that is focused after that process. The sample checks that it
  is a new one before changing it.
- `EDP.IWorkspace.DM_CreateNewDocument`: create a blank schematic (`"SCH"`). It returns the new document's
  name (`Sheet1.SchDoc`: an unsaved document has no folder yet) and adds it to the focused project.
- `EDP.IProject.DM_AddSourceDocument`: add that document to the project as a source document.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument` (extension methods, `using DXP;`): open and show the
  schematic.

## Run it
1. No document needs to be open. Any open projects stay untouched.
2. **ProjectCreateWithSchematic › Create Project**, or `tools/ad.ps1 -Sample ProjectCreateWithSchematic run CreateProject`
3. A new PCB project appears in the Projects panel with a blank schematic, which becomes the active document.
   The returned text gives the project and schematic paths, the project count before and after, and the
   project's document count after each step.

| Command | Parameters | Result |
|---|---|---|
| `CreateProject` | none | `Created project '<project>' with schematic '<sheet>'.` plus the project and document counts |

## How it works
`ProjectCreateWithSchematicCommands.cs` starts the `SCH` server if it isn't running (and stops with an error if
`StartServer` fails), then gets the workspace with `GetDXPWorkspace() as IWorkspace`. It records the project
count (`DM_ProjectCount`) and the paths of the open projects (`DM_Projects`, `DM_ProjectFullPath`) and sends
`WorkspaceManager:OpenObject`. It then reads `DM_FocusedProject`: if no project is focused, or the focused one
was already open, it stops with an error rather than add a schematic to a project the user already had.
Otherwise it calls `DM_CreateNewDocument("SCH")`, adds the returned name with `DM_AddSourceDocument`, and opens
it with `OpenDocument("SCH", name)` and `ShowDocument`. It reads `DM_LogicalDocumentCount` before and after each step
and reports the counts.

## Notes
- Nothing is saved: the project and schematic exist only in memory until you save them. AD chooses their
  names and folder.
- `WorkspaceManager:OpenObject` is a process string, not an API, and the SDK reference doesn't describe it.
  It creates the project and focuses it before the call returns, with no dialog. The unsaved project has only a
  name (`PCB_Project1.PrjPCB`), no folder.
- `DM_CreateNewDocument` already adds the new document to the focused project (count 0 → 1), so the
  `DM_AddSourceDocument` call adds nothing (1 → 1, one entry in the Projects panel).
- Both "Projects open" counts come from `DM_ProjectCount`, read before and after the process. The count includes
  the Free Documents project (`DM_Projects(0)`, path `Free Documents`), so it is one higher than the projects in
  the Projects panel; the +1 difference is what matters. New projects are appended at the end.
- Because the unsaved project and schematic have bare names and no folder, close them by exact name, not by
  folder: other unsaved documents (such as AD's Home page) also have bare names.
- The command changes the workspace and can't be undone with Undo. Close the new project without saving to
  discard it.
