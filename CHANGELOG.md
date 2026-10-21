# Changelog

## Unreleased

### Added

- `macros/wall-framing/` - a **wall-framing layout auto-dimensioner** for Tekla Structures 2025: a drawing macro that reproduces the annotations of a `WALL FRAMING LAYOUT` GA drawing (stud chains, band chains, top outline, overall dimensions, north run, wall-panel marks, section marks, notes).
  - The drawing is found by its **title**, not by a fixed mark, so the same file works on every computer of a project.
  - An **issued or frozen drawing is never written to** - the macro refuses it and says so.
  - The wall panels are found by their prefix, or **automatically** when the prefix matches nothing.
  - The rules live in their own assembly (`WF_Rules.cs`) with **no Tekla reference**; the macro and the rules are merged into one `.cs` file for the Tekla macro catalog.
  - A deployment package (`package/WallFramingAutoDimension.zip`) for installing on another computer, with `INSTALL_NOTES.txt` and an optional installer script.
  - Test harnesses in `macros/wall-framing/tests/`; the reference dumps of the project model are **not** committed (client data) - see `macros/wall-framing/tests/reference/README.md`.

## 0.1.0 - 2026-10-15

First version.

- MCP server (stdio) for Tekla Structures 2025: .NET Framework 4.8 x64 build for Tekla, .NET 8 build with a mock model for CI and demos.
- 16 tools: connection, read selected/by GUID/by filter, select, highlight, quantity report (BOQ + CSV), validation, distance, rules list, wall plan, panel division, create beams, create columns, modify, delete.
- Safety: read-only by default, delete disabled by default, LOW/MEDIUM/HIGH risk, preview + confirm token bound to the exact arguments, catalog check, read-back verification, JSON-lines operation log.
- Rules engine with `config/rules/boundary-wall.json`.
- Tekla DLLs loaded from the installed Tekla 2025 at run time.
- 48 unit tests, MCP smoke test, GitHub Actions CI.
- Not yet tested against a live Tekla 2025 model.
