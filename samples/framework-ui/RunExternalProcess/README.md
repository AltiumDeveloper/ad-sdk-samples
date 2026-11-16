# Run an external program and wait for it to exit

Starts a program you name from an extension command, waits for it to exit (killing it when a timeout expires) and
returns its exit code and captured output. It shows that launching an external tool from Altium Designer is plain
.NET (`System.Diagnostics.Process`), and what waiting for it costs: the command blocks until the program ends.

## What it shows
- `System.Diagnostics.Process` with `ProcessStartInfo` (`UseShellExecute = false`, `CreateNoWindow = true`,
  redirected standard output and error): start a console program without a window flashing over Altium Designer.
- `Process.BeginOutputReadLine` / `BeginErrorReadLine`: read both streams asynchronously, so a program that writes
  a lot cannot deadlock against a full pipe buffer.
- `Process.WaitForExit(TimeSpan)` and `Process.Kill(entireProcessTree: true)`: wait a bounded time, because the
  command runs inside Altium Designer. The whole command returns within `Timeout` + 3 s.
- Why not the parameterless `Process.WaitForExit()`: it also waits for end of file on the redirected streams, which
  never comes while a process the program started still holds the pipes. The sample waits for the end-of-file events
  of `OutputDataReceived` / `ErrorDataReceived` (`e.Data == null`) for a short, bounded time instead.
- Command parameters (`Exe`, `Args`, `Timeout`, `WorkDir`) read with `ctx.Get`, and expected failures (no program,
  program not found, bad timeout) reported with `UserException`.

## Run it
1. No document is needed.
2. `tools/ad.ps1 -Sample RunExternalProcess run RunExternalProcess "Exe=cmd.exe|Args=/c echo hello& exit 3"`.
   The menu item **RunExternalProcess › Run External Process** passes no parameters, so it only shows the usage
   message.
3. The result is `'cmd.exe' exited with code 3 after <n> s.`, followed by `stdout:` and `hello`.

| Command | Parameters | Result |
|---|---|---|
| `RunExternalProcess` | `Exe=<path or name>` (required), `Args=<arguments>`, `Timeout=<seconds, 1-600, default 30>`, `WorkDir=<existing folder>` | `'<exe>' exited with code <n> after <s> s.`, then the captured `stdout:` and `stderr:` text (each cut to 4000 characters), or `... did not exit within <t> s and was killed ...` |

## How it works
`RunExternalProcessCommands.cs` checks the parameters first: `Exe` is required, `Timeout` must be a whole number from
1 to 600, and `WorkDir` must exist. It builds a `ProcessStartInfo` without the shell, so the output can be
redirected. It subscribes to `OutputDataReceived` / `ErrorDataReceived`, which collect lines into a size-limited,
thread-safe buffer, and calls `Start`. A `Win32Exception` from `Start` (for example a program that is not found)
becomes a `UserException`. It then calls `BeginOutputReadLine` / `BeginErrorReadLine` and `WaitForExit(timeout)`.
If the program is still running at the timeout, `Kill(entireProcessTree: true)` ends it and its children; a
`Win32Exception` or `AggregateException` from the kill is reported in the result text. A single 3 s grace period
then covers the rest: the end of a killed process (`WaitForExit(TimeSpan)` again) and the end of both streams (each
event handler sets a `ManualResetEventSlim` when it receives `e.Data == null`). The timed `WaitForExit` does not wait
for the streams, the parameterless one would wait without limit, hence the events. The command returns the exit code
(or "unknown" if a killed process did not end within the grace period), the elapsed time until the process ended
(measured before the output drain) and the captured text, with an "output may be incomplete" note when a stream did
not end in time.

## Notes
- The command launches whatever `Exe` names, with your rights. It has no default program on purpose.
- Altium Designer calls commands on its main thread (docs: Runtime and hosting, "The main thread"), so while the
  program runs, the command holds that thread for up to `Timeout` seconds. A command called through `tools/ad.ps1`
  also runs on AD's main window thread (an STA thread with a Windows Forms synchronization context). During an 8 s
  wait AD still answers window messages sent to it and Windows does not mark it as hung (the .NET wait on an STA
  thread pumps sent messages).
- Without `WorkDir` the program starts in Altium Designer's current folder, which is its install folder, not the
  project folder. Pass `WorkDir` when the program reads or writes relative paths.
- `|` separates parameters, so `Args` cannot contain a `|` (no shell pipes). Use `&` to chain `cmd /c` commands.
- Output is decoded with the default console encoding of .NET. Programs that write in another code page may show
  wrong characters for non-ASCII text.
- The exit code of a killed program is whatever Windows reports for a terminated process (-1 for `ping.exe` killed
  at the timeout). The sample does not interpret it.
- A program that starts another process and exits without waiting for it (for example
  `cmd.exe /c start /b ping -n 20 127.0.0.1 >nul & exit 0`) passes its output pipes on to that process. The command
  returns about 3 s after the program exits, with the exit code, what was captured so far and the note
  `(output may be incomplete: a process the program started still holds the output pipes; it was left running)`.
  That process keeps running: only the timeout path kills (the tree of a program that is still running), and the
  command does not hunt for other processes to stop. Its later output is not returned.
- A program that opens a window (for example `notepad.exe`) stays visible and blocks the command until it is
  closed or the timeout kills it.
- The sample uses no Altium Designer SDK type. The extension skeleton (`PluginFactory`,
  `PluginServerModule`, `.ins`/`.rcs`) is supplied by this repository's shared framework.
