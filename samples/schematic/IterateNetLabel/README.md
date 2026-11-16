# Iterate the net labels of a schematic sheet

Lists the text of every net label on the current schematic sheet. It shows the basic schematic iterator pattern:
create an iterator on the document, filter it to one object kind, step through the objects, destroy the iterator.

## What it shows
- `SCH.ISch_ServerInterface.GetCurrentSchDocument`: the active schematic document, or none.
- `SCH.ISch_Document.SchIterator_Create` / `SCH.ISch_Document.SchIterator_Destroy`: the document owns the
  iterator; destroy it in a `finally` block so it is released also when the loop throws.
- `SCH.ISch_Iterator.AddFilter_ObjectSet` with `SCH.TObjectSet` and `SCH.TObjectId.eNetLabel`: the filter takes a
  set of object kinds. Change the object ID to visit another kind of object.
- `SCH.ISch_Iterator.FirstSchObject` / `SCH.ISch_Iterator.NextSchObject`: they return `SCH.ISch_BasicContainer`;
  the filter makes the cast to `SCH.ISch_NetLabel` safe.
- `SCH.ISch_NetLabel.DisplayString`: the label's text as shown on the sheet, after special-string evaluation.

## Run it
1. Open a schematic sheet with net labels (for example `SL_Config_2E.SchDoc` of the SpiritLevel-SL1 example
   project that ships with Altium Designer) and make it the active document.
2. **IterateNetLabel › Iterate Net Label**, or `tools/ad.ps1 -Sample IterateNetLabel run IterateNetLabel`
3. The result lists the sheet name, the number of net labels, and one label text per line.

| Command | Parameters | Result |
|---|---|---|
| `IterateNetLabel` | `Max=<n>` (optional, default 200): the most labels listed | `<sheet>: <count> net label(s):` then one `  <text>` line per label (`, first <n> shown:` when there are more than `Max`), or `<sheet>: no net labels.` |

## How it works
`IterateNetLabelCommands.cs` reads `Max`, then gets the schematic server from `SCH.GlobalVars.SchServer` and the
active document from `GetCurrentSchDocument`. A `UserException` covers the cases where no schematic editor or no
schematic document is active. The command creates an iterator with `SchIterator_Create`, adds a `TObjectSet`
filter holding `eNetLabel`, and walks the objects with `FirstSchObject` / `NextSchObject`, collecting
`GetState_DisplayString()` of each. `SchIterator_Destroy(ref iterator)` runs in a `finally` block. The count is
over all labels; only the listing is capped at `Max`.

## Notes
- Read-only: nothing on the sheet changes.
- The iterator visits the objects of the active document only, not the other sheets of the project.
- `GetCurrentSchDocument` returns null while a PCB document is focused, even when schematic sheets are open; the
  command then reports that no schematic document is active.
- A type test such as `obj as ISch_NetLabel` succeeds for every SDK object, so it cannot replace the filter. To
  visit several kinds with one iterator, put them all in the `TObjectSet` and branch on `GetState_ObjectId()`.
- The order of the labels is the iterator's order; the SDK reference does not document it. It
  matches the order of the records in the `.SchDoc` file.
- For plain-text net labels `DisplayString` equals `Text`.
