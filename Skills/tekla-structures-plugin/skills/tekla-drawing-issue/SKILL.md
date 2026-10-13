---
name: tekla-drawing-issue
description: Prepares and issues Tekla Structures 2025 drawings through the Tekla connector — finds drawings by type/mark/name/title, checks their status, optionally checks for unmarked objects, then prints to PDF and/or exports to DWG/DXF/DGN in batches. Use when the user says "issue drawings", "print drawings to PDF", "export DWG", "send assembly drawings", "drawing list", "which drawings are out of date", or names drawing marks to print or export.
---

# Tekla Drawing Issue

Find the right drawings, check them, and print/export them with as few calls as possible. Tool names below are the Tekla connector's tools (in Cowork: `mcp__remote-devices__tekla-mcp__<tool>`).

## Efficiency rules

- One `get_drawings` call with every filter combined — never list all drawings and filter locally.
- Pass explicit `marks` lists to print/export so one call covers the whole set; `print_drawings` already batches by paper size.
- Only open drawings when a per-drawing check was asked for.
- Run PDF print and DWG export in the same turn when both are wanted.

## 1. Parse the request

Extract, without asking, whatever the user already said:

| Item | Maps to |
|---|---|
| Drawing type (GA / assembly / single part / cast unit / multi) | `drawing_type`: G / A / W / C / M |
| Marks, prefixes ("all B-", "starting with HCS") | `mark_filter` (`Starts With`, `Contains`, `Is Equal`; several conditions with `logic: "OR"`) |
| Name or title text ("level 2", "phase 3") | `name_filter` / `title1_filter`… |
| Output | PDF (`print_drawings`), DWG/DXF/DGN (`export_drawings`), or both |
| Settings | named `print_settings` / `export_settings` if the user gave one; otherwise auto |

Ask one AskUserQuestion only for what is truly missing (usually output format). Default: PDF, auto paper size; DWG version 2010.

## 2. Find and check

1. `get_drawings(...)` with all filters in one call. Report the count. If 0, suggest a loosened filter once.
2. `get_drawings_properties(marks=[...])` for the found marks — output the table exactly as returned. Flag drawings whose status shows them as out of date, not up to date, or frozen/locked-issue states, and ask whether to include them (one question covering all flagged drawings).
3. Unmarked-object check — only if the user asked ("check marks before issuing"): for each drawing, `open_drawing(mark)` → `check_for_unmarked_objects(categories=["all"])` (empty `view_keys` = all views). The connector requires the open drawing to be closed before opening the next; if no close tool is available, check only the first drawing and tell the user.

## 3. Issue

In one turn:
- PDF: `print_drawings(marks=[...], print_settings=<name or omit>)`
- CAD: `export_drawings(marks=[...], drawing_format="dwg"|"dxf"|"dgn", version=<if given>, export_settings=<name or omit>)`

## 4. Report

```
Issued 24 drawings (A-type, marks B1–B24)
PDF: 24 ✔   DWG 2010: 24 ✔
Skipped: B7, B12 (out of date — re-create in Tekla, then re-run)
```

Include the output location if the tool returns it. Offer to write a drawing register (mark, name, title, revision, status, issued date) to Excel with the xlsx skill — building it from the properties table already fetched, no extra calls.

## Failure handling

- Connection error → Tekla/desktop app closed; say so and stop.
- Print/export fails for some marks → list them with the error; do not retry the whole batch.
