using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SherinTeklaMcp.Core;
using SherinTeklaMcp.Core.Logging;
using SherinTeklaMcp.Core.Mock;
using SherinTeklaMcp.Core.Operations;
using SherinTeklaMcp.Core.Rules;
using SherinTeklaMcp.Core.Safety;
using SherinTeklaMcp.Core.Settings;
using SherinTeklaMcp.Server;

// -----------------------------------------------------------------------------------------
// Sherin Tekla MCP server - entry point. Transport: stdio.
// stdout belongs to the MCP protocol. Anything else (including Tekla's own Console output)
// must go to stderr, otherwise the JSON-RPC stream breaks.
// -----------------------------------------------------------------------------------------
Console.SetOut(Console.Error);

var exeDir = AppDomain.CurrentDomain.BaseDirectory;
var settingsPath = Environment.GetEnvironmentVariable("SHERIN_TEKLA_MCP_SETTINGS");
if (string.IsNullOrWhiteSpace(settingsPath)) settingsPath = Path.Combine(exeDir, "config", "settings.json");
var settings = ServerSettings.Load(settingsPath);
var rulesDir = string.IsNullOrWhiteSpace(settings.RulesDirectory) ? Path.Combine(exeDir, "config", "rules") : settings.RulesDirectory;
var useMock = Environment.GetEnvironmentVariable("SHERIN_TEKLA_MCP_MOCK") == "1";

Console.Error.WriteLine($"[server] Sherin Tekla MCP {ServerInfo.Version} | settings: {settings.LoadedFrom} | ReadOnly={settings.ReadOnly} AllowDelete={settings.AllowDelete}");

var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    DisableDefaults = true,
    ContentRootPath = exeDir,
});
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(new ConfirmationService());
builder.Services.AddSingleton(new OperationLog(settings.ResolveLogDirectory()));
builder.Services.AddSingleton(new RuleRepository(rulesDir));

#if NET48
if (useMock)
{
    builder.Services.AddSingleton<ITeklaService>(new MockTeklaService());
}
else
{
    // Must run before the first Tekla type is touched: loads the INSTALLED Tekla 2025 DLLs.
    SherinTeklaMcp.Tekla.TeklaAssemblyResolver.Register(settings.TeklaBinDir);
    // Created through a factory so no Tekla type is loaded before the resolver is in place.
    builder.Services.AddSingleton<ITeklaService>(_ => TeklaBackend.Create(settings));
}
#else
// net8.0 build: there is no Tekla - always the mock backend.
_ = useMock;
builder.Services.AddSingleton<ITeklaService>(new MockTeklaService());
#endif

builder.Services.AddSingleton(sp => new WriteOperations(
    sp.GetRequiredService<ITeklaService>(),
    sp.GetRequiredService<ServerSettings>(),
    sp.GetRequiredService<ConfirmationService>(),
    sp.GetRequiredService<OperationLog>()));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "sherin-tekla-mcp",
            Title = "Sherin Tekla MCP (Tekla Structures 2025)",
            Version = ServerInfo.Version,
        };
        options.ServerInstructions = ServerInfo.Instructions;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();
