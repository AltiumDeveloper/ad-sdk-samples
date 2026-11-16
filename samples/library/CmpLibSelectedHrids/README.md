# List HRIDs of components selected in a CmpLib

Lists the HRID (human-readable item ID) of each component selected in the active component library (`.CmpLib`)
editor. It shows how to check a document's kind before treating it as a `ComponentLib.IComponentLibraryDocument`,
and how to read that editor's selection.

## What it shows
- `DXP.IClient.GetCurrentView` / `DXP.IServerDocumentView.GetOwnerDocument`: the document behind the active view.
  When AD passes the view to the command (menu, Run Process), that view is used.
- `DXP.IServerDocument.GetKind`: the document kind, checked before the cast. SDK interfaces are dispatch
  interfaces, so the cast to `ComponentLib.IComponentLibraryDocument` succeeds for any document. Only the kind check
  tells you whether it is a CmpLib.
- `ComponentLib.IComponentLibraryDocument.GetSelectedComponents`: the components selected in the CmpLib editor, as
  a `ComponentLib.IComponentDefinitionList`. It is a typed extension method, so it needs `using ComponentLib;`.
- `ComponentLib.IComponentDefinitionList.Get` with `Count`: index-based access to the list.
- `ComponentLib.IComponentDefinition.ItemHRID`: the component's HRID, read with `GetItemHRID`.

## Run it
1. Open a component library (`.CmpLib`) and make its editor the active document. Select one or more components in it.
2. `tools/ad.ps1 -Sample CmpLibSelectedHrids run GetSelectedComponents`, or **DXP › Run Process** with process
   `CmpLibSelectedHrids:Run` and parameters `Cmd=GetSelectedComponents`. The **CmpLibSelectedHrids › Get Selected
   Components** menu is only in the no-document menu (see Notes).
3. The result is `<file>.CmpLib: <n> selected components`, then one numbered line per component with its HRID.

| Command | Parameters | Result |
|---|---|---|
| `GetSelectedComponents` | none | `<file>: <n> selected components` and `  <i>. <HRID>` per component; `<file>: no components selected...` for an empty selection; an error naming the document kind when the active document is not a CmpLib |

## How it works
`CmpLibSelectedHridsCommands.cs` takes the view AD passed to the command (`ctx.View`), or the client's current view
when there is none (a call from `tools/ad.ps1` may not carry a view). It gets the owner document and compares
`GetKind()` with `CmpLib`, ignoring case. If they don't match, a `UserException` reports the kind and the file name.
It then casts the document to `ComponentLib.IComponentLibraryDocument`, calls `GetSelectedComponents`, and loops
from 0 to `Count() - 1`, calling `Get(i).GetItemHRID()` on each item. All lines are returned as one text result.
Nothing in the document is changed.

## Notes
- Read-only: the command changes no document.
- `IServerDocument.GetKind()` returns `PCB` for a `.PcbDoc`, so running the command with
  a board active reports "This is not a CmpLib document (kind 'PCB', ...)".
- The selection is made by a person in the CmpLib editor. Without one, the command can only report an empty list.
- The generated menu is inserted only into AD's no-document menu. The sample template has no menu target for the
  CmpLib editor. While a CmpLib is focused, run the command with `tools/ad.ps1` or with **Run Process**.
- The command compares the document kind with `CmpLib`, ignoring case: the exact casing is not documented. For an
  empty `GetItemHRID` value it shows `(no HRID)`.
