# Import a DWG/DXF file into a new PCB document

Creates a new PCB document, imports an AutoCAD `.dwg` or `.dxf` file into it with the built-in `PCB:Import`
process, and saves it as a `.PcbDoc`. It shows how an extension drives a built-in importer through
`DXP.Utils.RunCommand` on a document it created itself, and how it names and saves that new document.

**Needs a human at Altium Designer.** The importer shows its own modal dialog, and the command waits until someone
confirms it.

## What it shows
- `EDP.IWorkspace.DM_CreateNewDocument`: creates a new document of a kind (`"PCB"`); the workspace comes from
  `DXP.IClient.GetDXPWorkspace`.
- `DXP.IClient.CurrentView` and `DXP.IServerDocumentView.GetOwnerDocument`: find the document that got the focus,
  and check that it is a new PCB document before changing anything.
- `PCB.IPCB_ServerInterface.GetCurrentPCBBoard`: the board of the new document.
- `DXP.Utils.SetParamterValue` and `DXP.Utils.RunCommand`: build a `Name=Value|...` parameter string and run the
  `PCB:Import` process (then `PCB:Zoom`) on the current document.
- `PCB.IPCB_BoardIterator` (`PCB.IPCB_Board.BoardIterator_Create` / `BoardIterator_Destroy`): count primitives by
  `PCB.IPCB_Primitive.ObjectID` before and after the import, so the result can be checked from the text.
- `DXP.IServerDocument.FileName` (`SetFileName`) and `DXP.IServerDocument.DoFileSave`: give the new document its
  path and save it.

## Run it
1. Have a `.dwg` or `.dxf` file. No document needs to be open.
2. `tools/ad.ps1 -Sample ImportDwgToPcb run ImportDwg "File=<path of the .dwg/.dxf>|Out=<folder>"`.
   The menu item **ImportDwgToPcb › Import Dwg** passes no `File` and only reports the missing parameter.
3. **Stay at Altium Designer: the import shows the "Import from AutoCAD" dialog** (layer map, scale, insertion
   point; Cancel / OK). The command waits until you press OK. It cannot run unattended.
4. The import is slow: a small DXF (a few hundred bytes) can take over a minute, including the wait for the dialog.
5. A new PCB document opens with the drawing's objects, and is saved as `<folder>\<drawing name>.PcbDoc`. The
   returned text lists the primitives added per kind, the save result and the layers used.

| Command | Parameters | Result |
|---|---|---|
| `ImportDwg` | `File=<.dwg or .dxf>` (required); `Out=<folder or .PcbDoc path>` (default: the temp folder, file named after `File`); `Overwrite=true` (optional); `Silent=false` (optional, default true: pass `Silent=True|TestMode=True`; neither value suppresses the dialog) | `Imported '<file>' into a new PCB document (Silent=True): <n> primitives added (<kind> <before> -> <after>, ...). Saved as '<target>': DoFileSave returned <bool>, file exists on disk. ...` |

## How it works
`ImportDwgToPcbCommands.cs` first validates the parameters: `File` must exist and be a `.dwg` or `.dxf`; the
target must not exist unless `Overwrite=true`. It then gets the workspace from `DXP.GlobalVars.Client`, notes the
focused document, and calls `DM_CreateNewDocument("PCB")`. The focused document must now be of kind `PCB` and
not the one focused before, and `GetCurrentPCBBoard` must return its board.

It counts the board's primitives, builds `Format=AutoCAD Files (*.DXF; *.DWG)|Filename=<file>` with
`SetParamterValue`, adds `Silent=True|TestMode=True` unless `Silent=false`, and runs `PCB:Import`, then
`PCB:Zoom Action=Redraw`. It re-queries the current board, checks that it is still the new document, and counts
again. If nothing was added, it stops and leaves the new document open and unsaved. Otherwise it calls
`SetFileName(target)` and `DoFileSave("")`, and checks that the file exists.

## Notes
- **The importer's parameters are not documented.** The sample passes `Format`, `Filename`, `Silent` and
  `TestMode`. The `Format` text is the `FileExtensions` value of the `DXF/DWG` import filter that AD declares for
  the PCB editor in `System\advpcb.ins`.
- **The import dialog is not suppressed.** `PCB:Import` with `Silent=True|TestMode=True` shows the "Import from
  AutoCAD" dialog, and the command waits until the user presses OK. `RunCommand` returns only after the import has
  finished. A modal dialog blocks AD, so run this sample only while someone is at AD.
- The sample does not write the registry key `HKEY_LOCAL_MACHINE\Software\Altium\TestMode`: that is a system
  setting and needs elevation.
- How long the import itself takes is not known: the time above includes the wait for the dialog. The DXF
  geometry lands on Mechanical 1, and one DXF unit becomes 1 mil.
- `DM_CreateNewDocument("PCB")` returns the default file name (`PCB1.PcbDoc`), and the new document is focused
  when the call returns. With a free document focused, the new board is added to Free Documents.
- The kind strings `DoFileSave` accepts are not documented. The command passes an empty kind, which saves the
  renamed new PcbDoc and returns True.
- Only these primitive kinds are counted: tracks, arcs, texts, fills, regions, polygons, pads, vias, components,
  dimensions and component bodies. If an import creates only other kinds, the command reports "added no
  primitives" and does not save.
- The menu item cannot pass a file, so it only shows the "No input file specified" message. Use `tools/ad.ps1`
  or a process call with `File=`.
