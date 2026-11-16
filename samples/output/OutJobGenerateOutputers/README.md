# Generate the files of every outputer in an Output Job

Opens each Output Job (.OutJob) of the focused project and calls `EDP.IOutputer.DM_Generate_OutputFiles` on its
outputers one by one, reporting each outputer's success flag and the file names it returns. It shows how to reach
the outputers of an OutJob directly, without running a whole output medium.

## What it shows
- `EDP.IWorkspace.DM_FocusedProject`, `EDP.IProject.DM_NeedsCompile` / `DM_Compile`: compile the focused project
  only when it needs it, before its outputs are generated.
- `EDP.IProject.DM_LogicalDocumentCount` / `DM_LogicalDocuments` with `EDP.IDocument.DM_DocumentKind`: find the
  project's documents of kind `OUTPUTJOB`.
- `DXP.IClient.OpenDocument` / `DXP.IClient.ShowDocument`: open the OutJob as a server document (and optionally
  show it), then cast it to `EDP.IWSM_OutputJobDocument`.
- `EDP.IWSM_OutputJobDocument.OutputerCount` / `Outputer`: every outputer of the OutJob by index, independent of
  the output media.
- `EDP.IOutputer.DM_GeneratorName` / `DM_GetDescription` / `DM_GetDocumentPath`: identify an outputer.
- `EDP.IOutputer.DM_Generate_OutputFiles(ref string)`: generate one outputer's output. The .NET form returns a
  success flag and fills the string with the generated file names.

## Run it
1. Open a project that contains an `.OutJob`, for example a copy of the SpiritLevel-SL1 example project that ships
   with Altium Designer (it has `3D Prints.OutJob`), and make it the focused project.
2. `tools/ad.ps1 -Sample OutJobGenerateOutputers run GenerateOutputers "DryRun=true"` to list the outputers, then
   `tools/ad.ps1 -Sample OutJobGenerateOutputers run GenerateOutputers "Generator=<name>"` for one of them, or
   **OutJobGenerateOutputers › Generate Outputers** to generate every outputer.
3. The result lists each OutJob and outputer with `generated` / `returned false` and the returned file names,
   then the files that appeared or changed under the project folder.

| Command | Parameters | Result |
|---|---|---|
| `GenerateOutputers` | `Job=<part of the OutJob file name>` (optional), `Generator=<names>` (`;`-separated generator names or outputer descriptions, optional), `Show=true` (show each OutJob), `DryRun=true` (list only) | Per outputer: `[i] '<description>' generator '<name>': generated, files '<returned string>'`, counts, and the files written under the project folder |

## How it works
`OutJobGenerateOutputersCommands.cs` gets the focused project from the DXP workspace and compiles it if
`DM_NeedsCompile` says so. It collects the logical documents whose kind is `OUTPUTJOB` (filtered by `Job`), opens
each with `IClient.OpenDocument("OUTPUTJOB", path)` and casts the server document to `IWSM_OutputJobDocument`. For
each index below `GetState_OutputerCount()` it reads the outputer with `GetState_Outputer(i)` **into a list first**
(generating reorders the collection: see Notes), then skips the ones that do not match `Generator` and calls
`DM_Generate_OutputFiles(ref files)` on the rest. A `COMException` from one outputer is
reported and the loop goes on. Because the outputer writes wherever it is configured to, the command compares the
project folder before and after and lists the new or changed files.

## Notes
- Not read-only: each outputer writes to its own configured output location (for the SpiritLevel-SL1 OutJob, its
  `Unit Output files` folder next to the project, not `Project Outputs`). Run it on a copy of a project.
- **The index order of `GetState_Outputer(i)` is not stable**: generating an outputer reorders the collection.
  A loop that calls `GetState_Outputer(j)` and generates in the same pass can generate one outputer twice and never
  reach another. Take the references in one pass, then generate from that snapshot, as this
  sample does.
- `OutputerCount` is the number of `OutputTypeN` entries in the `.OutJob` file, **including** outputers that are
  disabled for every medium (`OutputEnabled…=0`), and those generate too when driven directly.
- `DM_Generate_OutputFiles` returns `true` on success and sets the `ref` string to a comma-separated list of
  absolute file names, each in double quotes; the value passed in is ignored. The generator's `Status Report.Txt`
  is part of the list.
- Generating does not mark the OutJob document modified.
- The command works on the **focused** project, which follows the focused document: opening a project is not
  enough, a document of it must be open/active (otherwise `DM_FocusedProject` returns the `Free Documents`
  pseudo-project). Generating report outputers opens their `.REP` / `Status Report.Txt` as free documents, which
  moves the focus away again.
- Without `Generator`, every outputer of every OutJob runs, including print outputers
  (`PCB Print`, `Schematic Print`, `PCB 3D Print`). A modal dialog opened by one of them would block a call made
  through `tools/ad.ps1`. Use `DryRun=true` first and
  pick outputers with `Generator`.
- OutJobs opened by the command stay open and are not saved. The result
  says for each one whether it was already open.
- Altium Designer's internal VCL forms (such as the OutJob editor's `JobManagerForm.MainGrid`) are not reachable
  from a .NET extension.
- The folder comparison stops after 20000 files.
