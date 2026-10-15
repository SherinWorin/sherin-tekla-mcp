# Contributing

Thanks for helping! Detailers, BIM modelers and developers are all welcome.

## Without writing code
- **Test on Tekla 2025** and send a [test report](https://github.com/SherinWorin/sherin-tekla-mcp/issues/new?template=tekla-test-report.yml). Always on a copy of a model.
- **Suggest a tool or a modeling rule** (boundary walls, base frames, platforms, your company standards) as an issue. Rules are JSON files in `config/rules`, so a clear description with numbers is enough.
- **Star the repo** so other Tekla users find it.

## With code
1. Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [CLAUDE.md](CLAUDE.md) (the rules also apply to humans).
2. Keep Tekla out of `SherinTeklaMcp.Core`; add unit tests with `MockTeklaService`.
3. Every model change must go through `WriteOperations` (preview -> confirm -> apply -> verify -> log). No tool may run AI-generated code.
4. New Tekla API calls: say where you verified them and add them to `docs/API_NOTES.md`.
5. `dotnet test tests/SherinTeklaMcp.Core.Tests` must pass; CI builds the Tekla 2025 version on Windows.
6. Update `docs/TOOLS.md` and `CHANGELOG.md`, then open a pull request.
