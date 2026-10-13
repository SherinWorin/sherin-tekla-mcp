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
