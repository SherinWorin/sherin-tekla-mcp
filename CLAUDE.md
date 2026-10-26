# Notes for AI assistants working on this repo

- Target: Tekla Structures **2025** Open API. Never invent Tekla classes or members. Before using a new API member, confirm it (official docs, compiled reference code) and add it to `docs/API_NOTES.md`.
- `SherinTeklaMcp.Core` must never reference Tekla. Put every rule, calculation and safety decision there and unit-test it with `MockTeklaService`.
- Every model-changing tool goes through `WriteOperations` (preview -> confirm token -> apply -> commit -> read back -> log). Never add a write path that skips it, and never add a tool that runs AI-generated code.
- Geometry in Tekla code: always inside `InGlobalPlane` (restores the user's work plane in `finally`).
- stdout is the MCP protocol: write logs to stderr only.
- After changes: `dotnet test tests/SherinTeklaMcp.Core.Tests`, update `docs/TOOLS.md` and `CHANGELOG.md`.
- Terminal instructions for the owner: label them **Command Prompt (Windows)** or **Ubuntu (WSL)**; PowerShell is blocked on the owner's PC.
- P142 drawing macros (`macros/wall-framing/`): start with `macros/wall-framing/HANDOVER.md` (state, owner rules, build/deploy, API traps, open items).
