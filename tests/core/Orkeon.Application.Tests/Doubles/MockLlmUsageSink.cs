using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmUsageSink"/> that keeps every usage event it receives, in order — what
/// the run's token meter would have been told.
/// </summary>
internal sealed class MockLlmUsageSink : ILlmUsageSink
{
    private readonly Lock _gate = new();
    private readonly List<CostUsageEvent> _recorded = [];

    /// <summary>The events received so far, oldest first.</summary>
    public IReadOnlyList<CostUsageEvent> Recorded
    {
        get { lock (_gate) return [.. _recorded]; }
    }

    /// <inheritdoc />
    public void Record(CostUsageEvent usage)
    {
        lock (_gate)
            _recorded.Add(usage);
    }
}
