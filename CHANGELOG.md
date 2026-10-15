# Changelog

## 0.1.0 - 2026-10-15

First version.

- MCP server (stdio) for Tekla Structures 2025: .NET Framework 4.8 x64 build for Tekla, .NET 8 build with a mock model for CI and demos.
- 16 tools: connection, read selected/by GUID/by filter, select, highlight, quantity report (BOQ + CSV), validation, distance, rules list, wall plan, panel division, create beams, create columns, modify, delete.
- Safety: read-only by default, delete disabled by default, LOW/MEDIUM/HIGH risk, preview + confirm token bound to the exact arguments, catalog check, read-back verification, JSON-lines operation log.
- Rules engine with `config/rules/boundary-wall.json`.
- Tekla DLLs loaded from the installed Tekla 2025 at run time.
- 48 unit tests, MCP smoke test, GitHub Actions CI.
- Not yet tested against a live Tekla 2025 model.
