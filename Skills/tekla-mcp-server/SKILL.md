---
name: tekla-mcp-server
description: Lessons and patterns from the open-source teknovizier/tekla_mcp_server (Python + pythonnet MCP server for Tekla Structures). Use whenever working on Tekla AI automation - installing or configuring that MCP server for Tekla 2025, choosing which Tekla MCP tool to call (selection, properties, components, view, operations, drawings, modeling), designing tool/function-calling contracts or safety tiers for the AI-TEKLA 2025 project, or porting its patterns (read-only mode, readOnly/destructive hints, filter builder, work-plane save/restore, component handlers, project context files) into C#. Also use when a Tekla MCP call fails or a Python/pythonnet Tekla connection breaks.
---

# Tekla MCP Server - study notes and how to use them

Source: https://github.com/teknovizier/tekla_mcp_server (GPL-3.0, Python 3.11+, FastMCP 3.x, pythonnet 3.1). Studied 2026-10-12.
It is the closest public reference to the AI-TEKLA 2025 goal: an LLM drives Tekla only through a fixed set of typed tools, never through generated code.

Two ways to use this skill:
1. **Run it** - install the server and connect it to Claude Desktop on the Tekla PC (`references/setup-tekla-2025.md`).
2. **Learn from it** - reuse its tool contract and safety design in the C# AI-TEKLA project (`references/patterns-for-csharp.md`).

The full tool and resource list is in `references/tool-reference.md`. Read it before choosing a tool.

## Version caution (important for this project)

The repo is tested on **Tekla 2022 and 2026 only**, not 2025. It loads DLLs from `<tekla_path>` and `<tekla_path>\Net48Runtime`, so 2025 will probably work, but treat it as unverified until tested on a scratch model. The project rule still holds: anything Tekla-API-specific must be confirmed against the 2025 assemblies. Its `loader.py` imports (Model, ModelObjectSelector, Filtering, Catalogs, Drawing classes) are a useful checklist of classes to confirm on 2025, not proof that they exist there.

Known 2025 differences to keep in mind (from the 2025 Open API release notes in the project):
- Matrix multiplication order changed in `Tekla.Structures` 2025 (A*B now associative). Any ported geometry code that multiplies matrices must be checked.
- `GetReportProperty` can now read values from multiple objects in one call - useful for bulk property reads.

## How to drive Tekla through the MCP tools

The server's design implies a working order. Follow it, because most tools act on the **current selection**:

1. **Check the connection** - read `tekla://connection_status`. Tekla must be running with a model open.
2. **Discover before acting** - read the resources you need (`tekla://element_types`, `tekla://filters/selection`, `tekla://phases`, `tekla://catalog/materials`, `project://context`). Project rules (for example base-skid rules) can be placed in the `context` folder so the AI reads them through `project://context`.
3. **Select** - `select_elements_by_filter` (type/class/name/profile/material/phase, AND/OR), `select_elements_by_filter_name`, or `select_elements_by_guid`.
4. **Inspect** - `get_elements_properties` (`flat`, `snapshot`, or `guids_only`), `get_elements_coordinates`, `get_elements_bounding_boxes`, `compare_elements`.
5. **Act** - only after the user agrees, for anything marked ✏️ or ⚠️.
6. **Verify** - re-read properties, run `check_for_invalid_objects` or `clash_check`, color or label the result (`color_selected`, `draw_elements_labels`) so the user sees what changed.

Safety tiers used by the server (map directly onto the project's LOW/MEDIUM/HIGH levels):
- 🔒 read-only (LOW): selection, properties read, view, clash/orphan/invalid checks, reports, print/export.
- ✏️ creative (MEDIUM): `place_beams/columns/panels/slabs`, `place_grid`, `copy_elements`, `put_components`, `create_phase`, collision clouds.
- ⚠️ destructive (HIGH): `set_elements_properties`, `move_elements`, `delete_selected`, `modify/remove_components`, cuts, attach, `run_macro`, drawing issue/update/delete.

Before any ⚠️ tool, state what is selected (count, types, GUIDs or marks) and what will change, and wait for a yes. `delete_selected` and `move_elements` act on whatever is selected, so re-check the selection right before calling them. For review-only sessions set `"read_only": true` in `settings.json`.

## Lessons worth keeping (from the repo's AGENTS.md "Critical Traps")

- **Work plane**: save the current `TransformationPlane`, set the local one, and restore it in `finally`. Leaving it changed silently corrupts later geometry. Applies equally to C# code.
- **Read-only labels are security-critical**: read-only mode only hides tools; there is no runtime write-block. A writing tool wrongly labeled read-only will run. In the C# project, enforce the risk level in the executor, not only in the tool list.
- **Commit after changes**: `model.CommitChanges()` after modifications; drawings need `drawing.CommitChanges()`.
- **Drawing view IDs are not stable** across sessions - never store them.
- **pythonnet**: Python lists do not marshal to .NET - use `ArrayList`/`List[T]`. `out` params come back as tuple items: `ok, value = obj.GetReportProperty(name, str())`.
- **stdio**: .NET console output is redirected to stderr so it does not corrupt the MCP stream.
- **Tests**: every test object name starts with `MCP_TEST_`; functional tests change the model, so run them only on a scratch model.

## Typical problems

- *Connection fails*: check `tekla_path` in `config/settings.json` (for 2025: `C:\Program Files\Tekla Structures\2025.0\bin`), and that Tekla is open with a model.
- *`clr` import conflict*: delete or rename the stray `site-packages\clr` folder in the Python install.
- *Config change ignored*: config is read once at startup - restart the MCP server (restart Claude Desktop).
- *Semantic attribute mapping fails or hangs*: set `"embeddings": {"enabled": false}`; the MiniLM model (~120 MB) downloads on first use.
