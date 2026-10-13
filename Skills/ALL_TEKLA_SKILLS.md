# ALL TEKLA STRUCTURES 2025 SKILLS (combined)

Generated 2026-10-13. Each section below is one skill/reference file.


---

<!-- FILE: tekla-mcp-server/SKILL.md -->

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


---

<!-- FILE: tekla-mcp-server/references/patterns-for-csharp.md -->

# Patterns from tekla_mcp_server to reuse in the C# AI-TEKLA 2025 project

These are design ideas, not code to copy (the repo is GPL-3.0; copying code would make our tool GPL too). Re-implement in C# against verified 2025 assemblies.

## 1. Tool contract = providers grouped by category
Upstream groups tools into providers: Selection, Properties, Components, View, Operations, Drawings, Modeling, plus Resources. Each tool has typed, described parameters and returns `{status: success|warning|partial|error, ...}`.
C# mapping: one `IToolProvider` per category in `AiTekla.Tools`, each tool a class with a JSON schema, a `RiskLevel`, and an `Execute(args)` returning a typed result DTO with the same status vocabulary.

## 2. Risk annotations drive visibility AND confirmation
Upstream: `readOnlyHint` / `destructiveHint` on every tool; `read_only` mode hides writers; a CI test fails if any tool lacks the hints; another test checks docs list exactly the tools in code.
Their own warning: hiding is not blocking. In C#: the executor itself must refuse MEDIUM/HIGH tools when read-only, and require a confirmation token for HIGH. Add a unit test that every registered tool declares a risk level, and one that `docs/TOOLS.md` matches the registry.

## 3. Resources for discovery, tools for actions
Small read-only lists (element types, filters, phases, grids, materials, rebar catalog, macros, report templates, connection status, project context files) are resources. Anything with logic is a tool. Never return large datasets.
Fits the project's "AI context pipeline" (Section 18): the AI reads resources to plan, then calls tools.

## 4. Selection-centred workflow
Almost every tool acts on the current selection: select -> inspect -> act -> verify. Selection tools build Tekla filter expressions from structured input (standard string/numeric filters + UDA filters, AND/OR). A C# `FilterBuilder` service mirroring this keeps the AI away from raw filter syntax.

## 5. Three property-read modes
`flat` (tables with UDAs + chosen report properties), `snapshot` (everything, for convention checking), `guids_only` (cheap). Useful to control token use. In 2025, check the new multi-object report-property call for bulk reads.

## 6. Config-driven rules, not hard-coded
`element_types.json` (type -> Tekla classes + numbering prefixes), `base_components.json` (component name/number + attribute schema + handler config), `report_properties.json`, `semantic_overrides.json`, and Markdown project context files. Matches project Section 13: base-skid rules, boundary-wall rules etc. belong in config/context files.

## 7. Component handler plug-ins
A registry of handler classes keyed by Tekla component name with `pre_process` / `post_process` hooks (example: lifting-anchor type chosen by element weight, placed at centre of gravity). C# equivalent: `IComponentHandler` registered by name, configured from JSON. Good model for pod/base-skid rule generators.

## 8. Semantic attribute mapping
User words ("concrete cover thickness") -> component attribute names via a small embedding model, falling back to the LLM when confidence is low (thresholds in settings). Optional; a simple alias table (like our `field-map.ini`) covers most cases first.

## 9. Engineering traps (apply in C#)
- Save/restore the work plane in `try/finally`.
- `CommitChanges()` after model edits; drawing edits commit on the drawing.
- Do not persist drawing view IDs.
- Wrap every tool in one error handler that turns exceptions into a structured error result and logs details (their `@mcp_handler`). Raise instead of returning early.
- Test objects prefixed `MCP_TEST_`; functional tests only on scratch models; unit tests never need Tekla running.

## 10. Drawing QA ideas worth borrowing
`check_drawing_collisions` (export to DXF, analyse overlaps, draw coloured revision clouds), `check_for_unattached_dimensions`, `check_for_unmarked_objects`, `align_section_views`, print with automatic paper size detection. These complement our existing `P142_BF_AutoDimension` and client-template work.


---

<!-- FILE: tekla-mcp-server/references/setup-tekla-2025.md -->

# Running tekla_mcp_server with Tekla Structures 2025 and Claude Desktop

Status: **unverified on 2025** (upstream tests 2022 and 2026). Try it on a scratch copy of a model first, in read-only mode.

## 1. Get the code and install (PowerShell, Windows)

