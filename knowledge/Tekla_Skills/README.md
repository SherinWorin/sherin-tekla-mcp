# Tekla Structures 2025 – Skills bundle

Exported 2026-10-13 from the "AI TEKLA STRUCTURES 2025 — INTELLIGENT MODELING TOOL" project.

## Contents
| Folder / file | What it is |
|---|---|
| `tekla-mcp-server/SKILL.md` | Main skill: how to drive Tekla through MCP tools, safety tiers, traps |
| `tekla-mcp-server/references/tool-reference.md` | Every tool + resource of teknovizier/tekla_mcp_server with parameters |
| `tekla-mcp-server/references/patterns-for-csharp.md` | Design patterns to port into the C# AI-TEKLA project |
| `tekla-mcp-server/references/setup-tekla-2025.md` | Install + Claude Desktop config for Tekla 2025 |
| `tekla-structures-plugin/skills/tekla-model-qa/SKILL.md` | Model QA workflow (invalid, orphans, clashes, unmarked) |
| `tekla-structures-plugin/skills/tekla-drawing-issue/SKILL.md` | Drawing find / print PDF / export DWG workflow |
| `tekla-structures-plugin/skills/tekla-material-report/SKILL.md` | MTO / takeoff / tonnage workflow |
| `ALL_TEKLA_SKILLS.md` | All of the above in ONE file – paste into any LLM as context |
| `tekla_skills.jsonl` | One JSON record per file ({id, path, name, description, content}) for RAG / fine-tune pipelines |

## How to use with other LLMs
- **Quick test (any chat LLM):** paste `ALL_TEKLA_SKILLS.md` as system prompt / first message.
- **RAG / knowledge base (Open WebUI, AnythingLLM, LlamaIndex):** upload the .md files or `tekla_skills.jsonl`.
- **Fine-tuning:** these are instructions, not Q&A pairs. Convert to prompt/response pairs first.

## Licence note
`tool-reference.md` is copied from teknovizier/tekla_mcp_server docs (GPL-3.0). Keep that attribution if you redistribute.
