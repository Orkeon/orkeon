using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Identifies the caller that is currently invoking the EventHub (agent + crew),
/// used by the hub to stamp <c>SourceCrewId</c> and <c>SourceAgentId</c> on outbound messages.
/// </summary>
/// <param name="CrewId">Crew that owns the current execution context.</param>
/// <param name="AgentId">Optional agent invoking the hub; <see langword="null"/> when the caller is the crew itself or the hub.</param>
public sealed record EventHubCaller(CrewId CrewId, AgentId? AgentId);

/// <summary>
/// Ambient access to the <see cref="EventHubCaller"/> for the current async-flow.
/// Implementations are expected to be backed by <see cref="System.Threading.AsyncLocal{T}"/>.
/// </summary>
public interface IEventHubCallerContext
{
    /// <summary>Gets the caller in scope for the current async-flow.</summary>
    EventHubCaller Current { get; }

    /// <summary>Pushes <paramref name="caller"/> as the current caller; <c>Dispose</c> restores the previous value.</summary>
    IDisposable Push(EventHubCaller caller);
}
