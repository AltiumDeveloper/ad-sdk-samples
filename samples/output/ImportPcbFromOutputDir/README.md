# Open the PcbDoc next to an output job's export folder

Takes an output job's export path (a path containing a `Unit Output files` segment), finds the `.PcbDoc` in the
base folder before that segment and opens it. It shows how to open a design document from code with
`DXP.IClient.OpenDocument`, and how an extension declares its own AD process (with its own parameter string) in the
shim, next to the generic `Run`.

## What it shows
- `DXP.IClient.OpenDocument`: open a file as a document of a given kind (`"PCB"`); it returns the
  `DXP.IServerDocument`, or nothing when AD cannot open it.
- `DXP.IClient.GetDocumentByPath`: tell whether the board was already open before the command ran.
- `DXP.IServerDocument.GetKind`: the kind AD gave the opened document.
- `DXP.CommandLauncher.RegisterCommand`: the shim registers the process `ImportPcbFromOutputDir:FileImportDirectory`
  (command name `FileImportDirectory`, under this sample's server), declared in the `.ins` with `Command Name = 'FileImportDirectory'`.
- `EDP.IWorkspace.DM_FocusedProject`, `EDP.IProject.DM_ObjectKindString`, `EDP.IProject.DM_LogicalDocuments`,
  `EDP.IDocument.DM_DocumentKind`: the test for "does the focused project already hold a document of
  this kind".

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample ImportPcbFromOutputDir` and Altium Designer restarted.
1. Have a folder with a `.PcbDoc` (for example a copy of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and create a sub-folder `Unit Output files` in it (an output job's export folder, or one made by hand).
2. `tools/ad.ps1 -Sample ImportPcbFromOutputDir run FileImportDirectory "BaseExportPath=<folder>/Unit Output files/Gerber"`.
   From the menu, **ImportPcbFromOutputDir › File Import Directory** runs the sample's own process without
   parameters and only reports that `BaseExportPath` is needed.
3. The board opens in AD and the command returns `Opened: <board>.PcbDoc (kind PCB) from base folder <folder>.`
4. Focus a project and run `CheckDocumentKind` (`Kind=PCB` to see a match).

| Command | Parameters | Result |
|---|---|---|
| `FileImportDirectory` | `BaseExportPath=<path containing 'Unit Output files'>` (required) | `Opened: <file> (kind <kind>) from base folder <folder>.` or `Already open: ...`; with several boards, how many and that the first by name was taken |
| `CheckDocumentKind` | `Kind=<document kind>` (default `Extension_CAM350`) | `Focused project: <name> (<kind>), <n> logical document(s), <m> of kind '<Kind>' ...`, or a Free Documents message |

The process `ImportPcbFromOutputDir:FileImportDirectory` takes the same `BaseExportPath=...` string and shows the
result in a message box.

## How it works
`ImportPcbFromOutputDirCommands.cs`, `FileImportDirectory`: reads `BaseExportPath`, cuts it at the first
`Unit Output files` (case-insensitive), resolves the rest with `Path.GetFullPath`, lists the `.PcbDoc` files of that
folder (top level, sorted by name) and takes the first. It checks `GetDocumentByPath`, then calls
`OpenDocument("PCB", file)` and reports the document's `GetKind()`. Every failure (no parameter, no segment, no
folder, no board, AD could not open it) is a `UserException` with the reason. The part of the path after the
segment need not exist.

`CheckDocumentKind` gets the focused project from the workspace, returns early for `Free Documents`
(`DM_ObjectKindString`), and counts the logical documents whose `DM_DocumentKind` equals `Kind`.

`FileImportProcess.cs` (shim) registers the extra process `FileImportDirectory` in the template's
`RegisterCommands` hook and forwards its parameters to the implementation through `Commands.Run`, as
`Cmd=FileImportDirectory|<parameters>`. The `.ins` declares it, the `.rcs` menu item calls it.

## Notes
- Opening is real: the board stays open in AD. Nothing is modified or saved.
- `OpenDocument` does **not** make the board the visible, focused document: it is open and AD lists it, but no
  editor tab appears. `IClient.ShowDocument` is what displays it; this sample does not call it.
- `DM_DocumentKind` returns `PCB` and `SCH`, and `DM_ObjectKindString` of a normal project is `PCB Project`.
  `DM_FocusedProject` follows the Projects panel: a project opened from code with `DM_OpenProject` does not become
  the focused one.
- A custom document kind such as `Extension_CAM350` (its files, editor view, menus and `NewDocumentInstance`), and a
  panel view that forwards a board's view notifications to another document (`IServerDocument.AddView` with a
  `CServerDocumentView` subclass), have no .NET counterpart in this shim. The framework's `NewDocumentInstance`
  returns nothing and is not a partial member.
- A "New CAM350 document" command (`WorkspaceManager:OpenObject` with `Kind=Extension_CAM350`) needs that document
  kind, so `CheckDocumentKind` only reports its enable test.
- An `.ins` can hook a command to output formats (`OutputHandler` entries with `Format`, `DocKind`, `ServerName`,
  `Command`). Without a document kind, and with no documentation of when AD calls such a handler, this sample
  declares none. What `BaseExportPath` AD passes to such a handler, and when, is not documented.
