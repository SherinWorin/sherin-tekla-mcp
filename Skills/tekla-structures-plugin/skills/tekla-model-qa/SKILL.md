---
name: tekla-model-qa
description: Runs a full QA pass on a Tekla Structures 2025 model through the Tekla connector — invalid objects, orphan subassemblies/rebars, clashes, and (for an open drawing) unmarked parts/bolts/rebar — then reports findings and highlights problems in the model. Use when the user says "check my model", "model QA", "clash check", "find orphans", "invalid objects", "unmarked parts", "is the model ready to issue", or before numbering, drawing issue, or IFC export.
---

# Tekla Model QA

Run a QA pass on the open Tekla model and return one consolidated report. Work through the Tekla connector (tool names below are its tools; in Cowork they appear as `mcp__remote-devices__tekla-mcp__<tool>`).

## Efficiency rules (apply throughout)

- Make the fewest calls possible. Never call a tool "just to look" when a later call returns the same data.
- Run independent read-only checks in parallel in one turn (invalid objects, both orphan modes, clash check can go together once the selection is set).
- Never pull full properties for a large selection. Use `get_elements_properties(mode="guids_only")` to count or probe; use `flat` only on the flagged objects.
- Do not call `redraw_view` right after `color_selected`.
- Do not re-list filters or resources already read in this session.

## 1. Scope (one question max)

If the user already named the scope (a filter name, phase, "everything", "current selection"), skip asking. Otherwise ask once with AskUserQuestion: current selection / a saved selection filter / whole model.

- Saved filter: read `tekla://filters/selection` only if the user did not give an exact name, then `select_elements_by_filter_name`.
- Property-based scope (phase, name, profile, class): `select_elements_by_filter`.
- Whole model: tell the user to select all in Tekla (Ctrl+A in the model view) and confirm, since the checks run on the selection.

Probe the size once with `get_elements_properties(mode="guids_only")` and state the object count.

## 2. Run the checks (one parallel batch)

In a single turn, call:

- `check_for_invalid_objects`
- `check_for_orphans(mode="subassemblies")`
- `check_for_orphans(mode="rebars")` — skip for pure steel scopes (no rebar)
- `clash_check(...)` — pass `filter_name` if the scope was a saved filter; set `between_reference_models` / `objects_inside_reference_models` to true only if the user mentioned IFC/reference models or coordination with other disciplines. Use `min_distance` only if the user gave a clearance.

Drawing-level check — only when a drawing is open or the user asks about marks: `get_drawing_views` once, then `check_for_unmarked_objects(categories=["all"])` with empty `view_keys` (covers every view in one call).

## 3. Highlight problems (optional, one pass)

If any issues were found and the user wants to see them: collect all flagged GUIDs, then `select_elements_by_guid` → `color_selected(255,0,0)` → `zoom_to_selection`. Offer `show_only_selected` for isolation. Do not redraw after coloring.

## 4. Report

Reply with a compact summary, then one table per non-empty category:

```
Model QA — <scope>, <N> objects checked
✔ Invalid objects: 0
✖ Orphan subassemblies: 3
✖ Clashes: 12 (8 part–part, 4 with reference model)
✔ Orphan rebars: 0
```

Tables: GUID, name/profile/position where available, and what is wrong. For clashes, group by object pair type and list the worst first. Keep empty categories to a single ✔ line.

End with the next best action (e.g. "attach the 3 orphan subassemblies, then re-run numbering"). Orphan results come as `{object_guid, target_guid}` pairs; the connector's attach tools are not exposed here, so tell the user the pairs to attach manually in Tekla.

If the user asks for a file, write the report to Excel (xlsx skill) with one sheet per category.

## Failure handling

- Tool call fails with a connection error → Tekla or the desktop app is closed; say so once and stop.
- Empty selection error → ask the user to select objects or give a filter name; do not retry blindly.
