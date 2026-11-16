# Read and write a board's unique ID

Reads the unique ID of the current PCB board and assigns a new one, passed as a command parameter. It shows how
to read a property inherited from `PCB.IPCB_Primitive`, and how to bracket a change to the board itself.

## What it shows
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the active PCB document, or none.
- `PCB.IPCB_Primitive.UniqueId`: the board's unique ID, read with `GetState_UniqueId` and written with
  `SetState_UniqueID`. The two accessors differ in the case of the last letter.
- `PCB.IPCB_ServerInterface.PreProcess` / `PostProcess` with `PCB.IPCB_Primitive.BeginModify` / `EndModify`:
  the bracket for changing PCB objects, applied to the board.
- Command parameters (`UniqueId=<text>`), read with `ctx.Get`.

## Run it
1. Open any `.PcbDoc` (for example the board of the SpiritLevel-SL1 example project that ships with Altium
   Designer) and make it the active document.
2. **BoardUniqueId › Get Unique Id**, or `tools/ad.ps1 -Sample BoardUniqueId run GetUniqueId`
3. `tools/ad.ps1 -Sample BoardUniqueId run SetUniqueId "UniqueId=MY-BOARD-01"`, then **Get Unique Id** again.

| Command | Parameters | Result |
|---|---|---|
| `GetUniqueId` | none | `<file>: UniqueId = '<id>'` |
| `SetUniqueId` | `UniqueId=<text>` (required) | `UniqueId changed from '<old>' to '<new>'.` |

## How it works
`BoardUniqueIdCommands.cs` gets the current board from `PCB.GlobalVars.PCBServer`. A clear `UserException`
covers the cases where no PCB editor or no PCB document is active. `GetUniqueId` returns `GetState_UniqueId()`.
`SetUniqueId` reads the `UniqueId` parameter, then changes the ID between `PreProcess`/`BeginModify` and
`EndModify`/`PostProcess`. It returns the old and new values, read back from the board.

## Notes
- The change is made in memory only and marks the document modified. Save the document to keep it.
- The ID is a free string: the SDK assemblies place no format or length limit on `SetState_UniqueID`. The
  IDs Altium Designer generates are 8 upper-case letters (for example `BTVKGJOI`); that format is not documented.
- `PCB.IPCB_Board.GenerateUniqueID` returns a new ID each time it is called and does not assign it. To give a
  board a fresh ID, pass its result to `SetState_UniqueID`. This sample does not call it.
