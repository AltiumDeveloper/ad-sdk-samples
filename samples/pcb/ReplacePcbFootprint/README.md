# Rename a footprint pattern and refresh its height

Renames the footprint pattern of every component that uses a given pattern and copies the height and description of
the new footprint from a PCB library into the component. It shows how to read a library footprint with
`PCB.IPCB_ServerInterface.LoadCompFromLibrary` without placing it, and how to change component properties inside
`PreProcess`/`PostProcess` - deliberately without `BeginModify`/`EndModify`, see How it works.

## What it shows
- `PCB.IPCB_Board.BoardIterator_Create` with `PCB.TObjectId.eComponentObject`: collect the board's components (the
  iterator is destroyed before any component is changed).
- `PCB.IPCB_Component.Pattern`: the footprint name. Assigning it is expected to rename the footprint reference only,
  leaving the pads and other primitives in place (see Notes).
- `PCB.IPCB_Component.SourceFootprintLibrary`: the library the component's footprint comes from, used as the library
  to look the new footprint up in.
- `PCB.IPCB_ServerInterface.LoadCompFromLibrary`: loads a footprint by name from a PCB library file as a
  `PCB.IPCB_LibComponent` that is not on any board, or returns null.
- `PCB.IPCB_LibComponent.Height` / `PCB.IPCB_LibComponent.Description` copied into `PCB.IPCB_Component.Height` and
  `PCB.IPCB_Component.FootprintDescription`. Height is in internal units (10,000 per mil).
- `PCB.IPCB_Group.SetState_XSizeYSize`: recalculates the component's bounds after the change.
- `PCB.IPCB_ServerInterface.PreProcess` / `PCB.IPCB_ServerInterface.PostProcess`: bracket the changes as one board
  operation.
- `PCB.IPCB_Primitive.GraphicallyInvalidate`: redraw each changed component.

## Run it
1. Open a `.PcbDoc` and make it the active document. Use a scratch copy: the command changes the board.
2. **ReplacePcbFootprint › List Footprints**, or `tools/ad.ps1 -Sample ReplacePcbFootprint run ListFootprints`, to see
   the patterns on the board. Add `Lookup=<pattern>` to check whether a footprint is in the components' libraries.
3. **ReplacePcbFootprint › Replace Pcb Footprint** (defaults `From=R0805|To=R0805-W`), or
   `tools/ad.ps1 -Sample ReplacePcbFootprint run ReplacePcbFootprint "From=<old>|To=<new>"`.
4. The result starts with `<board>: <n> components changed from '<old>' to '<new>', <r> refreshed from the library,
   <m> without a library match.`, then one line per component with the old and new height and the description.

| Command | Parameters | Result |
|---|---|---|
| `ReplacePcbFootprint` | `From=<pattern>` (default `R0805`), `To=<pattern>` (default `R0805-W`), `Library=<.PcbLib path>` (optional; default: each component's `SourceFootprintLibrary`) | Counts and one line per changed component, or `no component uses the pattern '<From>'` |
| `ListFootprints` | `Pattern=<name>` (optional filter), `Lookup=<footprint>` (optional), `Library=<.PcbLib path>` (optional, for `Lookup`) | Pattern summary and one line per component (Pattern, Height, Description, Library); with `Lookup`, the footprint's Height and Description per library, or `not found` |

## How it works
`ReplacePcbFootprintCommands.cs`: `ReplacePcbFootprint` gets the current board, collects its components with a board
iterator (all layers, `eProcessAll`) and keeps those whose `Pattern` equals `From` (case-sensitive). Between
`PreProcess` and `PostProcess`, each one gets `SetState_Pattern(To)`, then
`LoadCompFromLibrary(To, library)` with the component's `SourceFootprintLibrary` (or `Library`). When the footprint is
found, its `Height` and `Description` are copied into the component. `SetState_XSizeYSize` recalculates the bounds
and `GraphicallyInvalidate` redraws the component.

The components are deliberately **not** wrapped in `BeginModify`/`EndModify`, unlike most PCB changes:
`EndModify` re-resolves the component against the available libraries (the mechanism is not
established), throws the new `Pattern` away (it is back to the old value right after `EndModify`) and, when the new
name is in no available library, opens a modal "Component not found in available libraries." box, one per
component, that blocks a non-interactive caller. A `.PcbLib` file on disk that is not an installed library does not
count as available. Without the pair, the new `Pattern` sticks. Components without a library match are counted and
keep their old height and description, but are renamed.

`ListFootprints` is a read-only helper for checking the result: it lists the components'
footprint data and, with `Lookup`, what `LoadCompFromLibrary` returns for a footprint name.

## Notes
- This does not replace the footprint geometry: only the name, height and description are assigned. The pads and
  other primitives are expected to stay those of the old footprint.
  `PCB.IPCB_Component.LoadCompFromLibrary()` (the component's own method) is documented as reloading the primitives
  from the footprint named by `Pattern`, which would be the way to a real replacement. That is out of scope here.
- `LoadCompFromLibrary` needs the **full path of a `.PcbLib`**. The bare library name that
  `SourceFootprintLibrary` returns (`Spirit_Level_Project.PcbLib`, `Miscellaneous Devices.IntLib`) is not resolved:
  the lookup returns `null` even for a footprint that the same file does contain. So pass `Library=<path to .PcbLib>`;
  without it the lookup fails on boards like the SpiritLevel example and the components are only renamed.
- An unknown footprint name makes `LoadCompFromLibrary` return `null` (no exception, no dialog), and
  the call does not open the library as a document. Whether the returned footprint has to be freed is not known; this
  sample doesn't free it.
- The rename leaves `SourceFootprintLibrary` unchanged.
- The change is in memory. Close the document without saving to discard it. Whether one Undo reverts all components
  is not known.
- Heights are printed in mils with the raw internal value in brackets.
