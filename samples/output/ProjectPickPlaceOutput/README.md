# Generate Pick and Place files from the project

Takes the focused project's configured Pick Place outputer from the project model and generates its files into a
folder, without opening an Output Job document. It shows how to reach an outputer through `EDP.IProject` and run
it with one call.

## What it shows
- `DXP.IClient.GetDXPWorkspace` cast to `EDP.IWorkspace`, then `EDP.IWorkspace.DM_FocusedProject`: the project
  the user is working in. Both are extension methods: they need `using DXP;` and `using EDP;`.
- `EDP.IProject.DM_Outputers`: returns a configured outputer of the project by its output type (here
  `Pick Place`, matched case-insensitively), or null. The output's own name (for example
  `Generates pick and place files`) is not matched.
- `EDP.IOutputer.DM_Generate_OutputFilesTo`: generates the outputer's files into a given folder. It returns only
  a success flag, so the sample compares the folder before and after to list the files written.
- `EDP.IOutputer.DM_GetDescription`, `DM_GeneratorName`, `DM_GetDocumentPath`, `DM_GetState_VariantName`: what
  the outputer is and what it generates from.
- `EDP.IProject.DM_Compile` (optional, `Compile=true`): compile the project before generating.

## Run it
1. Open a PCB project that has a Pick Place output configured (for example in one of its Output Jobs), and focus
   one of its documents, for example the SpiritLevel-SL1 example project that ships with Altium Designer.
2. **ProjectPickPlaceOutput › Generate**, or
   `tools/ad.ps1 -Sample ProjectPickPlaceOutput run Generate "Out=<folder>"`
3. The result names the outputer, the success flag, the output folder and the files written (for a Pick Place
   output, typically a `.txt` and/or `.csv` file). Without such an outputer the command fails with
   `<project> has no 'Pick Place' outputer`.

| Command | Parameters | Result |
|---|---|---|
| `Generate` | `Out=<folder>` (default `%TEMP%\ProjectPickPlaceOutput`), `Outputer=<name>` (default `Pick Place`), `Overrides=<text>` (default empty, passed unchanged as the second argument of `DM_Generate_OutputFilesTo`), `Compile=true` (optional) | Project, outputer description/generator/document/variant, `DM_Generate_OutputFilesTo returned <bool>`, output folder, `Files written: n` and their names |

## How it works
`ProjectPickPlaceOutputCommands.cs` reads the parameters, gets the workspace from `DXP.GlobalVars.Client` and the
focused project from it, and fails with a `UserException` when there is none. With `Compile=true` it calls
`DM_Compile` first. It asks the project for the outputer with `DM_Outputers(<name>)` and fails clearly if the
result is null. It creates the output folder, records the files already in it, calls
`DM_Generate_OutputFilesTo(<folder>, <overrides>)`, and returns the outputer's properties, the returned flag and
the files that are new or changed in the folder.

## Notes
- `DM_Outputers` matches the output type, not case sensitive, and returns null for an unknown type (no
  exception). With SpiritLevel-SL1, `DM_Generate_OutputFilesTo` returns `True`, needs no compile and shows no
  dialog; besides the Pick Place file (`Pick Place for <board>.txt`, units mil) it writes a
  `Status Report.Txt` into the folder. `DM_GetDescription` returns the output's name, `DM_GeneratorName` its type.
- The SpiritLevel project defines the Pick Place output both in the `.PrjPcb` output groups and in a project
  OutJob; the variant `DM_Outputers` returns matches the `.PrjPcb` entry.
- The reference describes `DM_Outputers` as returning "the indexed Output Generator", but its parameter is a
  name string (`string argName`).
- The format of the second argument of `DM_Generate_OutputFilesTo` (parameter overrides) is not documented. The
  sample passes it through and defaults to an empty string.
- The command writes files only: the project and its documents are not changed (unless `Compile=true`, which
  only compiles). Files already in the output folder and not touched by the generation are not listed.
