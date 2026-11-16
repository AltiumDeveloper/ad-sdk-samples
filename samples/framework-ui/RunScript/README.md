# Run a procedure of an existing script

Runs a named procedure of an existing script project or script file from an extension command. It shows that an
extension needs no scripting API for this: it launches the built-in process `ScriptingSystem:RunScript` with
`DXP.Utils.RunCommand` and a `Key=Value|Key=Value` parameter string.

**Needs a human at Altium Designer.** If the script engine cannot find the script, it shows a modal error box
inside the call, and Altium Designer stays blocked until someone clicks OK.

## What it shows
- `DXP.Utils.RunCommand`: launch any built-in process by its `<Server>:<Process>` id with a parameter string. It
  returns nothing and raises nothing when the process fails; a dialog the process shows keeps the call from
  returning until it is closed. Validate the inputs before the call.
- The `ScriptingSystem:RunScript` parameters: `ProjectName=<.PrjScr>` or `FileName=<script file>`, plus
  `ProcName=<procedure>`. They are not in the SDK reference.
- Command parameters (`ProjectName`, `FileName`, `ProcName`, `Out`), read with `ctx.Get`.

## Run it
1. No document is needed. Run **RunScript › Write Demo Script**, or
   `tools/ad.ps1 -Sample RunScript run WriteDemoScript "Out=<folder>"`. It writes `RunScriptDemo.PrjScr` and
   `RunScriptDemo.pas` (to the temp folder by default), deletes an old `RunScriptDemo.txt` there, and returns the
   `RunScript` call to try.
2. Be at the Altium Designer screen, then
   `tools/ad.ps1 -Sample RunScript run RunScript "ProjectName=<folder>\RunScriptDemo.PrjScr|ProcName=RunScriptDemo"`.
3. The returned text says `Ran ScriptingSystem:RunScript for RunScriptDemo in 'RunScriptDemo.PrjScr'.` and
   `RunScriptDemo.txt` (containing `RunScriptDemo ran`) now exists next to the script. Altium Designer also opens
   `RunScriptDemo.PrjScr` in the workspace and leaves it open. If an `Error` box "Program cannot find script ..."
   appears instead, click OK: the script was not found and nothing ran.
4. With the project open, `RunScript "FileName=<folder>\RunScriptDemo.pas|ProcName=RunScriptDemo"` (the second
   form) runs it again.

| Command | Parameters | Result |
|---|---|---|
| `RunScript` | `ProcName=<procedure>` (required, an identifier), and `ProjectName=<.PrjScr>` or `FileName=<script file>` (or both) | `Ran ScriptingSystem:RunScript for <proc> in '<file>'.`, one line per form run; input errors as text before any call |
| `WriteDemoScript` | `Out=<existing folder>` (default: the temp folder) | Helper: writes `RunScriptDemo.PrjScr` and `RunScriptDemo.pas`, deletes an existing `RunScriptDemo.txt`, returns the `RunScript` call to try |

## How it works
`RunScriptCommands.cs` holds both commands. `RunScript` checks that `ProcName` is an identifier (which also keeps
`|` and `=` out of the parameter string) and that the named files exist. It then builds one parameter string per
form, `ProjectName=<path>|ProcName=<proc>` and/or `FileName=<path>|ProcName=<proc>`, and passes each to
`DXP.Utils.RunCommand("ScriptingSystem:RunScript", ...)`. With both parameters the project form runs first.
`WriteDemoScript` deletes an old `RunScriptDemo.txt`, writes a DelphiScript whose single procedure
`RunScriptDemo` saves that file with a `TStringList`, and writes a minimal script project that lists it. The
project keeps only the `[Design]` `Version=1.0` header and the `[Document1]` `DocumentPath=` entry of a
`.PrjScr` saved by Altium Designer, in UTF-8 with a byte order mark as Altium Designer writes it.

## Notes
- When `ScriptingSystem:RunScript` cannot find the script, it shows a modal `Error` box ("Program cannot find
  script with following items: Project name '', Module name '', Procedure name '<proc>'.") inside
  `DXP.Utils.RunCommand`. The call does not return, and raises no exception, until OK is clicked, so the command
  hangs and Altium Designer is blocked. The sample cannot detect this beforehand; it only checks that the files
  exist and that `ProcName` is an identifier.
- `ProjectName=<.PrjScr>|ProcName=<proc>` runs the procedure of a script project that was not open; Altium
  Designer opens the project in the workspace and leaves it open. The minimal two-section `RunScriptDemo.PrjScr`
  is accepted, and the generated DelphiScript runs as written.
- `RunCommand` returns after the script has finished (the script's output file exists as soon as the call
  returns).
- `FileName=<full path of a .pas>` works when the script belongs to an open script project. When the script is not
  open and not part of an open script project, it is not found: the blocking `Error` box appears, with project
  and module name empty.
- Not known: whether a missing procedure in a found script shows the same blocking box.
- The sample runs whatever script code the caller names, so `RunScript` is not read-only: the script decides what
  it changes.
- `ProcName` must be a plain identifier; a qualified name (for example `Unit.Proc`) is rejected. Whether
  `ScriptingSystem:RunScript` needs or accepts qualified names is not known.
- The demo script is written as UTF-8 without a byte order mark. How the script engine reads a non-ASCII path
  inside it is not known.
- Paths cannot contain `|`, the parameter separator (Windows paths cannot anyway).
