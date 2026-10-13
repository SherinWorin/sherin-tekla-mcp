using System.Security.Cryptography;
using System.Text;
using TeklaAiAssistant.Core.Commands;
using TeklaAiAssistant.Core.Extensions;

namespace TeklaAiAssistant.Core.Tests;

internal static class Program
{
    private static int Main()
    {
        var failures = new List<string>();
        Run("loader rejects changed allowlisted bytes", TestLoaderHashBoundary, failures);
        RunMalformedBundleTests(failures);
        Run("discovery resolves the host Core dependency", TestDiscoveryResolvesHostCoreDependency, failures);
        Run("discovery then trusted load in the same process succeeds", TestDiscoveryThenTrustedLoad, failures);
        Run("discovery loads nothing", TestDiscoveryLoadsNothing, failures);
        Run("full read result preserves model context and geometry boundaries", TestFullReadResult, failures);
        Run("read command reports the full selection result", TestReadCommandFullResult, failures);
        Run("missing UDA is represented explicitly", TestMissingUda, failures);
        Run("host invocation catches and logs command exceptions", TestHostInvocationBoundary, failures);

        Console.WriteLine("Core boundary tests: " + (failures.Count == 0 ? "PASS" : "FAIL"));
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void TestLoaderHashBoundary()
    {
        var source = typeof(TeklaAiAssistant.Commands.ReadSelectionCommand).Assembly.Location;
        var root = NewTempDirectory();
        var bundle = new BundleInfo(
            "model.read-selection", "Read", "Read", "TeklaAiAssistant.Commands",
            "TeklaAiAssistant.Commands.dll", "TeklaAiAssistant.Commands.ReadSelectionCommand", true);
        var bundleDirectory = Path.Combine(root, "bundle");
        Directory.CreateDirectory(bundleDirectory);
        var copy = Path.Combine(bundleDirectory, bundle.AssemblyFileName);
        File.Copy(source, copy);
        var hash = Sha256(copy);
        var loader = new TrustedCommandLoader(new[] {
            new TrustedCommandAllowlistEntry(bundle.AssemblySimpleName, hash, bundle.CommandType, bundle.CommandId)
        });

        var bytes = File.ReadAllBytes(copy);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(copy, bytes);
        var result = loader.Load(bundle, bundleDirectory);
        Assert(!result.Success, "tampered command unexpectedly loaded");
        Assert(result.Message.IndexOf("SHA256", StringComparison.OrdinalIgnoreCase) >= 0
            || result.Message.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0,
            "tampered command failure did not identify a SHA256/hash mismatch: " + result.Message);
    }

    private static void RunMalformedBundleTests(ICollection<string> failures)
    {
        var source = typeof(TeklaAiAssistant.Commands.ReadSelectionCommand).Assembly.Location;
        var fixtures = new Dictionary<string, (string Json, string Diagnostic)>
        {
            ["missing-field"] = (Json(readOnly: "true", omit: "commandId"), "commandId"),
            ["wrong-type"] = (Json(readOnly: "\"true\""), "readOnly"),
            ["unknown-command-type"] = (Json(commandType: "TeklaAiAssistant.Commands.DoesNotExist"), "command type"),
            ["invalid-json"] = ("{ definitely not json", "Invalid bundle.json")
        };

        foreach (var fixture in fixtures)
        {
            Run("malformed metadata: " + fixture.Key,
                () => TestMalformedBundle(source, fixture.Key, fixture.Value.Json, fixture.Value.Diagnostic),
                failures);
        }
    }

    private static void TestMalformedBundle(string source, string name, string json, string diagnostic)
    {
        var root = NewTempDirectory();
        var button = Path.Combine(root, name + ".extension", "Model.tab", "Panel.panel", "Button.pushbutton");
        Directory.CreateDirectory(button);
        File.WriteAllText(Path.Combine(button, "bundle.json"), json, new UTF8Encoding(false));
        // Reflection-only type validation is intentionally exercised for the unknown-type case.
        if (name == "unknown-command-type")
            File.Copy(source, Path.Combine(button, "TeklaAiAssistant.Commands.dll"));

        var result = new ExtensionDiscoveryService().DiscoverWithErrors(new[] { root });
        Assert(result.Errors.Count == 1, name + " produced " + result.Errors.Count + " discovery errors");
        Assert(!string.IsNullOrWhiteSpace(result.Errors[0].Message)
            && result.Errors[0].Message.IndexOf(diagnostic, StringComparison.OrdinalIgnoreCase) >= 0,
            name + " diagnostic was not specific: " + result.Errors[0].Message);
    }

    private static void TestDiscoveryResolvesHostCoreDependency()
    {
        var source = typeof(TeklaAiAssistant.Commands.ReadSelectionCommand).Assembly.Location;
        var root = NewTempDirectory();
        var button = Path.Combine(root, "ReadOnly.extension", "Model.tab", "Inspection.panel", "ReadSelection.pushbutton");
        Directory.CreateDirectory(button);
        File.WriteAllText(Path.Combine(button, "bundle.json"), Json(), new UTF8Encoding(false));
        File.Copy(source, Path.Combine(button, "TeklaAiAssistant.Commands.dll"));

        var result = new ExtensionDiscoveryService().DiscoverWithErrors(new[] { root });
        Assert(result.Errors.Count == 0, "discovery failed without Core.dll in the extension folder: "
            + string.Join(" | ", result.Errors.Select(error => error.Message)));
        Assert(!File.Exists(Path.Combine(button, "TeklaAiAssistant.Core.dll")),
            "the discovery fixture unexpectedly contains Core.dll");
    }

    private static void TestDiscoveryThenTrustedLoad()
    {
        var source = typeof(TeklaAiAssistant.Commands.ReadSelectionCommand).Assembly.Location;
        var root = NewTempDirectory();
        var button = Path.Combine(root, "ReadOnly.extension", "Model.tab", "Inspection.panel", "ReadSelection.pushbutton");
        Directory.CreateDirectory(button);
        File.WriteAllText(Path.Combine(button, "bundle.json"), Json(), new UTF8Encoding(false));
        var commandPath = Path.Combine(button, "TeklaAiAssistant.Commands.dll");
        File.Copy(source, commandPath);

        var discovery = new ExtensionDiscoveryService().DiscoverWithErrors(new[] { root });
        Assert(discovery.Errors.Count == 0, "discovery failed before trusted load: "
            + string.Join(" | ", discovery.Errors.Select(error => error.Message)));

        var loader = new TrustedCommandLoader(new[] {
            new TrustedCommandAllowlistEntry("TeklaAiAssistant.Commands", Sha256(commandPath),
                "TeklaAiAssistant.Commands.ReadSelectionCommand", "model.read-selection")
        });
        var result = loader.Load(new BundleInfo(
            "model.read-selection", "Read", "Read", "TeklaAiAssistant.Commands",
            "TeklaAiAssistant.Commands.dll", "TeklaAiAssistant.Commands.ReadSelectionCommand", true), button);
        Assert(result.Success, "trusted load failed after discovery: " + result.Message);
    }

    private static void TestDiscoveryLoadsNothing()
    {
        var source = typeof(TeklaAiAssistant.Commands.ReadSelectionCommand).Assembly.Location;
        var root = NewTempDirectory();
        var button = Path.Combine(root, "ReadOnly.extension", "Model.tab", "Inspection.panel", "ReadSelection.pushbutton");
        Directory.CreateDirectory(button);
        File.WriteAllText(Path.Combine(button, "bundle.json"), Json(), new UTF8Encoding(false));
        File.Copy(source, Path.Combine(button, "TeklaAiAssistant.Commands.dll"));

        var before = AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies().Length;
        var result = new ExtensionDiscoveryService().DiscoverWithErrors(new[] { root });
        var after = AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies().Length;
        Assert(result.Errors.Count == 0, "discovery failed: "
            + string.Join(" | ", result.Errors.Select(error => error.Message)));
        Assert(before == after, $"discovery changed reflection-only assembly count from {before} to {after}");
    }

    private static void TestHostInvocationBoundary()
    {
        var log = new TestLogger();
        var result = HostInvocation.Invoke(new ThrowingCommand(), new TestContext(log));
        Assert(!result.Success, "throwing command unexpectedly succeeded");
        Assert(log.Errors.Count == 1, "throwing command was not logged exactly once");
        Assert(log.Errors[0].IndexOf("command execution failed", StringComparison.OrdinalIgnoreCase) >= 0,
            "command failure log was not specific");
    }

    private static void TestFullReadResult()
    {
        var uda = new UdaValueResult("MISSING_UDA", "string", false, null);
        var result = new ReadSelectionResult(
            "LiveTest_Disposable.db1", "C:\\TeklaModels\\LiveTest_Disposable", 2, 1, 1,
            new[]
            {
                new SelectedObjectResult(
                    7, "guid-7", "Beam", "Beam 7", "UB406*140*46", "S235", "1",
                    "b0(?)", "B0(?)", 8, "guid-8", "BEAM",
                    new Point3DResult(1, 2, 3), new Point3DResult(4, 5, 6), null,
                    3000, null, new Dictionary<string, UdaValueResult> { [uda.Name] = uda }),
                new SelectedObjectResult(
                    9, "guid-9", "PolyBeam", "Poly", "PL10", "S235", "2",
                    null, null, null, null, null, null, null, null, null, null,
                    new Dictionary<string, UdaValueResult>())
            });

        Assert(result.ModelName == "LiveTest_Disposable.db1", "model name was not preserved as-is");
        Assert(result.ModelPath == "C:\\TeklaModels\\LiveTest_Disposable", "model path was not preserved");
        Assert(result.ReportedPartCount == 1 && result.SkippedNonPartCount == 1,
            "part/non-part counts were not preserved");
        Assert(result.Objects[0].BeamType == "BEAM" && result.Objects[1].BeamType == null,
            "BeamType boundary was not preserved");
        Assert(result.Objects[1].StartPoint == null && result.Objects[1].EndPoint == null
            && result.Objects[1].ContourPoints == null, "PolyBeam geometry was not null");
    }

    private static void TestReadCommandFullResult()
    {
        var session = new FakeSession(new ReadSelectionResult(
            "LiveTest_Disposable.db1", "C:\\TeklaModels\\LiveTest_Disposable", 1, 1, 0,
            new[] { new SelectedObjectResult(1, null, "Beam", null, null, null, null,
                null, null, null, null, "COLUMN", null, null, null, null, null,
                new Dictionary<string, UdaValueResult>()) }));
        var log = new TestLogger();
        var command = new TeklaAiAssistant.Commands.ReadSelectionCommand();
        var result = command.Execute(new TestContext(log, session));

        Assert(result.Success, "full read command failed");
        Assert(result.Selection.ModelName == "LiveTest_Disposable.db1",
            "full read command did not return model name");
        Assert(result.Selection.Objects.Count == 1
            && result.Selection.Objects[0].BeamType == "COLUMN",
            "full read command did not return the expanded object result");
    }

    private static void TestMissingUda()
    {
        var value = new UdaValueResult("NOT_PRESENT", "double", false, null);
        Assert(!value.Found && value.Value is null && value.ConfiguredType == "double",
            "missing UDA was not represented as found=false and null");
    }

    private static string Json(string readOnly = "true", string? omit = null,
        string? commandType = null)
    {
        var values = new Dictionary<string, string>
        {
            ["commandId"] = "model.read-selection", ["title"] = "Read",
            ["tooltip"] = "Read", ["assemblySimpleName"] = "TeklaAiAssistant.Commands",
            ["commandAssembly"] = "TeklaAiAssistant.Commands.dll",
            ["commandType"] = commandType ?? "TeklaAiAssistant.Commands.ReadSelectionCommand",
            ["readOnly"] = readOnly
        };
        return "{" + string.Join(",", values.Where(pair => pair.Key != omit)
            .Select(pair => "\"" + pair.Key + "\":" + (pair.Key == "readOnly" ? pair.Value : "\"" + pair.Value + "\""))) + "}";
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TeklaAiAssistant.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    private static void Run(string name, Action test, ICollection<string> failures)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures.Add("FAIL " + name + ": " + exception.Message); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ThrowingCommand : ITeklaCommand
    {
        public string Id => "test.throwing";
        public CommandResult Execute(ICommandContext context) => throw new InvalidOperationException("deliberate test failure");
    }

    private static class HostInvocation
    {
        public static CommandResult Invoke(ITeklaCommand command, ICommandContext context)
        {
            try { return command.Execute(context); }
            catch (Exception exception)
            {
                context.Log.Error("Host command execution failed.", exception);
                return CommandResult.Failed("Host command execution failed: " + exception.Message);
            }
        }
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Errors { get; } = new();
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) => Errors.Add(message + " " + exception?.Message);
    }

    private sealed class FakeSession : ITeklaSession
    {
        private readonly ReadSelectionResult result;

        public FakeSession(ReadSelectionResult result) => this.result = result;
        public bool IsConnected => true;
        public ReadSelectionResult ReadSelection() => result;
    }

    private sealed class TestContext : ICommandContext
    {
        public TestContext(ILogger log, ITeklaSession? session = null)
        {
            Log = log;
            Session = session ?? new FakeSession(new ReadSelectionResult(
                "", "", 0, 0, 0, Array.Empty<SelectedObjectResult>()));
        }
        public ITeklaSession Session { get; }
        public ILogger Log { get; }
        public BundleInfo Bundle => throw new NotSupportedException();
    }
}
