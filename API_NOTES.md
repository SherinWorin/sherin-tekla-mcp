# Tekla Structures 2025 API verification notes

Source of truth for this file: the exact `2025.0.0` Trimble NuGet packages
downloaded from `api.nuget.org`, using the XML documentation shipped beside
each assembly. No 2026 PDF reference was used.

## Package evidence

Verified from package contents on 2026-10-04:

- `Tekla.Structures` `2025.0.0`: `lib/net48` and `lib/netstandard2.0`, with XML documentation.
- `Tekla.Structures.Model` `2025.0.0`: `lib/net48` and `lib/netstandard2.0`, with XML documentation.
- `Tekla.Structures.Drawing` `2025.0.0`: `lib/net48` and `lib/netstandard2.0`, with XML documentation.
- `TSAppConfigPatcherTask` `2.1.3` is available from NuGet; it has not yet been restored or executed in this workspace.

The model package declares dependencies on the matching `Tekla.Structures`
and `Tekla.Structures.Datatype` packages, plus Trimble runtime dependencies.
The repository must use PackageReferences and must not contain copied DLLs.

## XML-documented members confirmed in `Tekla.Structures.Model.xml`

These are package-surface confirmations only; they are not a live connection
test and do not prove behavior against the installed Tekla build:

- `Tekla.Structures.Model.Model` constructor
- `Model.GetConnectionStatus`
- `Model.GetInfo`
- `Model.GetModelObjectSelector`
- `Model.SelectModelObject(Identifier)`
- `Tekla.Structures.Model.UI.ModelObjectSelector.GetSelectedObjects`
- `Model.CommitChanges`
- `Model.GetReportPropertyStr`, `GetReportPropertyInt`, and `GetReportPropertyDouble`
- `ModelObject.Insert`, `Select`, `Modify`, and `Delete`
- `ModelObject.GetReportProperty` overloads
- `ModelObject.GetAllReportProperties`
- `ModelObject.GetCoordinateSystem`
- `ModelObjectEnumerator.AutoFetch` (static property; applies to all enumerators)

The user-provided members `Part.GetSolid`, solid bounds, advanced options,
dimension creation, view coordinate systems, and view enumeration still need
their exact 2025 XML/member signatures recorded before implementation.

## Environment checks

