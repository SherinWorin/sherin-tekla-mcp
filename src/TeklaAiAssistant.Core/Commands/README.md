# Read-only command integration (net48)

Implement `ITeklaCommand.Id` and `Execute(ICommandContext)`. The context supplies
`Session`, `Log`, and the validated `BundleInfo`. `ILogger` has `Info(string)` and
`Error(string, Exception? = null)`. `ITeklaSession` has only `IsConnected` and
`GetSelectedParts()`; return plain `SelectedPartInfo` records, never vendor
objects. It intentionally exposes no mutation or commit operations and no raw
Tekla objects.
The record fields are `Id`, `Profile`, `Material`, nullable `LengthMm`,
`PartPosition`, and `AssemblyPosition`. `PartPosition` and `AssemblyPosition`
are currently returned as `<unavailable>`; they must not be presented as current
Tekla marks. Return `CommandResult(Success, Message, Parts)`; a clear selection
is a successful result with an empty `Parts` list. `CommandResult.Failed(message)`
supplies an empty part list.

`BundleInfo` fields, in constructor order: `CommandId`, `Title`, `Tooltip`,
`AssemblySimpleName`, `AssemblyFileName`, `CommandType`, `ReadOnly`.

Call `ExtensionDiscoveryService.DiscoverWithErrors(roots)` and display/log its
`Errors` (each has `Path` and `Message`). Invalid executable bundles are skipped;
valid siblings remain available. `Discover(roots)` retains the previous list-only
API. `BundleDefinition.Command` contains the validated `BundleInfo`; existing
`CommandAssembly`, `CommandType`, `Metadata`, and `Children` remain available.
Only `bundle.json` is read. Group folders can omit it; pushbuttons require all
seven metadata fields shown in the sample. DLLs are local filenames, not paths.

Construct `TrustedCommandLoader` with host-owned `TrustedCommandAllowlistEntry`
records: assembly simple name, actual compiled DLL SHA256 (64 hex characters),
fully-qualified command type, command id. No trust entries are accepted from
bundle metadata, and no wildcard or missing-hash fallback exists. Call
`Load(bundleDefinition)` or `Load(bundleInfo, bundleDirectory)`. Check `Success`
and `Message`, then execute `Command` with the host context in the host's normal
exception boundary. The loader checks approved bytes, assembly name, and a public
parameterless `ITeklaCommand` type before execution loading; after constructing the
approved type it checks the instance's declared id. `readOnly=false` is rejected.

Sample identity: `TeklaAiAssistant.Commands.dll`, simple name
`TeklaAiAssistant.Commands`, type
`TeklaAiAssistant.Commands.ReadSelectionCommand`, id `model.read-selection`.
Deploy the built DLL alongside its `bundle.json`. Pin its real build hash in the
host's explicit allowlist; rebuilds require updating that trusted hash.

This is **not a security sandbox**. Trusted constructors, commands and dependencies
run in-process with host privileges; any loaded assembly can instantiate its own
Tekla `Model` and call mutating APIs. `readOnly` is a capability declaration,
not enforcement against arbitrary DLL behavior. The actual trust boundary is
the host-owned SHA-256 allowlist plus code review of the assembly and its
dependencies. Dependencies must be trusted/deployed by the host as well. Keep
deployment folders controlled. The file lock during inspection/load
uses Windows .NET Framework sharing semantics. The SDK net48 project needs the
framework `System.Runtime.Serialization` reference for JSON serialization (no
NuGet JSON package). Reflection-only validation requires the .NET Framework runtime.
