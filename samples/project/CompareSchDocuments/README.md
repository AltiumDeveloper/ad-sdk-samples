# Open the diff viewer on two schematic files

Runs the built-in Altium Designer process `WorkspaceManager:Difference` on two schematic files given as parameters,
so AD opens both sheets and lists their differences in the Differences panel. It shows how to launch any built-in
process from an extension with `DXP.Utils.RunCommand` and an Altium `Key=Value|Key=Value` parameter string.

## What it shows
- `DXP.Utils.RunCommand`: launch a built-in process by its id (`Server:Process`) with a parameter string. The
  call has no return value. An unknown server or process is ignored silently, but an error raised by the target
  server comes back as a .NET exception, so the call must be wrapped in `try`/`catch`. The command turns that
  exception into a `UserException` that names the process and the two files.
- Checking the inputs before the launch (file exists, two different files), because an unknown process is silent
  and the call returns no result.
- Command parameters (`Document1=<path>|Document2=<path>`), read with `ctx.Get`, and a file picker as fallback
  only when a user started the command from the menu (`ctx.IsInteractive`).

## Run it
1. Have two `.SchDoc` files on disk, for example two sheets of the SpiritLevel-SL1 example project that ships with
   Altium Designer. No document needs to be open.
2. **CompareSchDocuments › Compare Sch Files** (pick the two files in the dialogs), or
   `tools/ad.ps1 -Sample CompareSchDocuments run CompareSchFiles "Document1=<folder>/SL_Power.SchDoc|Document2=<folder>/SL_Config_2E.SchDoc"`
3. The command returns `Ran WorkspaceManager:Difference on 'SL_Power.SchDoc' and 'SL_Config_2E.SchDoc'. ...`. AD opens
   both sheets and the Differences panel (for these two sheets: 233 differences, 220 extra and 13 changed objects);
   each difference is also added to the Messages panel as a `Comparator` message.

| Command | Parameters | Result |
|---|---|---|
| `CompareSchFiles` | `Document1=<path>`, `Document2=<path>` (required from `tools/ad.ps1`; file dialogs from the menu) | `Ran WorkspaceManager:Difference on '<file1>' and '<file2>'. ...`, or an error naming the process, the files and the exception when the process fails |

## How it works
`CompareSchDocumentsCommands.cs` resolves both paths: from the parameters, or, when the command was clicked in AD
and a parameter is missing, from an `OpenFileDialog`. Each path is made absolute and must exist; the two must
differ, otherwise a `UserException` explains what to pass. The command then builds
`Document1=<path>|Document2=<path>`, logs it, and calls `DXP.Utils.RunCommand("WorkspaceManager:Difference", ...)`
inside a `try`/`catch`. `RunCommand` has no return value and ignores an unknown process silently, but an error raised
by the target server is rethrown as a .NET exception; the command catches it and throws a `UserException` with the
process id, both full paths, the exception type and message, and the statement that the comparison did not open.
Without an exception it returns the two file names and points to the Differences panel.

## Notes
- The process `VersionControl:CompareFiles` with the same `Document1`/`Document2` parameters does not work: it is in
  no `.ins` or `.rcs` file, `RunCommand` throws `NotSupportedException` for it, and `ICommandLauncher.LaunchCommand`
  with it opens nothing. This sample uses `WorkspaceManager:Difference` (listed in `System/WorkspaceManager.ins` as
  "Show differences between documents"). Neither process is documented in the SDK reference. Whether `Difference`
  takes further parameters, and whether `RunCommand` returns before the comparison has finished, is not known.
- **It can ask first.** The first comparison in a session can show a modal question, whether you want to see the
  differences; after Yes the Differences panel appears. Later runs in the same session go straight to the panel.
  When AD asks is not known, so expect the question and never start this from an unattended script that cannot
  answer it.
- The process opens the two sheets (they stay open) and adds one Messages panel entry per difference.
- The file dialog filters `*.SchDoc` (with an "All files" option).
- Relative paths are resolved against Altium Designer's current directory; pass absolute paths.
- The command changes no design data.
