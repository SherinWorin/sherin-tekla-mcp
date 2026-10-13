# Tekla Structures plugin

Workflows for Tekla Structures 2025, built on the Tekla connector running on your computer.

| Skill | What it does |
|---|---|
| `/tekla-model-qa` | Invalid objects, orphan subassemblies/rebars, clash check, unmarked objects → one QA report; highlights problems in red |
| `/tekla-drawing-issue` | Find drawings by type/mark/title, check status, print PDF and export DWG/DXF/DGN in batches, optional drawing register |
| `/tekla-material-report` | Material / assembly / bolt takeoff via Tekla report templates or a grouped Excel takeoff with tonnage totals |

## Requirements
- Tekla Structures 2025 open with a model loaded
- The Tekla connector (tekla-mcp) running on the same computer, and the Claude desktop app open and linked to the session

## Design
Every skill is written to minimise tool calls: combined filters, one parallel batch of checks, GUID-only probes before property pulls, and batch print/export by mark list.
