using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmUsageSink"/> that hands every event to a cost manager itself, as the scripted REPL's
/// sink does; <see cref="Events"/> counts what it received.
/// </summary>
public sealed class StubForwardingLlmUsageSink(ICostBudgetManager? manager = null) : ILlmUsageSink
{
    public int Events;

    public void Record(CostUsageEvent usage)
    {
        Interlocked.Increment(ref Events);
        manager?.RecordUsage(usage);
    }
}
