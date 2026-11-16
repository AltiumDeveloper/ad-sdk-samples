# Generate ODB++ files from the project

Takes the focused project's configured ODB++ outputer from the project model and generates its files into a
folder, without opening an Output Job document. It shows how to reach an outputer through `EDP.IProject` by its
output type and run it with one call.

## What it shows
- `DXP.IClient.GetDXPWorkspace` cast to `EDP.IWorkspace`, then `EDP.IWorkspace.DM_FocusedProject`: the project
  the user is working in. Both are extension methods: they need `using DXP;` and `using EDP;`.
- `EDP.IProject.DM_Outputers`: returns a configured outputer of the project by its output type (here `ODB`), or
  null. The type, not the output's own name, is matched (the same holds for `Pick Place`, see
  ProjectPickPlaceOutput).
- `EDP.IOutputer.DM_Generate_OutputFilesTo`: generates the outputer's files into a given folder. It returns only
  a success flag, so the sample compares the folder (recursively) before and after to list what was written.
- `EDP.IOutputer.DM_GetDescription`, `DM_GeneratorName`, `DM_GetDocumentPath`, `DM_GetState_VariantName`: what
  the outputer is and what it generates from.
- `EDP.IProject.DM_Compile` (optional, `Compile=true`): compile the project before generating.

## Run it
1. Open a PCB project that has an ODB++ output configured and focus one of its documents, for example the
   SpiritLevel-SL1 example project that ships with Altium Designer: its `.PrjPcb` has an output of type `ODB`
   named `ODB++ Files` in the Fabrication Outputs group.
2. **ProjectOdbOutput › Generate**, or `tools/ad.ps1 -Sample ProjectOdbOutput run Generate "Out=<folder>"`
3. The result names the project, the outputer, the success flag, the output folder and the files written,
   grouped by top-level entry (an ODB++ job is a folder tree, so each top-level folder is shown with its file
   count). Without an `ODB` outputer the command fails with `<project> has no 'ODB' outputer`.

| Command | Parameters | Result |
|---|---|---|
| `Generate` | `Out=<folder>` (default `%TEMP%\ProjectOdbOutput`), `Outputer=<type>` (default `ODB`), `Overrides=<text>` (default empty, passed unchanged as the second argument of `DM_Generate_OutputFilesTo`), `Compile=true` (optional) | Project, outputer description/generator/document/variant, `DM_Generate_OutputFilesTo returned <bool>`, output folder, `Files written: n` and the top-level entries written |

## How it works
`ProjectOdbOutputCommands.cs` reads the parameters, gets the workspace from `DXP.GlobalVars.Client` and the
focused project from it, and fails with a `UserException` when there is none. With `Compile=true` it calls
`DM_Compile` first. It asks the project for the outputer with `DM_Outputers(<type>)` and fails clearly if the
result is null. It creates the output folder, records the files already under it, calls
`DM_Generate_OutputFilesTo(<folder>, <overrides>)`, and returns the outputer's properties, the returned flag and
the files that are new or changed under the folder, summarised per top-level entry.

## Notes
- `DM_Outputers` matches the output **type** (`ODB`), not the output's own name: with SpiritLevel-SL1,
  `Outputer=ODB++ Files` fails like an unknown type, and an unknown type returns null rather than throwing. The
  outputer found reports description `ODB++ Files`, generator `ODB`, document `[PCB Document]`, variant
  `[No Variations]`.
- For SpiritLevel-SL1, `DM_Generate_OutputFilesTo` returns `True` and writes 68 files in about 10 s: the ODB++
  job tree in a sub-folder `odb\` of the given folder (`fonts`, `input`, `matrix`, `misc`, `steps\pcb\...`,
  `symbols`), next to it the `.tgz` and `.zip` archives, a `.REP` report, a netlist `.rep` and
  `Status Report.Txt`. No dialog appears, nothing is opened in Altium Designer (the project's `OpenOutputs=1`
  does not apply here), and the project does not have to be compiled first: `Compile=true` produces the
  identical file set.
- The format of the second argument of `DM_Generate_OutputFilesTo` (parameter overrides) is not documented. The
  sample passes it through and defaults to an empty string.
- The command writes files only: the project and its documents are not changed (unless `Compile=true`, which
  only compiles). Files already under the output folder and not touched by the generation are not listed.
