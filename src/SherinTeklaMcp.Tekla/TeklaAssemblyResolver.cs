using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SherinTeklaMcp.Tekla
{
    /// <summary>
    /// Loads the Tekla Open API DLLs from the INSTALLED Tekla Structures 2025.
    ///
    /// Must be registered at start-up BEFORE any Tekla type is used. Search order for the
    /// 'bin' folder: settings.json TeklaBinDir, the TEKLA_BIN_DIR environment variable, the
    /// folder of a running TeklaStructures.exe, then the standard install folders. Inside it,
    /// 'bin' and 'bin\Net48Runtime' are searched (newer Tekla versions split the DLLs).
    /// </summary>
    public static class TeklaAssemblyResolver
    {
        /// <summary>The Tekla major version this build is compiled for.</summary>
        public const int CompiledForMajor = 2025;

        private static readonly object Gate = new object();
        private static bool _registered;

        public static string BinDir { get; private set; } = "";
        public static string FoundVia { get; private set; } = "";
        public static string ModelDllFileVersion { get; private set; } = "";
        /// <summary>Why Tekla cannot be used, or empty when everything looks right.</summary>
        public static string Problem { get; private set; } = "";

        public static IReadOnlyList<string> ProbeDirectories
        {
            get
            {
                if (string.IsNullOrEmpty(BinDir)) return Array.Empty<string>();
                var dirs = new List<string> { BinDir };
                var net48 = Path.Combine(BinDir, "Net48Runtime");
                if (Directory.Exists(net48)) dirs.Add(net48);
                return dirs;
            }
        }

        public static void Register(string? configuredBinDir)
        {
            lock (Gate)
            {
                if (_registered) return;
                _registered = true;

                BinDir = Locate(configuredBinDir, out var via) ?? "";
                FoundVia = via;
                if (BinDir.Length == 0)
                {
                    Problem = "Tekla Structures 2025 'bin' folder not found. Start Tekla 2025, or set \"TeklaBinDir\" in settings.json " +
                              "(for example C:\\Program Files\\Tekla Structures\\2025.0\\bin).";
                }
                else
                {
                    var modelDll = FindInProbe("Tekla.Structures.Model.dll");
                    try { ModelDllFileVersion = modelDll == null ? "" : FileVersionInfo.GetVersionInfo(modelDll).FileVersion ?? ""; }
                    catch { ModelDllFileVersion = ""; }
                    var major = ParseMajor(ModelDllFileVersion);
                    if (major != 0 && major != CompiledForMajor)
                        Problem = $"This server is built for Tekla {CompiledForMajor}, but the Tekla found at {BinDir} is version {ModelDllFileVersion}. " +
                                  "Point \"TeklaBinDir\" in settings.json to the Tekla 2025 bin folder.";
                }

                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                Console.Error.WriteLine(Problem.Length == 0
                    ? $"[tekla] using Tekla DLLs from {string.Join(" + ", ProbeDirectories)} (found via {FoundVia}, Model.dll {ModelDllFileVersion})"
                    : "[tekla] " + Problem);
            }
        }

        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                var name = new AssemblyName(args.Name).Name ?? "";
                if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
                if (Problem.Length > 0 && name.StartsWith("Tekla.", StringComparison.OrdinalIgnoreCase)) return null;

                var already = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                if (already != null) return already;

                var path = FindInProbe(name + ".dll");
                return path == null ? null : Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                // An exception escaping this handler would be cached by the runtime - report and fail the bind.
                Console.Error.WriteLine($"[tekla] could not load '{args.Name}': {ex.Message}");
                return null;
            }
        }

        private static string? FindInProbe(string fileName)
        {
            foreach (var dir in ProbeDirectories)
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static string? Locate(string? configured, out string via)
        {
            if (IsTeklaBin(configured)) { via = "settings.json"; return Normalize(configured!); }

            var env = Environment.GetEnvironmentVariable("TEKLA_BIN_DIR");
            if (IsTeklaBin(env)) { via = "TEKLA_BIN_DIR"; return Normalize(env!); }

            try
            {
                foreach (var p in Process.GetProcessesByName("TeklaStructures"))
                {
                    string? dir = null;
                    try { dir = Path.GetDirectoryName(p.MainModule?.FileName ?? ""); } catch { /* access denied */ }
                    if (IsTeklaBin(dir) && ParseMajor(SafeFileVersion(Path.Combine(dir!, "Tekla.Structures.Model.dll"))) == CompiledForMajor)
                    {
                        via = "running TeklaStructures.exe";
                        return Normalize(dir!);
                    }
                }
            }
            catch { /* process listing is best-effort */ }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidates = new[]
            {
                Path.Combine(programFiles, "Tekla Structures", "2025.0", "bin"),
                @"C:\Program Files\Tekla Structures\2025.0\bin",
                @"C:\TeklaStructures\2025.0\bin",
            };
            foreach (var c in candidates)
            {
                if (IsTeklaBin(c)) { via = "standard install folder"; return Normalize(c); }
            }

            via = "(not found)";
            return null;
        }

        private static bool IsTeklaBin(string? dir) =>
            !string.IsNullOrWhiteSpace(dir) &&
            (File.Exists(Path.Combine(dir, "Tekla.Structures.Model.dll")) ||
             File.Exists(Path.Combine(dir, "Net48Runtime", "Tekla.Structures.Model.dll")));

        private static string Normalize(string dir) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir.Trim()));

        private static string SafeFileVersion(string path)
        {
            try { return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "" : ""; }
            catch { return ""; }
        }

        private static int ParseMajor(string version)
        {
            var dot = (version ?? "").IndexOf('.');
            var head = dot > 0 ? version!.Substring(0, dot) : version;
            return int.TryParse(head, out var major) ? major : 0;
        }
    }
}
