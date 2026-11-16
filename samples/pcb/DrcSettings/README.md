# Read a board's DRC settings and run a batch DRC

Reports whether online design rule checking is on and how many of the current board's design rules are enabled for
checking, switches online DRC on or off, and runs a batch DRC that writes a report file. It shows where the DRC
settings live in the .NET API: a PCB Editor preference, a flag on each rule, and a board method for the batch run.

## What it shows
- `PCB.IPCB_ServerInterface.SystemOptions` and `PCB.IPCB_SystemOptions.DoOnlineDRC`: the online DRC switch is a PCB
  Editor preference shared by all documents, not a property of the board.
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eRuleObject`: design rules are board objects, found with
  a board iterator.
- `PCB.IPCB_Rule.DRCEnabled` and `PCB.IPCB_Rule.RuleKind`: whether each rule takes part in design rule checking,
  counted per rule kind.
- `PCB.IPCB_Board.RunBatchDesignRuleCheck` with `PCB.TDRCReportFileFormat`: run the batch DRC and write an HTML or
  text report without opening it.
- `PCB.TObjectId.eViolationObject`: the violations on the board, counted before and after the batch run.

## Run it
1. Open a `.PcbDoc` and make it the active document (any board with design rules, for example the board of the
   SpiritLevel-SL1 example project that ships with Altium Designer). Use a copy: the batch DRC may add violation
   objects to the board.
2. **DrcSettings › Report DRC Settings**, or `tools/ad.ps1 -Sample DrcSettings run ReportDrcSettings`
3. The result starts with `<board>: online DRC on|off, <n> rules (<e> enabled, <d> disabled), <v> violations on the
   board.`, followed by one line per rule kind in the form `    eRule_<Kind>: <x> enabled, <y> disabled` (the
   counts depend on the board's rule set).

| Command | Parameters | Result |
|---|---|---|
| `ReportDrcSettings` | none | The summary line and one line per rule kind |
| `EnableOnlineAndBatchCheck` | `Online=true` or `Online=false` (required; the menu passes `Online=true`) | `Online DRC changed from <old> to <new>. …`, with the parameter that restores the old value |
| `RunBatchDrc` | `Out=<folder or file>` (default: the temp folder), `Format=Html` (default) or `Format=Text` | `RunBatchDesignRuleCheck returned <bool>. Violations on the board: <n> before, <m> after. Report written to <path>` (or `No report file at <path>`) |

With a folder, the report is named `DrcSettings_<board>.html` (or `.txt`). A missing `Online` or an unknown `Format`
is rejected with an error and changes nothing.

## How it works
`DrcSettingsCommands.cs`: every command gets `PCB.GlobalVars.PCBServer` and the current board, and raises a
`UserException` when either is missing. `ReportDrcSettings` reads `DoOnlineDRC` from the server's system options,
then iterates the board's rule objects on all layers (`eProcessAll`), reading `DRCEnabled` and `RuleKind` of each
rule and counting them per kind. A second iteration counts the violation objects. The `ForEach` helper destroys each
iterator in a `finally` block.

`EnableOnlineAndBatchCheck` parses `Online`, reads `DoOnlineDRC`, sets it with `SetState_DoOnlineDRC` and reads it
back. `RunBatchDrc` builds the report path from `Out` and the board name, counts the violations, calls
`RunBatchDesignRuleCheck(path, format, false, false)` (no report display, no web publishing), counts the violations
again and checks that the file was written during the run.

## Notes
- The .NET `PCB.IPCB_Board` has no accessor for the board's `PCB.IPCB_DesignRuleCheckerOptions`, and no other API
  returns it, so its settings (report file name, maximum violation count, rule sets to check) cannot be read here.
- `DoOnlineDRC` affects every PCB document in this Altium Designer session. A value set with
  `SetState_DoOnlineDRC` is seen by later, independent reads of `SystemOptions`. The command does not call
  `Export_ToIniFile`. Restore the old value with the parameter the command returns.
- `DRCEnabled` reads false when the rule's rule set is disabled, even if the rule itself is enabled (reference).
  The report counts the value as read. On the SpiritLevel-SL1 board the rules that read false are the ones the DRC
  report marks `(Disabled)`.
- `RunBatchDesignRuleCheck` returns true for a check that completed (on a board without violations), writes the
  report to exactly the file name passed (no extension added or changed), opens no dialog with
  `argDisplayReportFile = false`, writes nothing to the Messages panel, and does not mark the document modified. The
  HTML format gives the same "Design Rule Verification Report" as AD's own DRC; the text format is pipe-separated
  (`Violations|Total|0`, one `Rule Violations|<rule>|<count>` line per rule).
- The command `EnableOnlineAndBatchCheck` sets only the online switch: the batch check has no switch in the
  reachable API.
