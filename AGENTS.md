# AGENTS.md — AI Tekla Structures 2025 Intelligent Modeling Tool

Read this file first in every session. It tells you what this project is, the rules, and where the knowledge is.

## 1. What we are building
An AI-assisted tool for **Tekla Structures 2025** (C#, .NET Framework 4.8, Tekla Open API).
Flow: AI reasoning → fixed tool calls → C# application → Tekla Open API → validation → report.
The AI never generates and runs arbitrary code inside Tekla. It only calls controlled tools.

## 2. Environment (verified)
- Tekla Structures 2025.0 (build 2025.0.46985.0), installed at `C:\TeklaStructures\2025.0\bin`
- Project root (WSL): `/home/sheri/omnirush.ai/`
- `Directory.Build.props` (Tekla's official gacless file, lowercase name) must stay in the project root, unchanged. Without it the app cannot connect (no bindingRedirects → `Connected: False`).
- `TeklaVersion=2025.0` is set in the `.csproj`, not on the command line.
- Tekla must be open **with a model loaded**, same user session, same elevation level as our app.
- Test model: `AI_TRAINING_2025_PODS` — scratch use only.

## 3. Knowledge base — read before writing Tekla code
| Path | Use it for |
|---|---|
| `knowledge/Tekla_Skills/ALL_TEKLA_SKILLS.md` | Tool contract, safety tiers, Tekla MCP patterns, QA / drawing / report workflows |
| `knowledge/Tekla_Skills/tekla-mcp-server/references/patterns-for-csharp.md` | How to structure tools, risk levels, error handling in C# |
| `knowledge/docs/AI_TEKLA_2025_MVP_PLAN.md` | Architecture and phase plan |
| `knowledge/docs/base-skid-rules.md` | Pod / base-skid modeling rules |
| `knowledge/api/` | Tekla 2025 API XML docs and release notes — the authority on what exists |
| `API_NOTES.md` | Verified findings from this project. Add to it, never delete |
| `examples/` | Code that has been built AND tested against Tekla 2025 |

When `Install_2025.0_*.xml` and `NuGet_*.xml` disagree, the
`Install_2025.0_*.xml` documentation is authoritative because it matches the
installed Tekla Structures build `2025.0.46985.0`.

## 4. Hard rules
1. **Never invent Tekla API** classes, methods, properties or namespaces. Check `knowledge/api/` or a working example in `examples/`. If not found: stop, say so, and ask.
2. Target **Tekla 2025 only**. Do not assume 2023/2024/2026 code is compatible. Note: matrix multiplication order changed in 2025.
3. **Risk levels**: LOW = read/search/calculate. MEDIUM = create a few objects, change properties. HIGH = delete, move, bulk changes, macros. HIGH needs explicit user "yes" after showing what is selected and what will change. Enforce this in the executor code, not only in the UI.
4. Before any model change: re-check the selection, show the plan, then act, then `model.CommitChanges()`, then re-read to verify.
5. Save the current `TransformationPlane` before changing it and restore it in `finally`.
6. `ModelInfo.ModelName` returns the file name with extension (e.g. `AI_TRAINING_2025_PODS.db1`).
7. Do not store drawing view IDs between sessions.
8. Test objects are named with the prefix `MCP_TEST_`. Functional tests run only on the scratch model.
9. Do not copy code from GPL projects (e.g. teknovizier/tekla_mcp_server). Re-implement ideas.
10. No magic numbers or project rules in code — rules go in `config/*.json` or `knowledge/docs/*.md`.

## 5. How to work
BUILD → TEST against the open model → VALIDATE → DOCUMENT → only then continue.
- Make the smallest change that works. Do not rewrite working code.
- Never hide compile errors. Show the real error, then fix.
- Label every terminal command with where it runs: **PowerShell (Windows)** or **Ubuntu (WSL)**.

## 6. Learning loop (how this project gets smarter)
After every successful task:
1. Add the verified fact to `API_NOTES.md` (date, class/method, what it does, how it was tested).
2. If the code is reusable, copy it to `examples/<topic>/` with a short comment header: what it does, Tekla version, test model, date.
3. If something failed, write the failure and the fix in `API_NOTES.md` under "Traps".
