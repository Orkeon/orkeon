using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmUsageSink"/> that keeps every usage event it receives, in order — what a
/// run's token meter would have been told. Thread-safe: parallel agents report concurrently.
/// </summary>
public sealed class MockLlmUsageSink : ILlmUsageSink
{
    private readonly Lock _gate = new();
    private readonly List<CostUsageEvent> _recorded = [];

    /// <summary>The events received so far, oldest first.</summary>
    public IReadOnlyList<CostUsageEvent> Recorded
    {
        get { lock (_gate) return [.. _recorded]; }
    }

    /// <summary>The total the meter would show: both directions of every event.</summary>
    public long TotalTokens
    {
        get { lock (_gate) return _recorded.Sum(e => (long)e.PromptTokens + e.CompletionTokens); }
    }

    /// <inheritdoc />
    public void Record(CostUsageEvent usage)
    {
        lock (_gate)
            _recorded.Add(usage);
    }
}
