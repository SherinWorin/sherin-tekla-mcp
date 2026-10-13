namespace TeklaAiAssistant.Tekla2025.Probe;

using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Tekla.Structures;
using Tekla.Structures.Model;
using TeklaAiAssistant.Core.Commands;
using UiModelObjectSelector = Tekla.Structures.Model.UI.ModelObjectSelector;

/// <summary>
/// Owns the Tekla connection and serializes all vendor calls on one dedicated thread.
/// Dispatch commands through ExecuteAsync; the session must not escape its callback.
/// </summary>
public sealed class TeklaSessionManager : ITeklaSession, IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new(128);
    private readonly Thread _thread;
    private readonly Action<string>? _log;
    private readonly string? _expectedModelPath;
    private Model? _model;
    private Exception? _initializationError;
    private string? _modelPath;
    private int _disposed;
    private int _lastSkippedNonPartCount;

    public TeklaSessionManager(Action<string>? log = null, string? expectedModelPath = null)
    {
        _log = log;
        _expectedModelPath = expectedModelPath is null ? null : NormalizeModelPath(expectedModelPath);
        _thread = new Thread(Run)
        {
            Name = "Tekla read-only session queue",
            IsBackground = true
        };
        _thread.Start();
    }

    /// <summary>The most recently observed path, never a cached model snapshot.</summary>
    public string? ModelPath => Volatile.Read(ref _modelPath);

    public int LastSkippedNonPartCount => Volatile.Read(ref _lastSkippedNonPartCount);

    public bool IsConnected
    {
        get
        {
            EnsureQueueThread();
            CheckConnectionAndModel();
            return true;
        }
    }

    /// <summary>
    /// Host example: await session.ExecuteAsync(s => command.Execute(new Context(s, log, bundle))).
    /// Construct the command context inside the synchronous callback and use its supplied
    /// session. A full queue fails immediately rather than blocking the host thread.
    /// </summary>
    public Task<T> ExecuteAsync<T>(Func<ITeklaSession, T> operation)
    {
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));
        if (Thread.CurrentThread == _thread)
            throw new InvalidOperationException("Nested dispatch to the Tekla queue is not supported.");

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(
            () =>
            {
                try
                {
                    var path = CheckConnectionAndModel();
                    var result = operation(this);
                    EnsureSameModel(path);
                    completion.TrySetResult(result);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            () => completion.TrySetException(new ObjectDisposedException(nameof(TeklaSessionManager))));

        if (Volatile.Read(ref _disposed) != 0)
        {
            item.Cancel();
            return completion.Task;
        }

        try
        {
            if (!_queue.TryAdd(item))
                completion.TrySetException(new InvalidOperationException("The Tekla execution queue is full. Try again after pending reads finish."));
        }
        catch (InvalidOperationException)
        {
            // Dispose may close the queue after the disposed check.
            item.Cancel();
        }

        return completion.Task;
    }

    /// <summary>Queue-only read of the verified 2025 string/ref advanced-option API.</summary>
    public bool TryGetMacroDirectory(out string directory)
    {
        EnsureQueueThread();
        var path = CheckConnectionAndModel();
        directory = string.Empty;
        var available = TeklaStructuresSettings.GetAdvancedOption("XS_MACRO_DIRECTORY", ref directory);
        EnsureSameModel(path);
        return available;
    }

    public IReadOnlyList<SelectedPartInfo> GetSelectedParts()
    {
        EnsureQueueThread();
        Volatile.Write(ref _lastSkippedNonPartCount, 0);
        var path = CheckConnectionAndModel();
            var parts = new List<SelectedPartInfo>();
            var skipped = 0;
            // Confirmed by the Tekla.Structures.Model 2025.0.0 XML documentation:
            // AutoFetch is a static property used for all enumerators in the application.
            ModelObjectEnumerator.AutoFetch = true;
            var selected = new UiModelObjectSelector().GetSelectedObjects();
        while (selected.MoveNext())
        {
            if (selected.Current is not Part part)
            {
                skipped++;
                continue;
            }

            var length = 0.0;
            var lengthAvailable = part.GetReportProperty("LENGTH", ref length);
            parts.Add(CreatePartInfo(part, lengthAvailable, length));
        }

        EnsureSameModel(path);
        Volatile.Write(ref _lastSkippedNonPartCount, skipped);
        _log?.Invoke($"Read {parts.Count} selected part(s); skipped {skipped} non-part selection(s). " +
                     "LENGTH is a report value; millimetres are assumed, not independently verified.");
        return parts.AsReadOnly();
    }

    private static SelectedPartInfo CreatePartInfo(Part part, bool lengthAvailable, double length)
    {
        var partPosition = string.Empty;
        var assemblyPosition = string.Empty;
        var partPositionAvailable = part.GetReportProperty("PART_POS", ref partPosition);
        var assemblyPositionAvailable = part.GetReportProperty("ASSEMBLY_POS", ref assemblyPosition);
        return new SelectedPartInfo(
            part.Identifier.ID.ToString(CultureInfo.InvariantCulture),
            part.Profile?.ProfileString ?? "<unavailable>",
            part.Material?.MaterialString ?? "<unavailable>",
            lengthAvailable && !double.IsNaN(length) && !double.IsInfinity(length) ? length : (double?)null,
            partPositionAvailable && !string.IsNullOrWhiteSpace(partPosition) ? partPosition : "<unavailable>",
            assemblyPositionAvailable && !string.IsNullOrWhiteSpace(assemblyPosition) ? assemblyPosition : "<unavailable>");
    }

    private void Run()
    {
        try
        {
            _model = new Model();
        }
        catch (Exception exception)
        {
            _initializationError = exception;
        }

        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (Volatile.Read(ref _disposed) != 0)
                item.Cancel();
            else
                item.Execute();
        }
        // No vendor object leaves this thread. Model does not require a commit or close.
        _model = null;
    }

    private Model GetModel()
    {
        EnsureQueueThread();
        if (_initializationError is not null)
            throw new InvalidOperationException("The Tekla Model connection could not be created.", _initializationError);
        return _model ?? throw new InvalidOperationException("The Tekla Model connection is unavailable.");
    }

    private string CheckConnectionAndModel()
    {
        var model = GetModel();
        if (!model.GetConnectionStatus())
            throw new InvalidOperationException("Tekla is not connected to an open model.");

        var path = model.GetInfo().ModelPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Tekla did not report the connected model's path.");
        if (_expectedModelPath is not null
            && !string.Equals(_expectedModelPath, NormalizeModelPath(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable-model guard refused execution. Expected " +
                _expectedModelPath + "; connected model: " + path + ". No selection was read.");
        var previous = _modelPath;
        Volatile.Write(ref _modelPath, path);
        if (previous is not null && !string.Equals(previous, path, StringComparison.OrdinalIgnoreCase))
            _log?.Invoke("Tekla model switch detected; reading the current model selection afresh.");
        return path;
    }

    private void EnsureSameModel(string expectedPath)
    {
        var currentPath = CheckConnectionAndModel();
        if (!string.Equals(expectedPath, currentPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Tekla model changed during execution. Read the selection again.");
    }

    private static string NormalizeModelPath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void EnsureQueueThread()
    {
        if (Thread.CurrentThread != _thread)
            throw new InvalidOperationException("Tekla session access must run inside ExecuteAsync on its dedicated queue thread.");
    }

    /// <summary>
    /// Stops accepting work and cancels queued work. An in-flight vendor call is never
    /// aborted; disposal waits at most two seconds, then the background thread drains
    /// pending work when that call returns.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _queue.CompleteAdding();
        while (_queue.TryTake(out var item))
            item.Cancel();
        if (Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(2));
        // Keep the queue alive until a potentially blocked vendor call returns.
    }

    private sealed class WorkItem
    {
        public WorkItem(Action execute, Action cancel)
        {
            Execute = execute;
            Cancel = cancel;
        }

        public Action Execute { get; }
        public Action Cancel { get; }
    }
}
