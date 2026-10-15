# Sherin Tekla MCP

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
```

## Status

v0.1.0 - first version. Core logic is unit-tested and CI builds the Tekla 2025 version on Windows. **The live Tekla connection still has to be tested on your Tekla 2025 install** - start in read-only mode on a copy of a model. See [docs/TESTING.md](docs/TESTING.md) and [TODO.md](TODO.md).

## Credits

Design ideas (not code) learned from [teknovizier/tekla_mcp_server](https://github.com/teknovizier/tekla_mcp_server), [tempalg1n/tekla-mcp-server](https://github.com/tempalg1n/tekla-mcp-server) and [40ushek/svMCP](https://github.com/40ushek/svMCP). Uses the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## License

MIT - see [LICENSE](LICENSE). Tekla and Trimble are trademarks of Trimble Inc.; this project is not affiliated with Trimble. Tekla DLLs are not included - they are loaded from your own licensed installation.
