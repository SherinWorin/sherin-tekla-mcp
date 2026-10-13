# P-142 A1 Client Drawing Layout in Tekla 2025 (replaces the AutoCAD sheet step)

Built 2026-09-24 from `P-142-MBCC-SD-BP4-DR-STR-801-R00_ISOMETRIC VIEWS -MOCK UP.dwg`. Verified on test drawing [57] by exporting it to DXF and comparing it with the AutoCAD PDF: every field lands within about 0.1 mm of the AutoCAD positions.

## Files (all in the model folder `AI_TRAINING_2025_PODS`)
| File | What it is |
|---|---|
| `attributes/P142_A1_CLIENT.lay` | Drawing layout, A1 841x594 |
| `P142-A1-FRAME.dxf` | Static sheet: triple border, title strip lines and text, north arrow, key plan, revision table grid, Kling approval stamp. 1:1 mm, origin at the sheet's bottom-left. |
| `P142_A1_TITLE.tpl` | Logos (DMC, STAMP, DAMAC, BURO KLING, MBCC, MI PNGs already in the model folder) plus live fields |
| `P142_A1_REV.tpl` | Revision rows (MARK / DESCRIPTION / DATE_CREATE dd-mm-yyyy), newest on top, rev 00 on the bottom row |
| `P142_A1_STAMP_LOGO.tpl` | Small Kling logo on the approval stamp |

All three templates are anchored at the sheet's bottom-right corner, with the margins built into the templates, so their positions are exact.

## Field mapping (fill these in Tekla, and the title block fills in automatically)
| Title block | Tekla source |
|---|---|
| Drawing title line 1 / 2 | Drawing **Title 1** / **Title 2** |
| Drawing No. | Drawing **Name** (template attribute `TITLE`; note `NAME` returns the mark, e.g. "G [57]") |
| Revision | Current revision mark (`REVISION.MARK`) |
| Drawn / Checked | Drawing UDA **Drawn By** / **Checked By** (`DR_DRAWN_BY`, `DR_CHECKED_BY`) |
| Revision rows | Document manager > Revision: Mark, Description, **Created by > Date** |
| Scale "NA", Sheet "A1", project text, P-142 | Fixed text in the DXF, same as AutoCAD |

## Use
GA drawing properties > Layout > **P142_A1_CLIENT**, drawing size **A1 (841x594)**. Print at A1, 1:1. The MCP `print_drawings` auto-size picked A3 and clipped the page.

When changing an existing drawing's layout, first press **Get**, then untick everything, and tick only Name/Title/Layout/UDA before **Modify**. Otherwise the dialog's `standard` view and mark settings overwrite the drawing's own.

## STR-801 isometric sheet in Tekla: drawing [58]
- [58] is a copy of [51] (Document manager > Copy to new sheet), set to P142_A1_CLIENT / A1, Name P-142-MBCC-SD-BP4-DR-STR-801, Title 1 BP4-MOCK UP, Title 2 ISOMETRIC VIEWS, Drawn SWC, Checked NK.
- **Finding:** the AutoCAD viewport enlarged the Tekla drawing 2.08x, so the PDF's views are really about 1:14.4 while their labels say 1:30. Tekla uses **1:15** (nearest standard scale, true label), so the views are about 4% smaller than in the PDF.
- The views are positioned by the drawing macro `Environments\common\macros\drawings\P142_ArrangeIsoViews_A1.cs`. It finds the 2 on-sheet views, sets the scale to 15 (pass 1, then commit), then sets the origins to View 1 (165.9, 101.1) and View 2 (578.6, 425.3) (pass 2). A single pass does not work because Tekla re-places views after a scale change.
- Verified by DXF export: the view content centres match the AutoCAD positions to within 0.2 mm.
- A new macro only appears in Applications & components after ≡ > Catalog management > **Reload catalog**.
- Remaining look differences: the Tekla view labels are the small default style (AutoCAD's are enlarged with a "3d" bubble), and red dash-dot reference/grid lines show in the Tekla views. Both are view-property settings.

## Known limits / notes
- "Overlap with views" can't be ticked for a DWG item; this caused no problems in the test.
- Tekla caches templates. After editing a .tpl, use Layout editor > right-click the table > Refresh template.
- Typical detail sheets (501/502) are hand-drafted content; they can use the same layout, but their details are not generated from the model.
- Test drawing [57] (with a test revision 00) was left in the model and can be deleted.
