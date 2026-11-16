# Pick tracks and show their coordinates

Prompts the user to click tracks on the current PCB and reports each track's start and end point in mils, relative
to the board origin. It shows the interactive pick pattern: `ChooseLocation` for a click, then
`GetObjectAtXYAskUserIfAmbiguous` to turn the point into a track.

## What it shows
- `PCB.IPCB_Board.ChooseLocation`: waits for a click in the PCB editor and returns the point in internal units. It
  returns false on Escape, and without prompting when the board has no view, so check the result.
- `PCB.IPCB_Board.GetObjectAtXYAskUserIfAmbiguous`: the primitive at a point, limited to a `PCB.TObjectSet`
  (`PCB.TObjectId.eTrackObject`) and a layer set (`PCB.PCBConstant.V6AllLayersSet`), with
  `PCB.TEditingAction.eEditAction_Select`. AD asks the user to choose when objects overlap. Null means nothing found.
- `PCB.IPCB_Track.X1`, `PCB.IPCB_Track.Y1`, `PCB.IPCB_Track.X2`, `PCB.IPCB_Track.Y2`: the end points in absolute
  internal units.
- `PCB.IPCB_Board.XOrigin` / `PCB.IPCB_Board.YOrigin`: subtract the board origin so the values match the rulers.
- `EDP.Utils.CoordToMils`: internal units (10,000 per mil) to mils.

## Run it
1. Open a `.PcbDoc` with tracks (the board of the SpiritLevel-SL1 example project that ships with Altium Designer
   works) and make it the active document.
2. **PickTrackCoordinates › Show Track Coord**, then click tracks. Press Escape or click away from a track to stop.
   From the command line: `tools/ad.ps1 -Sample PickTrackCoordinates run ShowTrackCoord "At=<x,y>"` (no click
   needed), or `"Pick=true"` while someone clicks in AD.
3. A message lists one line per picked track, for example
   `x1 = 1250 y1 = 830.5 x2 = 1400 y2 = 830.5 (layer eV6_TopLayer, width 10 mil)`, under a line such as
   `2 track(s) picked, clicked away from a track:`.

| Command | Parameters | Result |
|---|---|---|
| `ShowTrackCoord` | `Max=<n>` (optional; menu default: no limit, command-line default 1), `Pick=true` (command line only: prompt for clicks), `At=<x,y>` (optional: mils from the board origin, no prompt) | One line per picked track, or `No track picked: <reason>.` / `No track at <x,y>.` |
| `ListTracks` | `Max=<n>` (default 5) | `<board>: <n> tracks, first <m> listed.` and per track the same line plus `mid At=<x>,<y>` (helper) |

## How it works
`PickTrackCoordinatesCommands.cs`: `ShowTrackCoord` gets the current board and its origin. In a loop it calls
`ChooseLocation` and stops when it returns false. It passes the point to `GetObjectAtXYAskUserIfAmbiguous` with the
track object set and all layers, and stops when the result is not an `PCB.IPCB_Track`. For each track it subtracts the
origin from `X1`/`Y1`/`X2`/`Y2`, converts with `CoordToMils`, logs the line with `ctx.Log` and collects it. The
collected lines are returned together when the loop ends. With `At=x,y` the prompt is skipped and only the hit test runs.

`ListTracks` walks the tracks with a board iterator (`eTrackObject`, all layers, destroyed in a `finally`) and prints
the same line plus each track's midpoint, so the hit test can be checked with `At=` without clicking.

## Notes
- The command only reads; nothing is changed, so there is nothing to undo.
- `ChooseLocation` blocks Altium Designer until someone clicks. The command therefore refuses to prompt when it is
  started from the command line, unless `Pick=true` is given.
- `At=` with a midpoint from `ListTracks` returns exactly the same track line, so the
  round trip mils → internal units → `GetObjectAtXYAskUserIfAmbiguous` → mils is exact. `V6AllLayersSet` includes
  the mechanical layers (on the SpiritLevel-SL1 board the first tracks are the outline on Mechanical 1). A point
  with no object returns null, without a dialog.
- If objects overlap at the point, `GetObjectAtXYAskUserIfAmbiguous` shows a menu to pick one. With `At=` that
  menu also needs a human. A track midpoint from `ListTracks` can still overlap another track, for example where
  tracks cross on different layers.
- Coordinates are printed with up to three decimals in the invariant culture.
