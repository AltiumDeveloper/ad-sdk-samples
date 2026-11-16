# Cycle the active physical sheet of each schematic

Compiles the focused project, opens each of its schematics and makes every physical sheet of the schematic active in
turn, so a repeated (multi-channel) sheet shows each channel once. It shows how the compiled project lists physical
documents and how `EDP.ILogicalDocument` selects which physical sheet an open schematic displays.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`: the project the commands work on. `EDP.IProject.DM_ProjectFileName` detects
  the Free Documents pseudo project.
- `EDP.IProject.DM_CompileEx`: the physical documents of a multi-channel design exist only after a compile.
- `EDP.IProject.DM_PhysicalDocumentCount` / `EDP.IProject.DM_PhysicalDocuments`: the expanded (physical) documents.
  `EDP.IDocument.DM_DocumentKind`, `EDP.IDocument.DM_FullPath`, `EDP.IDocument.DM_PhysicalInstanceName`,
  `EDP.IDocument.DM_PhysicalInstancePath` and `EDP.IDocument.DM_ChannelIndex` describe each entry.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument`: open a schematic and bring it to the front.
  `DXP.IClient.GetDocumentByPath` finds an open document without opening it.
- `EDP.ILogicalDocument.GetPhysicalDocumentCount`, `EDP.ILogicalDocument.GetPhysicalDocuments` and
  `EDP.ILogicalDocument.ActivePhysicalDocument`: the physical sheets of an open schematic and the active one. The
  server document is cast to `EDP.ILogicalDocument`, the .NET form of an `IDispatch(ServerDocument)` cast.
- `DXP.IClient.BroadcastNotification` with a `DXP.ViewNotification` (code 45): the editor-tab update
  notification, optional (`Notify=true`).

## Run it
The extension broadcasts a view notification, so deploy it with `tools/deploy.ps1 -Sample ChangePhysicalSheet` and
restart Altium Designer before the first run.

1. Open a project whose schematic repeats a sheet, and focus it. Bluetooth Sentinel, an example project that ships
   with Altium Designer, works: `Visual_LEDx6.SchDoc` places `Red_LED.SchDoc` six times (six physical sheets,
   `Red_LED1` … `Red_LED6`). Opening the project file alone may not focus it; open one of its documents.
2. **ChangePhysicalSheet › Cycle Physical Sheets**, or `tools/ad.ps1 -Sample ChangePhysicalSheet run CyclePhysicalSheets`
3. Every schematic of the project is opened. The result lists, per schematic, its physical sheets, each switch with
   the active sheet read back, the restored sheet, and a `Done: <n> switches, <m> view notifications ...` line.
   `ListPhysicalSheets` then shows the same sheets without changing anything. In a project without a repeated
   sheet every schematic has one physical sheet, so there is nothing to cycle.

| Command | Parameters | Result |
|---|---|---|
| `ListPhysicalSheets` | `Compile=false` (optional: skip the compile) | The project's physical documents (`[i] SCH <file> instance=... path=... channel=...`), then for each open schematic its physical sheets and the active one; closed schematics are listed as `not open`. Compiles the project (unless `Compile=false`) but opens and changes no document |
| `CyclePhysicalSheets` | `Notify=true` (optional: broadcast the view notification after each switch), `Restore=false` (optional: leave the last sheet active), `Compile=false` (optional) | One block per schematic: `<file>: <n> physical sheets, active before '<name>'`, one `set '<name>' -> active '<name>'` line per switch, `restored -> active '<name>'`, and the `Done:` summary |

## How it works
`ChangePhysicalSheetCommands.cs`: both commands get the focused project (`FocusedProject`), refuse Free Documents and
compile it with `DM_CompileEx(true, ref cancelled)` unless `Compile=false`. `SchematicPaths` walks
`DM_PhysicalDocuments`, keeps the SCH entries and removes duplicate paths, because a repeated sheet appears once per
channel.

`CyclePhysicalSheets` opens each path with `OpenDocument("SCH", path)` and `ShowDocument`, casts the server document to
`ILogicalDocument`, reads the physical sheet names with `GetPhysicalDocuments(j)` and the active one with
`GetActivePhysicalDocument`. It passes each name back to `SetActivePhysicalDocument` and reads the active sheet
again. With `Notify=true` it broadcasts `new ViewNotification(45)` after every switch. At the end it makes the
original sheet active again (unless `Restore=false`). A `COMException` from an `ILogicalDocument` call is reported
for that schematic and the next one is processed.

`ListPhysicalSheets` prints the physical document list and, for schematics already open (`GetDocumentByPath`), the
same `ILogicalDocument` data without setting anything.

## Notes
- An SCH `DXP.IServerDocument` answers the `EDP.ILogicalDocument` members after a plain
  cast. Both interfaces are IDispatch-based in the .NET SDK, so the cast itself
  always succeeds and a mismatch would only show as a `COMException` on the first call.
- Physical sheet names: a repeated sheet gives `<sheet symbol designator><1..n>` (`Red_LED1` … `Red_LED6`), a
  sheet placed once gives the designator (`Wireless`, `Q1`, `Q2`). `GetActivePhysicalDocument` returns an **empty
  string** on a freshly opened schematic, so the default `Restore=true` has nothing to restore on the first run.
  `GetDocumentByPath` returns null for a document that is not open.
- `IProject.DM_PhysicalDocuments` lists the six `Red_LED.SchDoc` channels as six **identical** entries:
  `DM_PhysicalInstanceName`, `DM_PhysicalInstancePath` and `DM_ChannelIndex` (0 for every entry) do not tell the
  channels apart. Only `ILogicalDocument` gives the per-channel names.
- `Notify` is off by default. Dozens of `IClient.BroadcastNotification(new
  ViewNotification(45))` calls in one command raise no error dialog and leave AD responsive. The view notification
  is the SDK's own `DXP.ViewNotification` class, so the sample defines no COM-visible class of its own.
  `DXP.NotificationCode.ViewNotificationCode` has no constant for 45; the number is that of
  `cViewNotification_EditorTabUpdate`.
- The switches run without a pause, so in AD only the final state is visible. The returned text shows each switch.
- The command compiles the project and opens documents, but does not modify any file. Switching the
  active physical sheet does not mark the schematic as modified.
- The sample needs no code in the extension's main assembly: the notification object comes from the SDK and is
  created in the implementation.
