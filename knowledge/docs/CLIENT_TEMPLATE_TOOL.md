# Client Template Tool — Tekla 2025 macro (built 2026-09-26)

Turns an architect's CAD title sheet (DWG or DXF, plus its PDF print) into a ready-to-use Tekla drawing layout in the same style as the P-142 A1 client sheet ([TEKLA_A1_CLIENT_TEMPLATE.md](TEKLA_A1_CLIENT_TEMPLATE.md)). One run gives you a frame DXF, a live title block, revision rows, logos and a `.lay` layout.

## Installed files
| Where | What |
|---|---|
| `C:\TeklaStructures\2025.0\Environments\common\macros\modeling\ClientTemplate_Setup.cs` (+ same in `drawings\`) | The Tekla macro |
| `...\common\macros\ClientTemplateTool\ctt_engine.py` | DXF/PDF engine (Python + ezdxf + PyMuPDF) |
| `...\ClientTemplateTool\field-map.ini` | Default label wording (copied into each model's input folder) |
| `...\ClientTemplateTool\seed\seed_layout.lay` | Copy of the verified `P142_A1_CLIENT.lay`, used as the binary template for new `.lay` files |
| `...\ClientTemplateTool\wheels\` | Offline Python packages (ezdxf 1.4.4, pymupdf 1.28.2, numpy, fonttools, …) |
| `...\ClientTemplateTool\libredwg\` | LibreDWG 0.13.3 `dwg2dxf.exe` (GPL), used for DWG → DXF |
| `...\ClientTemplateTool\venv\` | Private Python env, created on first run from the system Python 3.12 |

## How to use (each new project)
1. Open the project model and run **ClientTemplate_Setup**: Quick Launch → `Macro.ClientTemplate_Setup`, or Applications & components. The first time, it creates `<model>\CLIENT_TEMPLATE_INPUT\` and opens that folder.
2. Drop the architect's **DWG** (or DXF) and its **PDF** into that folder. The PDF supplies the logos.
3. Run the macro again and enter a layout name (for example `P150_A1`). Tick *Overwrite* when you re-run with the same name.
4. The macro writes to the model folder:
   - `<name>_FRAME.dxf`
   - `<name>_TITLE.tpl`, `<name>_REV.tpl` and `<name>_LOGOS.tpl`
   - `<name>_LOGO_n.png`
   - `attributes\<name>.lay`
   
   A report (`<name>_report.txt` in the input folder) lists each matched field, anything missing, and the positions used.
5. **Close and reopen any drawing.** The layout list only refreshes when a drawing is reopened. Then set Drawing properties → Layout → `<name>` and Drawing size (for example A1 841x594). Press **Get**, untick all, and tick only Layout before **Modify**.

## What it does
- Reads the paper-space layout that has the most content (otherwise model space).
- Takes the largest rectangle as the sheet border and scales it to the ISO size, using the PDF page size, else an "A1"-type text on the sheet.
- **Live fields** come from labels in `field-map.ini`:
  - DRAWING NO → `TITLE` (drawing Name)
  - DRAWING TITLE → `TITLE1`/`TITLE2`/`TITLE3`
  - DRAWN / CHECKED → UDAs `DR_DRAWN_BY` / `DR_CHECKED_BY`
  - REVISION → `REVISION.MARK`
  
  The value to the right of or below each label is removed from the frame and replaced by the field, at the same position and height.
- **Revision table**: found from the NO/REV + DESCRIPTION + DATE headers. The sample row under the header becomes the Tekla revision row. The row height comes from the grid lines, and rows stack upwards with the newest on top.
- **Logos**: images in the PDF, saved as PNGs and placed at their PDF positions.
- **Frame**: everything else is copied 1:1 in mm, without viewports, OLE logos or anything outside the border. Tekla places a DXF by its extents, so the `.lay` DWG offset makes up for border line width that goes past the corner.
- The field font size is the CAD height divided by 0.955, because Tekla draws template text at 0.955 × its font size.

## Verification (P-142 sheet, test layout TEST142_A1 on drawing [57])
Tekla DXF export compared with the verified P142_A1_CLIENT export:
- The frame is within 0.03 mm.
- Drawing No, Drawn, Checked and Revision fields are within 0.05 mm.
- Revision rows are identical (266.5).
- Title lines are within 0.15 mm (positions from the PDF).
- Text heights now match CAD: 2.49 vs 2.46, where P-142 had 2.39.

## Known limits
- Multi-line titles (MTEXT) take their line positions from the PDF when it has them; without the PDF they can be about 1 mm off.
- A CENTERED field's box width is estimated (length × 0.6 × font size), so centred text can sit about ±1 mm off.
- If the sample revision is not in the lowest row, the report warns. Tekla rows always stack upwards from that row.
- Values inside nested blocks can't be removed. The report says when this happens.
- LibreDWG can mis-read unusual DWG content. If the frame looks wrong, save the DWG as DXF in AutoCAD and put the DXF in the folder instead; a DXF takes priority over a DWG.
- Manual touches made on P-142 (for example `\W0.88` on the Damac address) are not reproduced.
- **Deployment gotcha found while testing:** Tekla shows macro dialogs behind its window. The macro now minimises and restores each dialog so it comes to the front.
