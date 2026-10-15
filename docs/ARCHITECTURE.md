# Architecture

```
 Claude Desktop / any MCP client
        |  JSON-RPC over stdio (stdout = protocol only, all logs on stderr)
        v
 SherinTeklaMcp.Server  (net48 exe for Tekla, net8.0 exe for mock/CI)
   Tools/ModelReadTools      LOW    connection, read, find, select, highlight
   Tools/ReportTools         LOW    quantity report (BOQ, CSV), validation
   Tools/GeometryRuleTools   LOW    distance, rules, wall plan, panel division
   Tools/ModelWriteTools     MED/HIGH  create beams/columns, modify, delete
        |                           |
        |                           v
        |               Core/Operations/WriteOperations   <- the safe write pipeline
        |                 input check -> SafetyPolicy -> catalog check -> risk
        |                 -> PREVIEW (+ ConfirmationService token)
        |                 -> APPLY (token verified) -> Tekla write -> commit
        |                 -> read-back verification -> OperationLog
        v                           |
 Core/ITeklaService  <--------------+
   |-- SherinTeklaMcp.Tekla/TeklaService   (live Tekla Structures 2025, Open API)
   '-- Core/Mock/MockTeklaService           (in-memory sample model)
```

## Projects

| Project | Target | Depends on | Purpose |
|---|---|---|---|
| `SherinTeklaMcp.Core` | netstandard2.0 | System.Text.Json | Everything that does not need Tekla. Unit-tested. |
| `SherinTeklaMcp.Tekla` | net48 x64 | Core, Tekla 2025 NuGet (compile only) | `TeklaService`, `TeklaAssemblyResolver`. |
| `SherinTeklaMcp.Server` | net48 x64 + net8.0 | Core (+ Tekla on net48), MCP SDK, Hosting | Host, DI, tool definitions. |
| `SherinTeklaMcp.Core.Tests` | net8.0 | Core, xUnit | 48 unit tests. |

## Key decisions

1. **Tools, not code.** The AI only calls typed tools. There is deliberately no "run C#" tool (unlike some other Tekla MCP servers) - project rule 7.
2. **Safety lives in Core, not in the tool list.** Read-only mode, delete permission, limits, risk levels and confirm tokens are checked inside `WriteOperations` for every call, so hiding or mislabelling a tool can never bypass them. The same pipeline runs for the mock and for Tekla, so it is tested without Tekla.
3. **Confirm tokens bind the arguments.** HMAC-SHA256 over operation + canonical JSON of the normalized arguments + expiry, with a random per-process key. The AI cannot apply what it did not preview, cannot change arguments after the user saw the plan, and old tokens die with the process.
4. **`ITeklaService` is low-level** (insert one beam, modify one object, commit). Business decisions stay out of the Tekla layer, which keeps it small and replaceable.
5. **Global coordinates everywhere.** `TeklaService.InGlobalPlane` sets the global work plane for every geometry read/write and restores the user's plane in `finally`.
6. **Installed Tekla DLLs at run time.** See [API_NOTES.md](API_NOTES.md#loading-the-tekla-dlls).
7. **Rules as data.** `config/rules/*.json` -> `LinearDivisionRule`. Adding a project standard = adding a JSON file.
8. **Reporting is independent of the AI.** `QuantityReport` works on plain `TeklaObjectInfo` rows.
9. **Errors are results, not crashes.** Every tool catches exceptions and returns `status: "error"` with a readable message; technical details go to stderr.

## Data flow of a write

1. AI calls `tekla_modify_objects(guids, material="S355JR", apply=false, intent="...")`.
2. `WriteOperations.ModifyObjects`: normalize -> `SafetyPolicy.CheckAllowed` (read-only? limits?) -> read current objects (unknown GUID = refuse) -> catalog check -> planned changes (old -> new) -> canonical JSON -> token. **Logged as `preview`.** Nothing written.
3. AI shows the plan; the user agrees.
4. AI calls again with the same arguments + `apply=true` + `confirmToken`.
5. Token verified -> `ITeklaService.ModifyObject` per object -> `CommitChanges` -> read back, compare every field -> **logged as `applied` / `partial` / `failed`** with old and new values.

## Extending

- **New read tool:** add a method to `ITeklaService` (+ mock + Tekla implementation), then a `[McpServerTool]` method in a tool class. Keep it LOW risk.
- **New write tool:** add the low-level call to `ITeklaService`, then a method in `WriteOperations` that reuses `Refuse` / `Preview` / `Finish`, then the tool. Add unit tests with the mock.
- **New rule type:** a new rule class + engine in `Core/Rules`, a JSON file, a calculation tool.
