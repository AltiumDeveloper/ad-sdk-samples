# List a sheet's sheet symbols and their child sheets

Compiles the project of the focused schematic sheet, then lists every sheet symbol on that sheet with the file name it
links to and the child sheets it points to. It shows how to walk the design hierarchy through the design model (EDP)
instead of iterating schematic primitives.

## What it shows
- `EDP.IWorkspace.DM_FocusedDocument`: the active document as a design-model `EDP.IDocument` (null when nothing is
  focused, so check it).
- `EDP.IDocument.DM_DocumentKind`: `'SCH'` for a schematic sheet.
- `EDP.IDocument.DM_Project` and `EDP.IProject.DM_Compile`: compile the sheet's project so the design model is up to
  date; `DM_Compile` returns a bool.
- `EDP.IWorkspace.DM_FreeDocumentsProject`: tells a free document (not in a project) apart from a project sheet.
- `EDP.IDocument.DM_SheetSymbolCount` / `EDP.IDocument.DM_SheetSymbols`: index-based access to the sheet symbols
  placed on the sheet.
- `EDP.ISheetSymbol.DM_SheetSymbolFileName`: the file name that links the symbol to its sheet.
- `EDP.ISheetSymbol.DM_ChildSheetCount` / `EDP.ISheetSymbol.DM_ChildSheet`: the child sheets as `EDP.IDocument`,
  named with `EDP.IDocument.DM_FileName`.
- `EDP.ISheetSymbol.DM_LogicalDesignator`: the designator drawn on the symbol.

## Run it
1. Open a project with a sheet hierarchy and focus its top-level sheet. In the SpiritLevel-SL1 example project that
   ships with Altium Designer, open `SL1 Xilinx Spartan-IIE PQ208 Rev1.02.PrjPcb` and focus
   `SL1 Xilinx Spartan-IIE PQ208 Rev1.02.SchDoc`.
2. **SheetSymbolChildSheets › Show Child Sheet**, or `tools/ad.ps1 -Sample SheetSymbolChildSheets run ShowChildSheet`
3. The result starts with `<sheet> (project <project>, compiled): <n> sheet symbols, <m> child sheets.`, followed by
   `Sheet symbol <designator>: <file name>` and an indented `Child sheet: <file name>` line per child. On SpiritLevel
   the top sheet has four sheet symbols (`U_SL_Config`, `U_SL_FPGA_Auto_2E`, `U_SL_LCD_SW_LED`, `U_SL_Power`) with
   one child sheet each. A leaf sheet such as `SL_Power.SchDoc` returns `... this schematic document has no sheet
   symbol.`

| Command | Parameters | Result |
|---|---|---|
| `ShowChildSheet` | `Compile=true\|false` (optional, default `true`: compile the sheet's project first) | The report as text; an error if no document is focused or the focused document is not a schematic |

## How it works
`SheetSymbolChildSheetsCommands.cs`: `ShowChildSheet` gets the workspace as `EDP.IWorkspace` (from
`DXP.IClient.GetDXPWorkspace`) and its `DM_FocusedDocument`. It checks that `DM_DocumentKind` is `SCH`. It then takes the
sheet's `DM_Project`, compares it with `DM_FreeDocumentsProject`, and, for a project sheet, calls `DM_Compile` (unless
`Compile=false`), reporting a `false` result. It loops `DM_SheetSymbolCount` times over `DM_SheetSymbols(i)`. For each
symbol it prints `DM_LogicalDesignator` and `DM_SheetSymbolFileName`, then loops `DM_ChildSheetCount` times over
`DM_ChildSheet(j)` and prints each child's `DM_FileName`. A symbol without a child sheet is reported and the loop goes on.
The parent sheet stays in its own variable throughout.

## Notes
- The command is read-only: compiling updates the in-memory design model only, nothing is saved.
- The sheet symbols and child sheets come from the compiled design model, which is why the project is compiled first.
  With `Compile=false` the list reflects the last compile.
- Compiling through `MessageRouter_SendCommandToModule('WorkspaceManager:Compile', ...)` is not exposed under that
  name in the .NET API. `EDP.IProject.DM_Compile` on the sheet's project is used instead.
- The parent sheet is kept in its own variable. Reusing one variable for the parent sheet and the child sheets would,
  after the first child, read the child's sheet symbols.
- A child sheet's `DM_FileName` is a bare file name (`SL_Config_2E.SchDoc`), not a path; `DM_Compile` returns
  `true` on the SpiritLevel project; `EDP.IDocument.DM_Project` of a *free* document returns the Free Documents
  project (its `DM_ProjectFullPath` is the string `Free Documents`), not null, and every sheet symbol of a free
  sheet reports `DM_ChildSheetCount` 0. Child sheets exist only in a compiled project's design model, while
  `DM_SheetSymbolFileName` is read from the sheet and is still shown.
