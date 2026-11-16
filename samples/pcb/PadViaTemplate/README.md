# Report the template each pad is linked to

Lists the pads of the current PCB board that are linked to a pad/via template, with the template's library ID,
template ID, revision ID, name and description. It shows how to get the board template behind a pad with
`PCB.IPCB_Board.FindLinkedBoardTemplate` and read it as a `PCB.IPCB_PadViaTemplate`.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.ePadObject`, `PCB.PCBConstant.AllLayersSet` and
  `PCB.TIterationMethod.eProcessAll`: iterate every pad of the board.
- `PCB.IPCB_Board.FindLinkedBoardTemplate`: the template a pad is linked to; null is treated as "not linked".
- `PCB.IPCB_PadViaTemplate.LibraryID`, `PCB.IPCB_PadViaTemplate.TemplateID`, `PCB.IPCB_PadViaTemplate.RevisionID`,
  `PCB.IPCB_PadViaTemplate.TemplateName`, `PCB.IPCB_PadViaTemplate.TemplateDescription`: the template's identity.
- `PCB.IPCB_PadViaTemplate.GetFullTemplateID`: `LibraryID@TemplateID`, used to group pads by template.
- `PCB.IPCB_Pad.TemplateLink` (`PCB.IPCB_PadViaTemplateLink`): the pad's own link identifiers, printed next to the
  board template so both can be compared.
- `PCB.IPCB_Board.PadViaLibrary` (`PCB.IPCB_PadViaLibrary.Count`): the number of templates on the board.
- Setup helper: `PCB.IPCB_PadViaLibrary.AddTemplate`, `PCB.IPCB_PadViaLibrary.Template` and
  `PCB.IPCB_Board.LinkToTemplate` create a template from a pad and link the pad to it.

## Run it
1. Open a `.PcbDoc` and make it the active document. Pads placed from a pad/via template library are reported as
   linked (on the board of the SpiritLevel-SL1 example project that ships with Altium Designer, all pads are). On a
   board without such pads, run **PadViaTemplate › Link Test Pad Template** first (it changes the board in memory;
   close without saving).
2. **PadViaTemplate › Get Template Name**, or `tools/ad.ps1 -Sample PadViaTemplate run GetTemplateName "Max=50"`
3. The first line is `<board>.PcbDoc: <n> pads, <k> linked to a template (<d> distinct), <u> not linked; board
   pad/via library: <c> templates.` With linked pads, one line per pad follows
   (`U1-1 : Library ID : ..., Template ID : ..., Revision ID : ..., Template Name : ..., Template Description : ...,
   Pad TemplateLink : ...`), then one line per distinct template with its pad count. Without, `No pad is linked to a
   pad/via template.`
4. To link a specific pad to a new board template:
   `tools/ad.ps1 -Sample PadViaTemplate run LinkTestPadTemplate "Pad=U5-11"`.

| Command | Parameters | Result |
|---|---|---|
| `GetTemplateName` | `Max=<n>` (optional, default 50: number of linked pads listed; counts always cover all pads) | The report as text |
| `LinkTestPadTemplate` | `Pad=<U1-1>` (optional; default: the first pad that is not linked) | `Linked pad <pad> to new template '<name>' (<id>); library count <a> -> <b>; FindLinkedBoardTemplate returns ...` |

## How it works
`PadViaTemplateCommands.cs`: `GetTemplateName` gets the current board and iterates its pads (all layers,
`eProcessAll`; the iterator is destroyed in a `finally`). For each pad it reads the pad's `TemplateLink` and calls
`FindLinkedBoardTemplate`. A non-null template is counted, grouped by `GetFullTemplateID`, and, up to `Max` pads,
listed with its five identity properties. Pads whose `FindLinkedBoardTemplate` is null but whose `TemplateLink` holds
identifiers are counted separately. The summary adds the template count of the board's `PadViaLibrary`.

`LinkTestPadTemplate` picks a pad (by pin descriptor, or the first unlinked one), calls `AddTemplate(pad)` on the
board's `PadViaLibrary`, takes the last template when the count went up, and calls `LinkToTemplate` between
`PreProcess`/`PostProcess` and the pad's `BeginModify`/`EndModify`. It reads the link back with
`FindLinkedBoardTemplate`.

## Notes
- `PCB.IPCB_Pad.TemplateLink` is **not** a way to find a pad's template. On the SpiritLevel-SL1 board every one of
  the 642 pads has a template through `FindLinkedBoardTemplate` while its `TemplateLink` has empty `LibraryID` and
  `TemplateID` - also right after `LinkToTemplate`. Use `FindLinkedBoardTemplate`.
- `FindLinkedBoardTemplate` also returns templates that are not in `IPCB_Board.PadViaLibrary`. On that board the
  library holds 0 templates while the pads use 38 distinct templates from another library ID, so don't look the
  returned template up in `PadViaLibrary`. Templates are identified by `GetFullTemplateID`
  (`LibraryID@TemplateID`); `TemplateName` is not unique across libraries.
- `AddTemplate(pad)` appends a template built from the pad (`Count` 0 -> 1, new template
  at `Template[Count - 1]`, pad's template name kept but new `LibraryID`/`TemplateID`), and `LinkToTemplate` links
  the pad to it so that `FindLinkedBoardTemplate` returns it. The document is marked modified. Whether the change
  can be undone is not known. Close the document without saving to discard it.
- `LinkTestPadTemplate` without `Pad=` fails with `The board has no pad that is not linked to a template.` when
  every pad is already linked; pass `Pad=` to re-link a linked pad to a new board template.
- Not known: whether `FindLinkedBoardTemplate` returns null (rather than throwing) for an unlinked pad. This
  sample treats null as "not linked".
- Not known: whether `LinkToTemplate` applies the template's settings to the pad.
- Pads are named by `PCB.IPCB_Pad.PinDescriptorString` (`U1-1`); pads without a component as `free pad <name>`.