```powershell
# PowerShell (Windows)
cd C:\Users\sheri\Sherin_AI
git clone https://github.com/teknovizier/tekla_mcp_server.git
cd tekla_mcp_server
py -3.12 -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
```

Python 3.11-3.13. It must run on Windows (pythonnet loads the Tekla .NET DLLs), not in WSL.

## 2. Config (`config\` folder)

Copy each `*.sample.json` to the same name without `.sample`:
`settings.json`, `element_types.json`, `semantic_overrides.json`, `base_components.json`, `report_properties.json`.

```powershell
# PowerShell (Windows), inside tekla_mcp_server
Get-ChildItem config\*.sample.json | ForEach-Object { Copy-Item $_ ($_.FullName -replace '\.sample','') }
```

Edit `config\settings.json`:
```json
"tekla_path": "C:\\Program Files\\Tekla Structures\\2025.0\\bin",
"read_only": true,
"embeddings": { "enabled": false }
```
Start with `read_only: true` and embeddings off; turn them on once the connection works.

`element_types.json` maps element types to Tekla classes and numbering prefixes. The sample is precast-concrete oriented (classes 1-18, steel 100-103). Replace it with the classes used on the pod/base-frame models before using `select_elements_by_filter` with `element_type`.

Put project rules as Markdown into `config\context\` (for example a copy of `base-skid-rules.md`). The AI can read them through the `project://context` resource.

The repo also ships Tekla macros in `config\macros\modeling\` (`TeklaMCPPrintDrawings.cs`, `TeklaMCPExportDrawings.cs`, `TeklaMCPSelectModelObjectsFromDrawings.cs`, ...) and attribute files in `config\attributes\`. The print/export/collision tools need these copied into the Tekla macro folder (`XS_MACRO_DIRECTORY`, e.g. `C:\TeklaStructures\2025.0\Environments\common\macros\modeling`) and the model `attributes` folder.

## 3. Connect Claude Desktop

`%APPDATA%\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "tekla-mcp": {
      "command": "C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\.venv\\Scripts\\python.exe",
      "args": ["C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\src\\tekla_mcp_server\\mcp_server.py"],
      "env": {
        "TEKLA_MCP_LOG_LEVEL": "INFO",
        "TEKLA_MCP_LOG_FILE_PATH": "C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\mcp_server.log",
        "TEKLA_MCP_CONFIG_DIR": "C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\config"
      }
    }
  }
}
```
Restart Claude Desktop. Open Tekla 2025 with a model first.

## 4. First test (read-only)

1. Ask: "Check the Tekla connection status." -> `tekla://connection_status` should say connected.
2. Select a few base-frame members in Tekla, ask: "Get the properties of the selected elements." -> `get_elements_properties`.
3. Ask: "Get coordinates and bounding boxes of the selection."
4. Check `mcp_server.log`: it lists how many of the 9 DLLs loaded. All 9 must load (`Tekla.Structures`, `.Plugins`, `.Model`, `.DataType`, `.Geometry3d.Compatibility`, `.Dialog`, `.Analysis`, `.Catalogs`, `.Drawing`). If any is missing on 2025, note which one - that is a real 2025 difference.

Only then set `read_only: false`, and still confirm every ⚠️ tool call.


---

<!-- FILE: tekla-mcp-server/references/tool-reference.md -->

# Tool and resource reference (teknovizier/tekla_mcp_server, main branch, copied 2026-10-12)

Upstream: https://github.com/teknovizier/tekla_mcp_server/blob/main/docs/reference.md (GPL-3.0). Most tools act on the current selection.

Complete reference for tools and resources available in Tekla MCP Server.

## Tools

Safety annotations:
- 🔒 Read-only - reads model data without modifying it, kept visible in read‑only mode
- ✏️ Creative - creates new model objects without modifying or deleting existing data
- ⚠️ Destructive - modifies or deletes existing model data

Parameter notation:
**`param`** = required, `param` = optional.
Type and default shown where relevant.

