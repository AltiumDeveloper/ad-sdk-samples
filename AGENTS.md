# AGENTS.md

Rules for anyone — people and coding agents — who adds or changes a sample in this repository. They keep the
samples consistent, trustworthy and findable. When something here conflicts with a habit, follow this file; when
this file is silent, follow the nearest existing sample.

## What this repository is

Complete Altium Designer extensions in C#, one per folder: installation files, a shim that Altium Designer loads,
and an implementation that hot-reloads. Readers are developers writing extensions and language models helping them.
Both copy what they find here, so every sample must build, run, and say only what is true.

The Altium Designer version the samples are built for is stated once, in `README.md` ("Requirements"). Nothing
else in the repository names an Altium Designer version.

The API reference is the [Altium Designer SDK documentation](https://docs.developer.altium.com/ad-sdk-docs/). Its
build takes the index this repository's CI builds (see "The samples index") to describe the samples and to link
every SDK page to the samples that use it.

## Layout

| Path | What | Edited by |
|---|---|---|
| `samples/<category>/<Id>/` | one sample (see below) | hand |
| `framework/` | the extension template's shim and implementation code, shared by every sample | only to follow the template (see "framework/") |
| `Directory.Build.props`, `Directory.Build.targets` | shared build settings: SDK references, target framework, hot reload | rarely, by hand |
| `tools/` | what a sample user runs: `deploy.ps1`, `undeploy.ps1`, `dev.ps1`, `ad.ps1`, `hotreload.ps1`, `new-sample.mjs`, `sync-solution.mjs` | hand |
| `schema/` | `sample.schema.json` (each `sample.json`), `index.schema.json` (the samples index) | hand; versioned (see "The samples index") |
| `eng/` | repository machinery run by CI: `build-index.mjs`, `ApiUsage/`, `export-sdk.ps1` | hand |
| `.github/workflows/samples.yml` | continuous integration; builds and releases the samples index | hand |
| `AdSdkSamples.slnx` | the solution | `node tools/sync-solution.mjs` only |
| `README.md` | front page; the sample table between the `samples-index` markers | hand; the table by `node eng/build-index.mjs` only |

Never committed: `local.props` (your Altium Designer path), build output, `index-out/` (the index CI builds).

## A sample

```
samples/<category>/<Id>/
  <Id>/<Id>.csproj              the shim: what Altium Designer loads (server module, panels, notification handlers)
  <Id>/Installation/            <Id>.ins, <Id>.rcs, <Id>.out when it has outputers
  <Id>.Impl/<Id>.Impl.csproj    the implementation: [HotCommand] methods; reloads on every Debug build
  README.md
  sample.json
```

* **Id**: PascalCase, unique across all categories, says what the sample does (`BoardPadCount`, not `Sample12`).
  It is the folder name, the extension name and the server name.
* **Category**: one of the folders under `samples/` (`schematic`, `pcb`, `library`, `3d-step`, `output`, `project`,
  `framework-ui`, `integration`, `licensing`). Pick where a reader would look first.
* Create one with `node tools/new-sample.mjs --category <category> --name <Id> --command "<Name>|<Description>"`,
  then run `node tools/sync-solution.mjs`.
* **What goes where.** Features are `[HotCommand]` methods in the implementation. Only what Altium Designer must
  hold for the life of the session lives in the shim: the server module, panels, notification handlers, document
  servers, processes with their own GetState. Shim code does not hot-reload: a change there needs a redeploy and a
  restart of Altium Designer.
* **One sample, one job.** A sample shows one technique or one task end to end. Commands that only help to try it
  (list what is there, reset) are fine; say in their description that they are helpers.

### Stay on target

Samples show what extensions are for: working with designs, libraries, projects and outputs, and with Altium
Designer's own user interface. Do not add samples whose point is a technique that fights the host — embedding
another application's window, Win32 tricks around Altium Designer's UI. Use P/Invoke only when the SDK has no way to
do the job, keep it in one `NativeMethods.cs`, and say in the README why it is needed.

## sample.json

Follows `schema/sample.schema.json`; everything in it is written by hand:

* `title` — one line, what the sample does. `summary` — one or two sentences: what a developer learns from it.
* Facets, from closed vocabularies only (the schema lists them; do not invent words):
  **domain** (where in Altium Designer), **task** (1–3: what it does), **surface** (how it hooks in: `command`,
  `panel`, `outputer`, `context-menu`, `document-server`, `notification`).
* `interactive: true` when a command waits for the user in Altium Designer (a pick, a dialog).
  `requiresRestart: true` when it adds something Altium Designer loads at startup (panel, menu, handler, outputer,
  document kind).
* `phrasings` — 2–5 ways a user would ask for this task ("count pads on a board"), not API names and not the title.
* `commands` — one entry per `[HotCommand]`, with the same name; `params` as passed to `tools/ad.ps1`
  (`Key=<meaning>|...`); `readOnly: false` when the command changes a document or Altium Designer state.

Never add the SDK APIs the code uses: CI derives them from the compiled code (see "The samples index").

## README.md of a sample

Sections, in this order, and no others: the title and one intro paragraph, `## What it shows`, `## Run it`,
`## How it works`, `## Notes`.

* **What it shows** — the SDK APIs the sample is about, each as `` `Namespace.Type.Member` ``: what it is for here.
  These names are what the index marks as the sample's primary APIs: list the ones that matter, not the plumbing.
* **Run it** — the document to open (an example project that comes with Altium Designer, or "any `.PcbDoc` with
  components"), the menu path (**`<Id>` › `<Caption>`**), the `tools/ad.ps1 -Sample <Id> run <Command> "<params>"`
  line, and a table of commands, parameters and results. If `requiresRestart`, say once to deploy with
  `tools/deploy.ps1 -Sample <Id>` and restart Altium Designer.
* **How it works** — the path through the code, naming the files.
* **Notes** — only what would hurt to leave out: a call that silently does the wrong thing, a required order or
  bracket, a unit or sentinel value, a limitation. Not what a reader learns by trying it for a minute.

Writing rules, for the README, `sample.json`, code comments and user-visible strings alike:

* **Plain facts.** "`SetComponentReference` moves a footprint without pads by the board origin offset." Not "we
  observed", not "it seems", not "tested on ...".
* **No history.** No earlier versions of the sample, no other examples it was compared with or derived from, no
  "unlike", "as before", "now". A design choice is explained by its own reason.
* **No versions and no test reports.** No Altium Designer version numbers, no test results, no lists of what was or
  was not checked. Compatibility is stated once, in the root README.
* **Nothing from one machine.** No user paths, installation folders, user documents or account names; use
  `%PUBLIC%`, `<folder>` or "an example project that comes with Altium Designer".
* **Exact names.** API members exactly as the SDK names them; US spelling; short sentences.
* **Do not explain workarounds you do not recommend.** Show the recommended way and what to watch out for.

## Code

* **Follow the template's patterns** (`framework/`): `[HotCommand]` methods returning their result as text,
  `CommandContext` for parameters (`ctx.Get`), `UserException` for expected failures (no document open, a missing
  parameter) with a message that says what to do. Use the template's extension points, not workarounds for them.
* **Bracket changes** the way the SDK requires (`PreProcess` / `BeginModify` … `EndModify` / `PostProcess` on PCB
  objects, the robot manager on schematic objects) so they can be undone and are redrawn.
* **Never block Altium Designer from the pipe.** A command that opens a modal dialog refuses to run through
  `tools/ad.ps1` and says so; it runs from its menu item.
* **Hot reload.** Release in `HotEntry.OnUnload` whatever the implementation started (events, timers, forms);
  callbacks handed to native code or to Altium Designer for longer than one call belong in the shim. Do not read
  files "next to the DLL" (a hot-reloaded assembly has no location): embed them as resources. Do not rely on the
  current directory (it is Altium Designer's installation folder): build paths from the document, the project or
  `Path.GetTempPath()`.
* **Notifications**: create a `DXP.NotificationHandler` with a callback and keep it in a field; its constructor
  registers it, so do not register it again. Filter by `INotification.GetNotificationType()` (the kinds are the
  `DXP.NotificationType` constants, their codes the classes nested in `DXP.NotificationCode`).
* **Comments** say why the code is the way it is, in the same plain-fact style as the README.
* Builds in Debug and Release with **0 warnings**.

## framework/

The extension template's generated shim and implementation code (the same files `dotnet new ad-extension`
creates), adapted once so every sample shares it: names come from the generated `SampleInfo.Name`. Change it only to
follow a new version of the template, and then check every sample. It is expected to become a package published by
the template repository; until then, do not grow it with sample-specific code.

## Before you open a pull request

1. `dotnet build AdSdkSamples.slnx -c Debug` and `-c Release`: 0 warnings, 0 errors.
2. `node eng/build-index.mjs`: validates every `sample.json` and updates the sample table in `README.md` (commit it).
3. Run the sample in Altium Designer: deploy it (`tools/deploy.ps1 -Sample <Id>`, restart once), run every command
   from its menu and through `tools/ad.ps1`, try the failure cases its README describes, and undo what it changed.
   A sample whose main behaviour you could not run is not ready.
4. Write nothing about that run into the repository.

## Continuous integration

`.github/workflows/samples.yml`:

* **check** — every push and pull request, forks included: `node eng/build-index.mjs --check` (schema, ids,
  categories, the README table). Needs neither .NET nor Altium Designer.
* **build** — Windows: fetches the Altium Designer SDK assemblies from a private repository, builds every sample in
  Debug and Release with warnings as errors, and builds the samples index from the compiled code (the workflow
  artifact `samples-index`, kept 14 days, for review). Pull requests from forks get no repository secrets and skip
  it; a maintainer builds those before merging.
* **release** — pushes to the default branch: when the index changed, publishes it as a new release (see "The
  samples index").

Repository settings: secret `AD_SDK_TOKEN` (read access to the private SDK repository), variables
`AD_SDK_REPOSITORY` (owner/name), `AD_SDK_VERSION` (its folder to build against) and `AD_SDK_REF` (its tag to check
out, so a build is reproducible).

The private SDK repository holds compile-time references only, laid out as `<version>/System/*.dll` plus
`<version>/X2.runtimeconfig.json`. Fill it from an Altium Designer installation with
`eng/export-sdk.ps1 -AltiumSystemDir <...\System> -Out <clone> -Verify` (`-Verify` builds every sample against the
copy). For a new Altium Designer version: export it as a new folder, tag it there (`ad-<full build number>`), set
`AD_SDK_VERSION` and `AD_SDK_REF`, fix what breaks, and update "Requirements" in `README.md`.

## The samples index

CI builds, from every sample's `sample.json`, its README and its compiled code:

* `index.json` (`schema/index.schema.json`): each sample with its facets and commands, every SDK type and member its
  code uses with file and line (`apis`; `primary` for the ones named in "What it shows"), and the reverse index from
  each SDK type and member to the samples that use it.
* `llms.txt`: the same list for language models.
* `sample.schema.json`, `index.schema.json`: the schemas the files follow.

It is never committed. On the default branch, every change to it becomes a release `index-<date>.<run>` with these
four files as assets, marked latest; releases are kept, and a push that leaves the index unchanged creates none. The
AD SDK documentation build reads it from there: `releases/latest/download/index.json` for the newest, or a
specific `releases/download/index-<date>.<run>/index.json` to pin it (a reproducible docs build; moving the pin is a
reviewed change there). Do not create other releases marked latest, and never delete an `index-*` release: docs
builds may pin it.

The schemas are the contract with that build: a change that removes or renames a field, or changes its meaning, increments `schemaVersion` in both schemas
and needs a matching change in the documentation build; adding an optional field does not.

