# Testing

## 1. Unit tests (no Tekla)

48 tests in `tests/SherinTeklaMcp.Core.Tests`: geometry, filters, argument parsing, quantity report + CSV, panel division and wall planning, validation, confirm tokens, safety policy, settings, and the full write pipeline against the mock backend (preview changes nothing, apply needs the token, changed arguments are refused, read-only and delete rules, catalog check, read-back verification, operation log).

Visual Studio: **Test -> Run All Tests**. Or:

```bat
:: Command Prompt (Windows)
dotnet test tests\SherinTeklaMcp.Core.Tests -c Release
```

## 2. MCP smoke test (no Tekla)

`scripts/smoke_test.py` starts the server with the mock backend and talks to it like Claude Desktop: initialize, tools/list (16 tools), reads, a report, a wall plan, and a write that must be refused in read-only mode.

```bat
:: Command Prompt (Windows), after building
python scripts\smoke_test.py src\SherinTeklaMcp.Server\bin\Release\net48\SherinTeklaMcp.Server.exe
```

## 3. CI (GitHub Actions)

Every push runs: unit tests + smoke test on Linux (net8.0 mock), and the full Windows build against the Tekla 2025 NuGet packages (net48) + unit tests + smoke test with the net48 exe. The Windows build is uploaded as an artifact.

## 4. Live test in Tekla 2025 (manual - do this on a COPY of a model)

Use the disposable test model `C:\TeklaModels\LiveTest_Disposable`, never a project model.

Record results in [API_NOTES.md](API_NOTES.md) (Live 2025 column) and [../CHANGELOG.md](../CHANGELOG.md).

| # | Step | Expected |
|---|---|---|
| 1 | `ReadOnly: true`. "Check the Tekla connection." | connected, your model name, bin folder, Model.dll 2025.x |
| 2 | Select 3 members. "What is selected?" | 3 objects, correct profiles; start/end in global coordinates even if a local work plane is active |
| 3 | Set a local work plane in Tekla, repeat 2 | same coordinates; your work plane is unchanged afterwards |
| 4 | "Find all IPE200" / "How many columns?" | counts match Tekla's own filter |
| 5 | "Quantity report by profile, CSV" | lengths/weights match a Tekla report; CSV opens in Excel |
| 6 | "Validate the selection" with a duplicate beam | duplicate reported, highlight works |
| 7 | "Create a beam ..." in read-only | refused with the READ-ONLY message |
| 8 | `ReadOnly: false`, restart. "Create an IPE200 S355JR beam from 0,0,3000 to 6000,0,3000" | preview first; created only after you agree; "Verified" in validation; Ctrl+Z removes it |
| 9 | "Create a beam with profile IPE2000" | refused: not in catalog |
| 10 | "Change the selected members to S355JR" | preview lists old -> new; applied; values correct in Tekla |
| 11 | "Delete the beam" with `AllowDelete: false` | refused |
| 12 | Check `%LOCALAPPDATA%\SherinTeklaMcp\logs` | one JSON line per preview / apply / refusal |
