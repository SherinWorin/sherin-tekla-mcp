# Extension bundles

Extensions use a folder-based bundle contract:

```text
Name.extension/
  extension.json
  Model.tab/
    Inspection.panel/
      ReadSelection.pushbutton/
        bundle.json
        TeklaAiAssistant.Commands.dll
```

Discovery supports tabs, panels, and pushbuttons. Each pushbutton's `bundle.json`
requires `commandId`, `title`, `tooltip`, `assemblySimpleName`, `commandAssembly`,
`commandType`, and a boolean `readOnly`. Malformed bundles are skipped with
diagnostics from `DiscoverWithErrors`. No YAML metadata is read.

The sample requires `TeklaAiAssistant.Commands.dll` alongside its metadata.
The host must explicitly allowlist the built DLL's SHA256, assembly simple name,
type, and command id before calling `TrustedCommandLoader`. There is no wildcard
trust, bundled allowlist, or placeholder hash. Only read-only commands are
supported in the MVP. These trusted DLLs and their dependencies run in-process;
the loader is not a security sandbox. `readOnly` is a capability declaration,
not enforcement: an approved assembly can create its own Tekla `Model` and
call mutating APIs. The host-owned SHA-256 allowlist and code review are the
trust boundary. See
`src/TeklaAiAssistant.Core/Commands/README.md` for the integration contract.