- NuGet v3 endpoint: reachable from WSL.
- The default Program Files path is not present; this install runs from
  `C:\TeklaStructures\2025.0\bin\` (confirmed from the live Windows process).
- Windows `dotnet.exe` is available at `C:\Program Files\dotnet\dotnet.exe`.
- Help > About version supplied by the user: **2025 Service Pack 1 (Build 46985)**.
  Build timestamp: `2025-04-09 11:20:47Z`. This supersedes the earlier
  assumption that the installed application was SP3 or later. No SP3+
  compatibility has been demonstrated; future live results must be labeled SP1.

## First live probe result

The probe restored and built successfully with Windows .NET, then ran as a
Windows executable while `C:\TeklaStructures\2025.0\bin\TeklaStructures.exe`
was running. `Model.GetConnectionStatus()` returned `false`; the API reported
that the `Tekla.Structures.Model-TeklaStructures-Console:2025.0.0.0` remoting
 service did not exist. This is a connection-state result, not evidence of an
 API mismatch. The next run must be made with an actual Tekla model open and
 the Tekla process ready for external Open API connections.

## Connection diagnosis after model-open retry

The follow-up retry still returned `Connected: False`. The ordered checks
produced these results:

1. **Elevation:** both Tekla and the current Windows shell were not elevated.
   Both were running as `SHERINWORIN\\sheri` in session `4`; no elevation
   mismatch was found.
2. **Version:** like-for-like inspection shows the installed and NuGet
   assemblies both have `AssemblyVersion 2025.0.0.0`. Their file versions are
   respectively `2025.0.46985.0` and `2025.0.0.0`; this is a build/file-version
   difference, not an assembly-version mismatch. The package pin remains
   `2025.0.0`.
3. **Binding redirects:** the generated probe config exists, but contains only
   redirects for `System.Buffers` and `System.Runtime.CompilerServices.Unsafe`;
   it contains no Tekla redirects. Gacless binding setup is therefore not yet
   verified.
4. **Session:** Tekla and the current shell are in the same Windows session
   and user context.

No source code was changed in response to this diagnosis. The package pin
remains unchanged.

## Required next action

The official Tekla `Directory.Build.Props` archive must be downloaded from
Tekla's connection guide and placed in this project's folder or a parent
folder. It must not be recreated here. After that file is present, clean and
rebuild the probe, inspect the generated config for Tekla `bindingRedirect` and
`codeBase` entries, and rerun with an actual model open (not the start screen).

## Gacless setup and successful connection

- Download found: `Directory.Build_.Props_.zip`, 332 bytes, modified
  2026-10-04 11:56. It was the only matching download.
- ZIP contents: exactly one file, `Directory.Build.Props`.
- Copied unchanged to the repository as `Directory.Build.props`.
- The official file content sets `TeklaVersion` to `2024.0` when the Debug
  configuration is already present. The build was therefore run with the
  verified installed version override `-p:Configuration=Debug
  -p:TeklaVersion=2025.0`; the source props file was not modified.
- MSBuild preprocess confirmed the repository `Directory.Build.props` import.
- Clean/build succeeded with Windows .NET.
- The generated probe config now contains Tekla `bindingRedirect` and
  `codeBase` entries pointing to `C:\TeklaStructures\2025.0\bin`.

The rebuilt Windows probe then connected successfully:

```text
Connected: True
Model path: C:\Users\sheri\OneDrive\Desktop\Tekla Model\AI_TRAINING_2025_PODS
Model name: AI_TRAINING_2025_PODS.db1
Selected count: 0
```

The connection MVP is now proven. `ModelInfo.ModelName` is verified to return
the model filename including the `.db1` extension. The model is stored under
OneDrive Desktop; use a local test copy such as `C:\TeklaModels\` before
relying on repeated write or validation tests, because sync can lock or
conflict with Tekla model files.

`TeklaVersion=2025.0` is now in the probe project file, not only on the
command line and not in Tekla's official `Directory.Build.props`, so Visual
Studio and later builds receive it automatically.

Verified with a plain build (no `-p:TeklaVersion` override): MSBuild logged
`TeklaVersion = 2025.0`, patched the app config, and produced the Tekla
`codeBase` entries successfully. The rebuilt probe still connected to the
open model and reported `Selected count: 0`.

No object data was read because the current Tekla selection was empty. The
next read-only test is to select parts (not the assembly) in Tekla and rerun
the same probe. The expected base-frame test, if present in the model, is 25
parts and 157.0 kg; if `BP3-BF1` is absent, use the model's verified
`BP4-BF.1` parts and record that distinction.

## Disposable live-test model verification

### Macro launcher preparation

The guarded settings-only host run on 2026-10-04 returned the active
`XS_MACRO_DIRECTORY` roots from Tekla SP1:

- `C:\TeklaStructures\2025.0\environments\common\macros\`
- `C:\TeklaStructures\2025.0\environments\middle_east\General\user-macros\`
- `C:\TeklaStructures\2025.0\bin\Env\Common\Macros\`

The model-path guard confirmed `C:\TeklaModels\LiveTest_Disposable`.
Proposed installation destination is the Middle East user-macros root's
`modeling` subdirectory; awaiting user confirmation before copying.
Installed stock macros provide evidence for `Process.Start`, and the launcher
entrypoint compiles against installed `Tekla.Macros.Runtime.dll`. Actual
external-process execution from the SP1 macro runtime remains untested until
installation is approved and the macro is run. No runtime restriction has
been observed, and no workaround has been introduced.

On 2026-10-04, the Windows probe confirmed the requested disposable model was
connected before live testing:

```text
Connected: True
Model path: C:\Users\sheri\OneDrive\Desktop\Tekla Model\LiveTest_Disposable
Model name: LiveTest_Disposable.db1
Selected count: 0
```

The path safeguard passed. No timing or close/switch test has been claimed yet.

The disposable model was subsequently moved out of OneDrive and reopened.
The connection was reverified on 2026-10-04:

```text
Connected: True
Model path: C:\TeklaModels\LiveTest_Disposable
Model name: LiveTest_Disposable.db1
Selected count: 0
```

The local-path safeguard passed. Timing tests have not started because no
selection is active yet.

## Next verification run on Windows

1. Open Tekla Structures 2025 SP3 or later and record Help > About.
2. Restore/build `TeklaAiAssistant.Tekla2025.Probe` with Visual Studio or `dotnet.exe`.
3. Confirm the installed bin assemblies match the restored package surface.
4. Add only the smallest read-only connection probe: connection status, model path,
   current selection, and selected-object report properties.
5. Run it against a disposable model and capture results without committing model data.

## End-to-end probe milestone — 2026-10-14

- Probe verified end to end from `D:\ONMIRUSH\Tekla Plugins\src` against
  `C:\TeklaModels\LiveTest_Disposable`: `Connected True`, 5 parts read, exit code 0.
- The trusted-command allowlist uses one line per entry with four tab-separated
  fields: assembly simple name, SHA256, command type, and command ID. Blank lines
  and lines beginning with `#` are allowed. The file is at
  `%LOCALAPPDATA%\TeklaAiAssistant\trusted-commands.allowlist`.
- Never `ReflectionOnlyLoad` the same assembly twice in one AppDomain; discovery
  uses `PEReader`/`MetadataReader` for metadata-only type checks.
- Probe runs only against `C:\TeklaModels\LiveTest_Disposable` (disposable-model guard).
- `PART_POS`/`ASSEMBLY_POS` show `(?)` when numbering is out of date. Not an API error.
- `LENGTH` unit: verified as mm against point-to-point distance on the five selected beams.

## Metadata dependency pin verification — 2026-10-14

- `System.Reflection.Metadata.dll` and `System.Collections.Immutable.dll` in the
  probe `bin\Debug\net48` are pinned to file version `7.0.22.51805`, matching
  `C:\TeklaStructures\2025.0\bin` byte-for-byte by SHA256.
- The already-built probe then ran successfully against
  `C:\TeklaModels\LiveTest_Disposable`: exit code 0, 5 selected parts read,
  with the same five part IDs, profiles, materials, lengths, and numbering
  outputs as the previous verified run.
- AutoFetch is a static, process-wide setting; set to true in TeklaSessionManager.
- Verified 2026-10-14: LENGTH = mm (matches point-to-point distance on 5 beams), WEIGHT = kg (46.32 kg/m on UB406*140*46).
- Verified: points are global even with a non-global user work plane; plane restored after read.
