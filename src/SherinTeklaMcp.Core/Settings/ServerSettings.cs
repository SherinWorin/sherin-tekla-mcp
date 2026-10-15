using System;
using System.IO;
using System.Text.Json;

namespace SherinTeklaMcp.Core.Settings
{
    /// <summary>
    /// Server settings, read once at start-up from <c>settings.json</c>. Restart the MCP
    /// client after editing it. Safe defaults: read-only, delete disabled.
    /// </summary>
    public sealed class ServerSettings
    {
        /// <summary>When true every model-changing tool is refused (enforced in code, not only hidden).</summary>
        public bool ReadOnly { get; set; } = true;

        /// <summary>Delete is disabled unless this is true AND the call is confirmed.</summary>
        public bool AllowDelete { get; set; }

        /// <summary>A modify/create affecting more objects than this is HIGH risk.</summary>
        public int HighRiskObjectCount { get; set; } = 20;

        /// <summary>Hard cap on objects in one write call.</summary>
        public int MaxObjectsPerWrite { get; set; } = 200;

        /// <summary>Hard cap on objects returned by one read call.</summary>
        public int MaxObjectsPerRead { get; set; } = 1000;

        /// <summary>Hard cap on objects scanned by one search.</summary>
        public int MaxObjectsToScan { get; set; } = 50000;

        /// <summary>Minutes a preview's confirm token stays valid.</summary>
        public int ConfirmTokenMinutes { get; set; } = 10;

        /// <summary>Parts shorter than this are reported by validation (mm).</summary>
        public double MinMemberLengthMm { get; set; } = 10;

        /// <summary>Tekla 2025 'bin' folder. Empty = auto-detect.</summary>
        public string TeklaBinDir { get; set; } = "";

        /// <summary>Operation log folder. Empty = %LOCALAPPDATA%\SherinTeklaMcp\logs.</summary>
        public string LogDirectory { get; set; } = "";

        /// <summary>CSV export folder. Empty = %LOCALAPPDATA%\SherinTeklaMcp\exports.</summary>
        public string ExportDirectory { get; set; } = "";

        /// <summary>Folder with rule files (*.json). Empty = 'config\rules' next to the server.</summary>
        public string RulesDirectory { get; set; } = "";

        /// <summary>Where these settings were loaded from (not serialized from the file).</summary>
        public string LoadedFrom { get; set; } = "(defaults)";

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

        /// <summary>
        /// Loads settings from <paramref name="path"/>. A missing file gives the safe defaults;
        /// an invalid file throws, because silently ignoring a typo in a safety setting is worse.
        /// </summary>
        public static ServerSettings Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new ServerSettings { LoadedFrom = "(defaults - no settings file found)" };

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<ServerSettings>(json, JsonOptions)
                           ?? throw new InvalidDataException($"Settings file is empty: {path}");
            settings.LoadedFrom = path!;
            settings.Normalize();
            return settings;
        }

        /// <summary>Clamps values so a bad number cannot switch a safety limit off.</summary>
        public void Normalize()
        {
            if (HighRiskObjectCount < 1) HighRiskObjectCount = 1;
            if (MaxObjectsPerWrite < 1) MaxObjectsPerWrite = 1;
            if (MaxObjectsPerWrite > 1000) MaxObjectsPerWrite = 1000;
            if (MaxObjectsPerRead < 1) MaxObjectsPerRead = 1;
            if (MaxObjectsToScan < 1) MaxObjectsToScan = 1;
            if (ConfirmTokenMinutes < 1) ConfirmTokenMinutes = 1;
            if (ConfirmTokenMinutes > 60) ConfirmTokenMinutes = 60;
            if (MinMemberLengthMm < 0) MinMemberLengthMm = 0;
        }

        public string ResolveLogDirectory() => Resolve(LogDirectory, "logs");
        public string ResolveExportDirectory() => Resolve(ExportDirectory, "exports");

        private static string Resolve(string configured, string leaf)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured);
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            return Path.Combine(root, "SherinTeklaMcp", leaf);
        }
    }
}
