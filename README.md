# Sherin Tekla MCP

[![CI](https://github.com/SherinWorin/sherin-tekla-mcp/actions/workflows/ci.yml/badge.svg)](https://github.com/SherinWorin/sherin-tekla-mcp/actions/workflows/ci.yml)
![Tekla Structures 2025](https://img.shields.io/badge/Tekla%20Structures-2025-0063a3)
![MCP](https://img.shields.io/badge/MCP-server-6e40c9)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

**An MCP server that lets Claude (or any MCP client) work with Tekla Structures 2025 - safely.**

Built by **Sherin Worin** as part of the *AI Tekla Structures 2025 - Intelligent Modeling Tool* project.

The AI never writes or runs code inside Tekla. It can only call a fixed set of typed C# tools:

```
AI reasoning -> MCP tool call -> C# server -> Tekla Open API (2025) -> read-back validation -> report
```

## What it can do (v0.1)

| Risk | Tools |
|---|---|
| **LOW** - read / calculate / UI only | `tekla_get_connection_info`, `tekla_get_selected_objects`, `tekla_get_objects`, `tekla_find_objects`, `tekla_select_objects`, `tekla_highlight_objects`, `tekla_quantity_report` (BOQ + CSV), `tekla_validate_objects`, `tekla_calculate_distance`, `tekla_list_rules`, `tekla_plan_wall`, `tekla_divide_opening` |
| **MEDIUM / HIGH** - changes the model | `tekla_create_beams`, `tekla_create_columns`, `tekla_modify_objects` |
| **HIGH** - off by default | `tekla_delete_objects` |

Things you can ask:

- "Show me all beams with profile IPE200." / "How many columns are in this model?"
- "Total running length and weight of each profile in my selection - and save it as CSV."
- "Check the selected parts for errors and highlight the problems."
- "Plan a 35.5 m boundary wall with the boundary-wall rule."
- "Create an IPE200 S355JR beam from 0,0,3000 to 6000,0,3000." (preview -> you confirm -> created -> verified)
- "Change the selected members to S355JR."

Full list with parameters: [docs/TOOLS.md](docs/TOOLS.md).

## Safety by design

- **Read-only by default.** Model changes are refused until you set `"ReadOnly": false` in `config/settings.json`. The check is in the code path of every write - not just a hidden tool.
- **Two-step writes.** Every change first returns a *preview* (plan, old -> new values, risk level) and a `confirmToken`. Apply only works with that token **and the same arguments**, within 10 minutes, in the same server run.
- **Risk levels** (LOW / MEDIUM / HIGH). Delete is always HIGH and disabled unless `"AllowDelete": true`.
- **Catalog check** of profiles and materials before anything is created or changed.
- **Read-back validation** after every write, and an **operation log** (`operations-YYYYMMDD.jsonl`: time, tool, user intent, objects, old/new values, status, errors).
- **Global coordinates** for every point; the user's work plane is always restored.
- **Rules in config files**, not in code (`config/rules/*.json`).

## Quick start

1. Get the build: download the `sherin-tekla-mcp-tekla2025-net48` artifact from the latest successful run on the **Actions** tab, **or** build `SherinTeklaMcp.sln` in Visual Studio (Release).
2. Add it to Claude Desktop - see [docs/SETUP.md](docs/SETUP.md).
3. Open Tekla Structures 2025 with a model, then ask Claude: *"Check the Tekla connection."*

## Project layout

```
src/SherinTeklaMcp.Core     Tekla-free logic: models, geometry, rules, quantities, validation,
                            safety (risk, confirm tokens), operation log, write pipeline, mock backend
src/SherinTeklaMcp.Tekla    Tekla Structures 2025 backend (.NET Framework 4.8, x64)
src/SherinTeklaMcp.Server   MCP server (stdio) and the tool definitions
tests/                      Unit tests (no Tekla needed)
config/                     settings.json and rules/*.json
scripts/smoke_test.py       Talks to the server like an MCP client (used by CI)
docs/                       SETUP, ARCHITECTURE, TOOLS, API_NOTES, TESTING
macros/wall-framing/        A standalone Tekla 2025 drawing macro (see below)
HANDOVER/                   Hand-over pack: the state of the work, the hard
                            rules, the Tekla 2025 traps, and how to continue
                            this project on another computer
```

## Also in this repository: the wall-framing auto-dimensioner

A **standalone drawing macro** for Tekla Structures 2025, independent of the MCP
server: it reproduces the annotations of a `WALL FRAMING LAYOUT` GA drawing
(stud chains, band chains, top outline, overall dimensions, north run,
wall-panel marks, section marks, notes).

- It finds the drawing by its **title**, not by a fixed mark, so the same file
  works on every computer of a project. When the model has one wall layout
  drawing per block, it works on **all** of them, one after the other, and it
  dimensions only the walls that the drawing actually shows.
- It **never writes into an issued or frozen drawing** - the macro refuses it.
- The rules and the licence engine live in their own assemblies with **no
  Tekla reference**; the macro catalog gets a **thin loader** that loads the
  modules at run time.
- Two editions: a **free** one (the dimension chains) and a **Pro** one
  (everything), unlocked by a licence key. Without a key the Pro features are
  free for three days. See
  [macros/wall-framing/docs/LICENSING.md](macros/wall-framing/docs/LICENSING.md).
- A deployment package (`macros/wall-framing/package/WallFramingAutoDimension.zip`,
  and the Pro one beside it) installs it on another computer.
- A **second macro**, `WallFramingAutoDimensionPro-AIO`, is **free** - it asks
  for no licence key - and it is for a GA sheet that carries **many** wall
  framing layouts: it dimensions every block on its own, so that the chain of
  one block does not run into the next one.

See [macros/wall-framing/README.md](macros/wall-framing/README.md) and
[macros/wall-framing/INSTALL_NOTES.txt](macros/wall-framing/INSTALL_NOTES.txt).

## Handing the project over to another computer

[HANDOVER/](HANDOVER/README.md) is the hand-over pack. It is written for a
fresh Claude instance on another PC: what the project is, what state it is
in, the rules that must not be broken, the Tekla 2025 behaviour that cost
the most time to find, every command to rebuild and re-verify, and the
prompt to paste into the new session.

Start at [HANDOVER/00-START-HERE.md](HANDOVER/00-START-HERE.md).

## Status

v0.1.0 - first version. Core logic is unit-tested and CI builds the Tekla 2025 version on Windows. **The live Tekla connection still has to be tested on your Tekla 2025 install** - start in read-only mode on a copy of a model. See [docs/TESTING.md](docs/TESTING.md) and [TODO.md](TODO.md).

## Testers wanted

This is an early version. If you have Tekla Structures 2025, you can help a lot in 15 minutes:

1. Follow [docs/SETUP.md](docs/SETUP.md) (it starts **read-only**, so it cannot change your model).
2. Run the first steps of [docs/TESTING.md](docs/TESTING.md) on a **copy** of a model.
3. Open a **[Tekla test report](https://github.com/SherinWorin/sherin-tekla-mcp/issues/new?template=tekla-test-report.yml)** - what worked, what did not.

Ideas for tools (connections, plates, drawings, your own modeling rules)? Open an issue. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Credits

Design ideas (not code) learned from [teknovizier/tekla_mcp_server](https://github.com/teknovizier/tekla_mcp_server), [tempalg1n/tekla-mcp-server](https://github.com/tempalg1n/tekla-mcp-server) and [40ushek/svMCP](https://github.com/40ushek/svMCP). Uses the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## License

MIT - see [LICENSE](LICENSE). Tekla and Trimble are trademarks of Trimble Inc.; this project is not affiliated with Trimble. Tekla DLLs are not included - they are loaded from your own licensed installation.
[![M8ven Score](https://m8ven.ai/badge/mcp/sherinworin-sherin-tekla-mcp-15tlyj?v=d803da16f10daa6533891ad3f0921a72)](https://m8ven.ai/mcp/sherinworin-sherin-tekla-mcp-15tlyj?s=readme)
