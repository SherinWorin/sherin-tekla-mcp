namespace TeklaAiAssistant.Tekla2025.Probe;

using TeklaAiAssistant.Core.Commands;

internal sealed class HostCommandContext : ICommandContext
{
    public HostCommandContext(ITeklaSession session, ILogger log, BundleInfo bundle)
    {
        Session = session;
        Log = log;
        Bundle = bundle;
    }

    public ITeklaSession Session { get; }
    public ILogger Log { get; }
    public BundleInfo Bundle { get; }
}