| Category | Tool | Description | Parameters |
|----------|------|-------------|------------|
| Selection | 🔒 `select_elements_by_filter` | Select elements by type, Tekla class, name, profile, material, finish, phase, part/assembly prefix/number. Supports AND/OR logic | `element_type` (str), `tekla_classes` (int \| list[int]), `standard_string_filters` (dict), `standard_numeric_filters` (dict), `custom_string_filters` (dict), `custom_numeric_filters` (dict), `combine_with` (str, default: `"AND"`) - all optional |
| Selection | 🔒 `select_elements_by_filter_name` | Select elements using a saved Tekla filter | **`filter_name`** (str) |
| Selection | 🔒 `select_elements_by_guid` | Select elements by GUID | **`guids`** (list[str]) |
| Selection | 🔒 `select_elements_assemblies_or_main_parts` | Switch selection to assemblies or main parts for the currently selected elements | **`mode`** (str): `"Assembly"` \| `"Main Part"` |
| Components | ✏️ `put_components` | Insert a Tekla component on selected elements. Supports semantic attribute mapping and intelligent defaults (e.g. anchor type by weight) | **`component_name`** (str), `properties_set` (str, default: `"standard"`), `custom_properties` (dict) |
| Components | 🔒 `get_components` | Get all components attached to selected elements. Returns name, number, attribute schema and current values | - |
| Components | ⚠️ `modify_components` | Modify attributes of existing components on selected elements | **`component_name`** (str), **`custom_properties`** (dict) |
| Components | ⚠️ `remove_components` | Remove all components with the given name from selected elements | **`component_name`** (str) |
| Properties | 🔒 `get_elements_properties` | Retrieve structured data about selected elements. `mode="flat"` (default) returns flat tables (assemblies, parts, reinforcement, IFC objects) with UDAs and report properties. `mode="snapshot"` returns full snapshots with all report properties, all UDAs, and nested reinforcement - use for convention checking. `mode="guids_only"` returns just each element's GUID (skips property extraction) | `report_props_definitions` (list[str]), `mode` (str: `"flat"` \| `"snapshot"` \| `"guids_only"`, default: `"flat"`) - all optional |
| Properties | ⚠️ `set_elements_properties` | Set properties on selected elements. Parts: name, profile, material, finish, class, numbering, phase. Assemblies: name, numbering, phase. UDAs supported via `user_properties` | `name` (str), `profile` (str), `material` (str), `tekla_class` (int), `finish` (str), `part_prefix` (str), `part_start_number` (int), `assembly_prefix` (str), `assembly_start_number` (int), `phase` (int), `user_properties` (dict) - all optional |
| Properties | 🔒 `get_elements_cut_parts` | Find all boolean cut parts in selected elements, grouped by parent part with GUIDs and profiles | - |
| Properties | ⚠️ `clear_elements_udas` | Clear UDAs from selected parts and assemblies. Clears all UDAs if no names provided | `uda_names` (list[str], optional) |
| Properties | 🔒 `get_elements_coordinates` | Get coordinates of selected elements: start/end points and offsets for beams, contour points for slabs | - |
| Properties | 🔒 `get_elements_bounding_boxes` | Get axis-aligned bounding boxes for selected elements. Returns per element: `element_type` (Tekla C# class name), `min`/`max` corners `{x, y, z}`, `centroid` `{x, y, z}` (mm) | - |
| Properties | 🔒 `compare_elements` | Compare two selected elements and return detailed differences (properties, UDAs, cut parts, welds, reinforcements) | `ignore_numbering` (bool, default: `false`) |
| Properties | ⚠️ `copy_properties_from_ifc` | Copy properties from IFC reference objects to matching Tekla elements by bounding-box overlap | **`user_properties`** (dict[str, str]): IFC property name → Tekla UDA name |
| View | 🔒 `draw_elements_labels` | Draw temporary labels on selected elements. Parts show position, GUID, name, profile, material, finish, class, weight. Assemblies show position, GUID, name, weight | `label` (str, optional), `custom_label` (str, optional) |
| View | 🔒 `zoom_to_selection` | Zoom the active rendered view to fit selected elements | - |
| View | 🔒 `redraw_view` | Redraw the active view | - |
| View | 🔒 `show_only_selected` | Hide everything except selected elements in the active view | - |
| View | 🔒 `hide_selected` | Hide selected elements in the active view | - |
| View | 🔒 `color_selected` | Color selected elements with an RGB colour | **`red`** (int, 0–255), **`green`** (int, 0–255), **`blue`** (int, 0–255) |
| View | 🔒 `apply_view_filter` | Apply a saved view filter to the active view | **`filter_name`** (str) |
| Operations | ⚠️ `cut_elements_with_cutters` | Boolean-cut selected elements using parts identified by class or GUID | `cutter_class` (int \| null), `cutter_guids` (list[str] \| null) - exactly one required, `delete_cutting_parts` (bool, default: `false`) |
| Operations | ⚠️ `convert_cut_parts_to_real_parts` | Convert all cut parts in selected elements into standalone model parts | - |
| Operations | 🔒 `check_for_orphans` | Find orphaned embedded subassemblies or rebars inside the bounding box of selected elements. Returns `{object_guid, target_guid}` pairs to feed to the attach tools | **`mode`** (str): `"subassemblies"` \| `"rebars"` |
| Operations | ⚠️ `attach_assemblies` | Attach specified assemblies to target assemblies | **`pairs`** (list): each item has **`object_guid`** (str), **`target_guid`** (str) |
| Operations | ⚠️ `attach_rebars` | Attach specified reinforcement to target assemblies' main parts | **`pairs`** (list): each item has **`object_guid`** (str), **`target_guid`** (str) |
| Operations | 🔒 `check_for_invalid_objects` | Find invalid objects (missing profile/material, bad geometry, no parent) among selected elements and their bounding boxes | - |
| Operations | 🔒 `clash_check` | Run Tekla's clash check against the current selection. Returns one record per detected clash with `object1`, `object2` (each: `guid`, `name`, `profile`, `material`, `tekla_class`, `top_assembly_guid`), `clash_type`, and `overlap` (only for `CLASH_TYPE_CLASH`). When `filter_name` is supplied, selected assemblies are expanded to their child parts and reinforcement before the check runs, only objects matching the named filter are selected and checked | `min_distance` (float, mm, default: `0.0`), `between_parts` (bool, default: `true`), `between_reference_models` (bool, default: `false`), `objects_inside_reference_models` (bool, default: `false`), `filter_name` (str) - all optional |
| Operations | 🔒 `create_report` | Create a Tekla report from the current selection using a report template. Waits for the file to appear and returns `content_preview` (configurable via `reports.preview_max_chars`) + `size_bytes`. When `output_folder` is omitted, writes to `XS_REPORT_OUTPUT_DIRECTORY` | **`template_name`** (str), `output_filename` (str, optional - defaults to template name), `output_folder` (str, optional), `title1`/`title2`/`title3` (str) - optional, `return_full_content` (bool, default: `False`) |
| Operations | ⚠️ `run_macro` | Run a Tekla macro by filename | **`macro_name`** (str) |
| Operations | 🔒 `select_model_objects_from_drawings` | Select the model objects corresponding to the currently selected drawings | - |
| Drawings | 🔒 `get_drawings` | Get drawings with optional filtering | `drawing_type` (str: `G`/`A`/`W`/`C`/`M`), `name_filter` (dict), `mark_filter` (dict), `title1_filter` (dict), `title2_filter` (dict), `title3_filter` (dict) - all optional |
| Drawings | 🔒 `get_drawings_properties` | Get properties of drawings by mark list, or the currently selected drawings if omitted | `marks` (list[str], optional) |
| Drawings | ⚠️ `set_drawings_properties` | Set name, titles and UDAs on drawings by mark list, or the currently selected drawings if omitted. Does not require any drawing to be open | `marks` (list[str], optional), `name` (str), `title1`/`title2`/`title3` (str), `user_properties` (dict) - all optional |
| Drawings | ⚠️ `set_drawings_issue_state` | Issue or unissue drawings by mark list, or the currently selected drawings if omitted. Drawing-list level action, no drawing needs to be open | `marks` (list[str], optional), `action` (str: `"issue"`/`"unissue"`, default: `"issue"`) |
| Drawings | ⚠️ `update_drawings` | Update drawings from the model by mark list, or the currently selected drawings if omitted. Already up-to-date drawings are skipped, not errors. None of the target drawings can be the active drawing | `marks` (list[str], optional) |
| Drawings | ✏️ `check_drawing_collisions` | Detect drawing-layout problems in drawings selected in the Document Manager (or by `marks`) by exporting each to DXF and analysing geometry: mark overlaps, view-vs-view overlaps, views outside the sheet grid, view frames hitting sheet furniture, and content crossing the sheet boundary. None of the target drawings can be the active drawing, and all must be up to date. A revision cloud is drawn at every found problem, colored by category: Red for geometry/view-frame collisions, Blue for mark overlaps, Cyan for content outside the sheet | `marks` (list[str], optional) |
| Drawings | ✏️ `check_for_unattached_dimensions` | Flag dimensions not attached to part geometry in drawings selected in the Document Manager (or by `marks`) and cloud them. A point is attached at an edge corner, the centre of a bolt/hole/bar/corner cap, or anywhere along a perpendicular edge (part/section edges, reinforcement, hidden lines, grids/centre lines, embeds). Views with part shortening are handled. None of the target drawings can be the active drawing, and all must be up to date. Magenta clouds are drawn at every unattached point. Reports `degraded_dimensions` - dimensions whose anchor state could not be read, so the check was not fully conclusive for them | `marks` (list[str], optional) |
| Drawings | 🔒 `print_drawings` | Print drawings to PDF with automatic paper size/orientation/multi-sheet tiling detection (A0-A4, or a clean tiling of one). With `print_settings`, customer settings are used as-is. Per-mark failures are reported in `errors`, a batch whose file count falls short (marks cannot be tied to output files) is reported once in `warnings` | `marks` (list[str], optional), `print_settings` (str, optional) |
| Drawings | 🔒 `export_drawings` | Export drawings to DWG/DXF/DGN via the Document Manager export macro. With `export_settings`, customer settings are used as-is | `marks` (list[str], optional), `drawing_format` (str: `dxf`/`dwg`/`dgn`, default: `dwg`), `version` (str: `2000`/`2004`/`2007`/`2010`/`2013`, default: `2010`), `export_settings` (str, optional) |
| Drawings | 🔒 `open_drawing` | Open a drawing by its mark in Tekla's drawing editor. | **`mark`** (str) |
| Drawings | 🔒 `close_drawing` | Close the active drawing | `save` (bool, default: `true`) |
| Drawings | 🔒 `get_drawing_views` | List all views in the active drawing with type, scale, position, size and display settings. Includes the sheet view (`is_sheet=true`, with its own size), needed to read title block annotations via `get_view_annotations`. Returns the total sheet count too. If the sheet combines multiple standard-size pages, each model view gets a `sheet_number` (1-based, top-left page first, or null if outside the page grid) | - |
| Drawings | 🔒 `get_view_objects` | List the model objects (parts, rebars, etc.) shown in a view, resolved to name/profile/material/class. Bolts also report their standard, size, count and connected parts. Parts of an embedded detail sub assembly are reported once as the subassembly. Model objects only, not annotations. No geometry | **`view_key`** (str), `limit` (int, default: `200`) |
| Drawings | 🔒 `get_view_annotations` | Read the marks, dimensions and text in a view with their content (mark string, dimension value, text). Marks with no readable text (e.g. WeldMark, LevelMark) report content "N/A". For the sheet view, only `text`/`graphics` are returned (marks/dimensions are aggregated per child view). No geometry | **`view_key`** (str), `type_filter` (str: `all`/`dimensions`/`marks`/`text`/`graphics`, default: `all`), `limit` (int, default: `200`) |
| Drawings | 🔒 `check_for_unmarked_objects` | Find parts, reinforcement and bolts with no mark pointing at them. An embedded detail subassembly is one `parts` unit - a mark on any one of its member parts covers the whole assembly. Reinforcement and bolts inside the assembly are still checked individually, not collapsed. Reports `unresolved_count` - view objects that could not be resolved to a model object and so were not checked | `view_keys` (list[str], optional - checks every non-sheet view in the active drawing when omitted), `categories` (list[str]: `all`/`parts`/`reinforcement`/`bolts`, default: `all`), `limit` (int, default: `200`) |
| Drawings | ⚠️ `move_view` | Move a view by an offset (mm) | **`view_key`** (str), **`dx`** (float, mm), **`dy`** (float, mm) |
| Drawings | ⚠️ `align_section_views` | Align section views in projection with the view they were cut from: a horizontal cut aligns the section's X position, a vertical cut aligns Y (only one coordinate changes). Parent found by matching the section view's name to a section mark of the same name. A section that overlaps the parent on the non-aligned axis by more than `overlap_tolerance` is treated as intentionally placed outside its projection lane (e.g. parked in a row below the parent) and left as-is. `view_keys` aligns only the listed section views. Views that could not be moved are reported in `failed` and the loop continues rather than aborting | `view_keys` (list[str], optional - aligns all when omitted), `overlap_tolerance` (float, default: `5.0`, mm) |
| Drawings | ⚠️ `set_views_attributes` | Set display attributes (scale, opening symbols, reflected/undeformed/unfolded) on one or more drawing views | **`views_attributes`** (list): each item has **`view_key`** (str) and at least one of `scale` (float), `show_part_openings_or_recess_symbol` (bool), `reflected_view` (bool), `undeformed_view` (bool), `unfolded_view` (bool) |
| Drawings | ⚠️ `delete_views` | Delete one or more views from the active drawing | **`view_keys`** (list[str]) |
| Drawings | ⚠️ `delete_clouds` | Delete all clouds from model views and the sheet view | `view_keys` (list[str], optional - processes all views when omitted) |
| Modeling | ✏️ `place_beams` | Place one or more beams | **`beams`** (list): each item has **`start_point`**, **`end_point`**, **`profile`**, **`material`**, **`tekla_class`** (int); optional: `start_point_offset`, `end_point_offset`, `name`, `position`, `part_number`, `assembly_number` |
| Modeling | ✏️ `place_columns` | Place one or more columns | **`columns`** (list): each item has **`base_point`**, **`height`** (float, mm), **`profile`**, **`material`**, **`tekla_class`** (int); optional: `start_point_offset`, `end_point_offset`, `name`, `position`, `part_number`, `assembly_number` |
| Modeling | ✏️ `place_panels` | Place one or more wall panels | **`panels`** (list): each item has **`start_point`**, **`end_point`**, **`profile`**, **`material`**, **`tekla_class`** (int); optional: `start_point_offset`, `end_point_offset`, `name`, `position`, `part_number`, `assembly_number` |
| Modeling | ✏️ `place_slabs` | Place one or more slabs | **`slabs`** (list): each item has **`points`** (list of ≥3 points), **`profile`**, **`material`**, **`tekla_class`** (int); optional: `name`, `position`, `part_number`, `assembly_number` |
| Modeling | ⚠️ `move_elements` | Move selected elements by a displacement vector | `dx` (float, mm, default: `0`), `dy` (float, mm, default: `0`), `dz` (float, mm, default: `0`) - at least one must be non-zero |
| Modeling | ✏️ `copy_elements` | Copy selected elements to a new position defined by the displacement vector, keeping originals in place. Assemblies are expanded to all constituent parts recursively | `dx` (float, mm, default: `0`), `dy` (float, mm, default: `0`), `dz` (float, mm, default: `0`) - at least one must be non-zero |
| Modeling | ✏️ `place_grid` | Place a rectangular grid in the Tekla model | **`x`** (list[float], mm), **`y`** (list[float], mm), `z` (list[float], mm), `x_labels` (list[str]), `y_labels` (list[str]), `z_labels` (list[str]), `origin` (PointInput), `name` (str) |
| Modeling | ⚠️ `delete_selected` | Delete all selected elements | - |
| Modeling | ✏️ `create_phase` | Create a new phase in the Tekla model | **`phase_number`** (int), `name` (str, optional) |

## Resources

| Resource | Description |
|----------|-------------|
| `project://context` | Index of project context Markdown files (name, description, file key) |
| `project://context/{file}` | Full content of a specific project context Markdown file |
| `tekla://components` | Mapping of Tekla component name → config key for all components configured on the server |
| `tekla://components/{component_key}` | Description and `custom_properties` schema (attribute keys, types, allowed values) for a specific component |
| `tekla://catalog/materials` | All available material names from the Tekla material catalog |
| `tekla://catalog/rebars` | All available rebar grades and sizes from the Tekla rebar catalog |
| `tekla://macros` | Available Tekla macro filenames from `XS_MACRO_DIRECTORY` |
| `tekla://reports` | Available Tekla report template names (`.rpt` files) from `XS_TEMPLATE_DIRECTORY` |
| `tekla://element_types` | Element types with their Tekla class numbers from `element_types.json` |
| `tekla://filters/selection` | Available Tekla selection filter names (`.SObjGrp` files) |
| `tekla://filters/view` | Available Tekla view filter names (`.VObjGrp` files) |
| `tekla://phases` | All phases in the current model (number, name, comment) and the active phase number |
| `tekla://grids` | Rectangular grid data: guid, name, X/Y/Z axes with coordinates and labels |
| `tekla://connection_status` | Current Tekla connection status: connected (bool), message |

Filter resources search in:
- `XS_FIRM` advanced option directories
- `XS_PROJECT` advanced option directories
- Model attributes directory (`./attributes`)


---

<!-- FILE: tekla-structures-plugin/skills/tekla-drawing-issue/SKILL.md -->

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


---

<!-- FILE: tekla-structures-plugin/skills/tekla-material-report/SKILL.md -->

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


---

<!-- FILE: tekla-structures-plugin/skills/tekla-model-qa/SKILL.md -->

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
