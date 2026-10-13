# Tekla Structures 2025 macro launcher

`ReadSelectionLauncher.cs` uses the installed macro runtime's attributed
entrypoint. It launches the configured host with **only `--show-result`**;
all selection/model logic and result display belong to that host.

## Local configuration and user verification

Set `HostExecutablePath` in the source to the deployed executable's absolute
Windows path (for example, `@"C:\TeklaAiAssistant\TeklaAiAssistant.Host.exe"`,
if that is the actual deployed filename). Its default is empty, so an
unconfigured macro only displays a configuration warning. Drive-relative and
root-relative paths are rejected. No environment variables are read or set.

Confirm the active macro directory from Tekla's macro-directory setting before
installing the source yourself. No source or DLL was copied into an installation
directory during this work. Run the macro in Tekla Structures 2025 SP1 with
`LiveTest_Disposable` open and a known selection. Verify that the host displays
the expected selection result. Closing the launcher's `Process` handle does not
terminate the host; the launcher does not wait or report command success.

**Runtime execution is not proven until the user launches the macro.** The
checks below prove compilation/API compatibility, not Tekla macro discovery,
the host deployment, connection to the running model, or result-window behavior.

## Installed-source evidence

Paths below are Windows paths (inspected through `/mnt/c` in this workspace).

* `C:\TeklaStructures\2025.0\Environments\common\macros\modeling\OpenPreviousModel.cs`,
  lines 2–5 and 14–16: `#pragma reference "Tekla.Macros.Runtime"`, namespace
  `UserMacros`, class `Macro`, `[Tekla.Macros.Runtime.MacroEntryPointAttribute()]`,
  and `public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)`.
  `ReopenCurrentModelWithoutSaving.cs` in the same directory, lines 1–11,
  independently uses the same shape. These examples also reference
  `Tekla.Macros.Akit`; this launcher does not use Akit and needs only the runtime.
* `C:\TeklaStructures\2025.0\bin\Env\Common\macros\modeling\DSTVtoDXFConverter.cs`,
  lines 14–22: creates `System.Diagnostics.Process`, sets `StartInfo.FileName`,
  calls `Start()` and `WaitForExit()`.
  `MultiReportGenerator.cs` in that directory, lines 13–17, similarly calls
  `Process.Start()` and `Process.Close()` without waiting.
* `C:\TeklaStructures\2025.0\bin\Env\Common\macros\modeling\DSTVtoDXFConverterTemplate.cs`,
  lines 197–208: sets `StartInfo.FileName` and `StartInfo.Arguments` before
  calling `Start()`; evidence for passing host arguments from a macro.
* `C:\TeklaStructures\2025.0\Environments\common\macros\modeling\DirectoryBrowser.cs`,
  lines 21–29: also starts an external executable through `Process.Start()`.

The installed examples evidence external-process launching, but do not prove
that this launcher has run under SP1. The inspected `TeklaStructures.exe`
reports file/product version `2025.0.46985.0`; an SP1 designation was not
independently established from that version alone.

## Compilation validation performed

Windows .NET Framework compiler
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` compiled this source
successfully with `/target:library /warnaserror+`, referencing the installed
`C:\TeklaStructures\2025.0\bin\Tekla.Macros.Runtime.dll`, `System.dll`, and
`System.Windows.Forms.dll`. The output was scratch-only:
`/tmp/opencode/tekla-macro-validation/ReadSelectionLauncher.dll` (not in the repo).
No Tekla DLLs were copied.

Exact command executed from Windows PowerShell:

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library /warnaserror+ /out:"\\wsl.localhost\Ubuntu\tmp\opencode\tekla-macro-validation\ReadSelectionLauncher.dll" /reference:"C:\TeklaStructures\2025.0\bin\Tekla.Macros.Runtime.dll" /reference:System.dll /reference:System.Windows.Forms.dll "\\wsl.localhost\Ubuntu\home\sheri\omnirush.ai\src\TeklaAiAssistant.Tekla2025.Macro\ReadSelectionLauncher.cs"
```

Reflection against that installed DLL reports assembly
`Tekla.Macros.Runtime, Version=7.1.0.0, Culture=neutral, PublicKeyToken=a70cba4ef557ee03`
and confirms the `MacroEntryPointAttribute` and `IMacroRuntime` types.
Reflection over the compiled launcher confirms
`Void Run(Tekla.Macros.Runtime.IMacroRuntime)` and its entrypoint attribute.
The scratch launcher was loaded from bytes for reflection because PowerShell's
`Assembly.LoadFrom` rejects the WSL UNC location; its `Run` method was not invoked.
The ordinary compiler ignores Tekla's `#pragma reference` (warning 1633 is
suppressed as in the installed examples); explicit compiler references validate
the APIs, while Tekla's own compilation/discovery remains a user runtime check.
