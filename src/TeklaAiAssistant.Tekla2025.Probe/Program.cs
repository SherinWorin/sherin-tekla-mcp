namespace TeklaAiAssistant.Tekla2025.Probe;

using System.Globalization;
using System.Windows.Forms;
using TeklaAiAssistant.Core.Commands;
using TeklaAiAssistant.Core.Extensions;

internal static class Program
{
    private const string ExpectedModelPath = @"C:\TeklaModels\LiveTest_Disposable";
    private const string CommandId = "model.read-selection";
    private const string CommandType = "TeklaAiAssistant.Commands.ReadSelectionCommand";

    [STAThread]
    private static int Main(string[] args)
    {
        HostLog? log = null;
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
                throw new InvalidOperationException("Windows LocalApplicationData is unavailable; cannot create a result log.");
            var stateDirectory = Path.Combine(localAppData, "TeklaAiAssistant");
            log = new HostLog(stateDirectory);
            Console.WriteLine("Result log: " + log.Path);
            if (args.Any(arg => arg != "--show-result" && arg != "--inspect-settings"))
                throw new ArgumentException("Only --show-result and --inspect-settings are supported.");
            if (args.Contains("--inspect-settings"))
            {
                using var settingsSession = new TeklaSessionManager(log.Info, ExpectedModelPath);
                settingsSession.ExecuteAsync(queued =>
                {
                    Console.WriteLine("Guarded model path: " + settingsSession.ModelPath);
                    var found = settingsSession.TryGetMacroDirectory(out var directory);
                    Console.WriteLine("XS_MACRO_DIRECTORY: " + (found ? directory : "<unavailable>"));
                    return found;
                }).GetAwaiter().GetResult();
                return 0;
            }

            // This fixed host-owned policy is outside the extension bundle. No runtime DLL
            // hash is ever promoted to trust: the deployer must review and approve it first.
            var allowlistPath = Path.Combine(stateDirectory, "trusted-commands.allowlist");
            var loader = new TrustedCommandLoader(ReadAllowlist(allowlistPath));
            var discovery = new ExtensionDiscoveryService().DiscoverWithErrors(
                new[] { Path.Combine(AppContext.BaseDirectory, "extensions") });
            foreach (var error in discovery.Errors)
                log.Error("Bundle discovery: " + error.Path + ": " + error.Message);
            if (discovery.Errors.Count != 0)
                throw new InvalidOperationException("Bundle discovery failed; see the result log.");

            var matches = discovery.Extensions.SelectMany(extension => extension.Bundles)
                .SelectMany(tab => tab.Children).SelectMany(panel => panel.Children)
                .Where(button => button.Command?.CommandId == CommandId
                    && button.Command.CommandType == CommandType).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected exactly one ReadSelection command bundle.");

            var bundle = matches[0];
            var loaded = loader.Load(bundle);
            log.Info(loaded.Message);
            if (!loaded.Success || loaded.Command is null)
                throw new InvalidOperationException(loaded.Message);

            using var session = new TeklaSessionManager(log.Info, ExpectedModelPath);
            var result = session.ExecuteAsync(queuedSession =>
            {
                // ExecuteAsync has already checked the disposable path on the queue;
                // each selection read checks it again before touching selection objects.
                log.Info("Guarded model path: " + session.ModelPath);
                var available = session.TryGetMacroDirectory(out var macroDirectory);
                log.Info("XS_MACRO_DIRECTORY: " + (available ? macroDirectory : "<unavailable>"));
                return loaded.Command.Execute(new HostCommandContext(queuedSession, log, bundle.Command!));
            }).GetAwaiter().GetResult();

            log.Info("Command success: " + result.Success);
            log.Info(result.Message);
            log.Info($"Model name={result.Selection.ModelName}; model path={result.Selection.ModelPath}; " +
                $"selected={result.Selection.SelectedObjectCount}; parts={result.Selection.ReportedPartCount}; " +
                $"skipped non-parts={result.Selection.SkippedNonPartCount}");
            foreach (var part in result.Objects)
            {
                log.Info($"Part id={part.Id}; guid={part.Guid ?? "<unavailable>"}; type={part.ObjectType}; " +
                    $"beamType={part.BeamType ?? "<n/a>"}; name={part.Name ?? "<unavailable>"}; " +
                    $"profile={part.Profile ?? "<unavailable>"}; material={part.Material ?? "<unavailable>"}; " +
                    $"class={part.Class ?? "<unavailable>"}; PART_POS={part.PartPosition ?? "<unavailable>"}; " +
                    $"ASSEMBLY_POS={part.AssemblyPosition ?? "<unavailable>"}; " +
                    $"assemblyId={part.AssemblyId?.ToString(CultureInfo.InvariantCulture) ?? "<unavailable>"}; " +
                    $"assemblyGuid={part.AssemblyGuid ?? "<unavailable>"}; " +
                    $"LENGTH assumed mm={part.LengthMm?.ToString("R", CultureInfo.InvariantCulture) ?? "<unavailable>"}; " +
                    $"WEIGHT kg unverified={part.WeightKg?.ToString("R", CultureInfo.InvariantCulture) ?? "<unavailable>"}");
                if (part.StartPoint is not null || part.EndPoint is not null)
                    log.Info($"Geometry id={part.Id}; StartPointMm={FormatPoint(part.StartPoint)}; " +
                        $"EndPointMm={FormatPoint(part.EndPoint)}");
                if (part.ContourPoints is not null)
                    log.Info($"Geometry id={part.Id}; ContourPointsMm=[{string.Join(", ", part.ContourPoints.Select(FormatPoint))}]");
            }
            Console.WriteLine(result.Message);
            if (args.Contains("--show-result"))
                MessageBox.Show(result.Message + Environment.NewLine + "Log: " + log.Path,
                    "Tekla read-only selection", MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return result.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            try { log?.Error("Host execution failed.", exception); }
            catch (Exception logException) { Console.Error.WriteLine("Cannot write result log: " + logException.Message); }
            Console.Error.WriteLine("Host execution failed: " + exception.Message);
            if (log is not null) Console.Error.WriteLine("Result log: " + log.Path);
            return 1;
        }
        finally
        {
            log?.Dispose();
        }
    }

    private static IEnumerable<TrustedCommandAllowlistEntry> ReadAllowlist(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException("Missing host-approved allowlist: " + path +
                ". Manually approve the reviewed command DLL SHA256 before deployment.");
        var entries = new List<TrustedCommandAllowlistEntry>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;
            var fields = line.Split('\t');
            if (fields.Length != 4)
                throw new InvalidOperationException("Allowlist lines require four tab-separated fields: assembly, SHA256, command type, command id.");
            entries.Add(new TrustedCommandAllowlistEntry(fields[0], fields[1], fields[2], fields[3]));
        }
        if (entries.Count == 0)
            throw new InvalidOperationException("The host-approved allowlist is empty.");
        return entries;
    }

    private static string FormatPoint(Point3DResult? point)
        => point is null
            ? "<unavailable>"
            : $"({point.Xmm.ToString("F1", CultureInfo.InvariantCulture)}, " +
              $"{point.Ymm.ToString("F1", CultureInfo.InvariantCulture)}, " +
              $"{point.Zmm.ToString("F1", CultureInfo.InvariantCulture)})";
}
