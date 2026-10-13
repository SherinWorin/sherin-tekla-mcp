namespace TeklaAiAssistant.Tekla2025.Probe;

using System.Text;
using TeklaAiAssistant.Core.Commands;

internal sealed class HostLog : ILogger, IDisposable
{
    private readonly StreamWriter writer;
    private readonly object sync = new();

    public HostLog(string stateDirectory)
    {
        var directory = System.IO.Path.Combine(stateDirectory, "Logs");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory,
            "selection-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
        writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string Path { get; }
    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception? exception = null)
        => Write("ERROR", message + (exception is null ? "" : Environment.NewLine + exception));

    private void Write(string level, string message)
    {
        lock (sync) writer.WriteLine(DateTime.UtcNow.ToString("O") + " " + level + " " + message);
    }

    public void Dispose()
    {
        lock (sync) writer.Dispose();
    }
}
