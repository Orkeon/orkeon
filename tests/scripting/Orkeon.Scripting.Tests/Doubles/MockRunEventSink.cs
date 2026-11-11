using Orkeon.Application.Runs;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>A run event sink that keeps every event it is written, in order.</summary>
internal sealed class MockRunEventSink : IRunEventSink
{
    private readonly Lock _sync = new();
    private readonly List<RunEvent> _events = [];

    /// <summary>A copy of the events written so far.</summary>
    public IReadOnlyList<RunEvent> Events
    {
        get { lock (_sync) return [.. _events]; }
    }

    public Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        lock (_sync) _events.Add(runEvent);
        return Task.CompletedTask;
    }
}
