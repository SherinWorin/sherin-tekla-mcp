---
name: tekla-material-report
description: Builds material, assembly, bolt and weight reports from a Tekla Structures 2025 model through the Tekla connector — either with a Tekla report template or as a grouped Excel takeoff (profile, grade, length, count, weight, area). Use when the user says "material list", "MTO", "takeoff", "tonnage", "steel weight", "bolt list", "assembly list", "cast unit list", "export properties to Excel", or asks for quantities of a phase, filter or selection.
---

# Tekla Material Report

Produce a takeoff for a scope of the model in the fewest calls. Tool names are the Tekla connector's tools (in Cowork: `mcp__remote-devices__tekla-mcp__<tool>`).

## Efficiency rules

- Prefer a native Tekla report template (`create_report`) — one call does the whole scope inside Tekla.
- Fall back to `get_elements_properties(mode="flat")` with every needed extra column requested in a single call; never loop per element.
- Read `tekla://reports` or `tekla://filters/selection` at most once per session.
- Do the grouping/summing in one Python step, not by hand.

## 1. Scope

Reuse any scope the user gave. Otherwise one AskUserQuestion: current selection / saved selection filter / property filter (phase, class, name, profile).

- Saved filter → `select_elements_by_filter_name`.
- Property filter → `select_elements_by_filter` (e.g. `standard_string_filters.phase`).
- For assembly-level lists, then `select_elements_assemblies_or_main_parts(mode="Assembly")`.

Probe with `get_elements_properties(mode="guids_only")` and state the count.

## 2. Choose the route

**Route A — Tekla template (default when a matching template exists)**
Read `tekla://reports` once. Match the request: material list / part list / assembly list / bolt list / cast unit list. Then:
`create_report(template_name=..., title1=<project or scope>, title2=<phase/filter>, output_folder=<if user gave one>, return_full_content=true)`
Use the returned text as the data for the summary.

**Route B — Grouped takeoff (no suitable template, or user wants Excel with custom grouping)**
Single call: `get_elements_properties(mode="flat", report_props_definitions=[...])` requesting only what's needed, typically: `profile`, `material`, `length`, `weight`, `area`, `phase`, `assembly position`, plus any UDA the user mentioned.
Then group in Python by profile + material (or by assembly/phase if asked): count, total length (m), total weight (t), total area (m²).

Bolts: select bolts by filter (or the user's saved bolt filter) and prefer a bolt-list template; if none, flat properties grouped by size, grade and length.

## 3. Output

Chat summary first:

```
Material takeoff — Phase 2 (412 parts)
Total steel: 86.4 t  |  Plates: 9.1 t  |  Bolts: 1,236 pcs
Top profiles: HEA300 S355 — 21.7 t; IPE400 S355 — 14.2 t; …
```

Then, if the user wants a file (or the list exceeds ~30 rows), build an Excel workbook with the xlsx skill: sheet "Summary" (by profile+grade with totals row), "Detail" (every row), "Bolts" when present. Round weights to 0.01 t, lengths to 0.01 m. When a folder on the user's computer is connected, save beside their project files.

## Failure handling

- `create_report` template not found → re-check the name against the `tekla://reports` list already read; fall back to Route B.
- Weight or length columns empty → the property name differs in this environment; show the columns returned and ask which one to use.
- Connection error → Tekla/desktop app closed; say so and stop.
