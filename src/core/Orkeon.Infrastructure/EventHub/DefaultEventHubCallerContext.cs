using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// Default <see cref="IEventHubCallerContext"/> backed by <see cref="AsyncLocal{T}"/>.
/// When no caller has been pushed, returns a caller stamped as <see cref="CrewId.System"/> /
/// <see langword="null"/> agent, matching the system-emitter semantics described in spec §6.1.
/// </summary>
public sealed class DefaultEventHubCallerContext : IEventHubCallerContext
{
    private static readonly EventHubCaller s_systemCaller = new(CrewId.System, null);
    private readonly AsyncLocal<EventHubCaller?> _current = new();

    /// <inheritdoc/>
    public EventHubCaller Current => _current.Value ?? s_systemCaller;

    /// <inheritdoc/>
    public IDisposable Push(EventHubCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var previous = _current.Value;
        _current.Value = caller;
        return new PopScope(this, previous);
    }

    private sealed class PopScope(DefaultEventHubCallerContext owner, EventHubCaller? previous) : IDisposable
    {
        public void Dispose() => owner._current.Value = previous;
    }
}
