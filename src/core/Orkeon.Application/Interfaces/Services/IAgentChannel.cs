using Orkeon.Domain.Common;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Bi-directional, request/response communication channel between autonomous agents.
/// Unlike <see cref="IAgentCommunicationService"/> which is fire-and-forget,
/// this channel supports synchronous request/response semantics and correlation tracking.
/// </summary>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IAgentChannel
{
    /// <summary>
    /// Sends a request to the target agent and awaits its response.
    /// </summary>
    /// <param name="request">The agent request.</param>
    /// <param name="timeout">Maximum time to wait for a response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the target agent.</returns>
    /// <exception cref="TimeoutException">Thrown when the target agent does not respond within <paramref name="timeout"/>.</exception>
    System.Threading.Tasks.Task<AgentChannelResponse> RequestAsync(
        AgentChannelRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers this agent as available to receive requests on the channel.
    /// The handler is invoked for each incoming request.
    /// </summary>
    /// <param name="agentId">The agent to register.</param>
    /// <param name="handler">An async handler that processes incoming requests and returns a response.</param>
    /// <returns>A disposable registration; disposing it unregisters the agent.</returns>
    IDisposable RegisterHandler(
        AgentId agentId,
        Func<AgentChannelRequest, CancellationToken, System.Threading.Tasks.Task<AgentChannelResponse>> handler);

    /// <summary>
    /// Broadcasts a notification to all agents in the crew (no response expected).
    /// </summary>
    /// <param name="fromAgentId">The sender agent.</param>
    /// <param name="crewId">The crew scope for the broadcast.</param>
    /// <param name="content">The notification content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task BroadcastAsync(
        AgentId fromAgentId,
        CrewId crewId,
        string content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A typed request sent from one agent to another over the <see cref="IAgentChannel"/>.
/// </summary>
/// <param name="CorrelationId">Unique ID for request/response correlation.</param>
/// <param name="FromAgentId">The agent sending the request.</param>
/// <param name="ToAgentId">The target agent.</param>
/// <param name="Intent">Describes what the sender is asking for (e.g. "clarify", "delegate", "provide_data").</param>
/// <param name="Payload">The request content (natural language or structured data).</param>
/// <param name="Metadata">Optional key-value metadata for extensibility.</param>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record AgentChannelRequest(
    Guid CorrelationId,
    AgentId FromAgentId,
    AgentId ToAgentId,
    string Intent,
    string Payload,
    IReadOnlyDictionary<string, object>? Metadata = null)
{
    /// <summary>Creates a new request with an auto-generated correlation ID.</summary>
    public static AgentChannelRequest Create(
        AgentId from, AgentId to, string intent, string payload,
        IReadOnlyDictionary<string, object>? metadata = null)
        => new(Guid.NewGuid(), from, to, intent, payload, metadata);
}

/// <summary>
/// A typed response to an <see cref="AgentChannelRequest"/>.
/// </summary>
/// <param name="CorrelationId">Must match the request's <see cref="AgentChannelRequest.CorrelationId"/>.</param>
/// <param name="FromAgentId">The agent that produced the response.</param>
/// <param name="Success">Whether the request was fulfilled successfully.</param>
/// <param name="Payload">The response content.</param>
/// <param name="Error">Error message if <paramref name="Success"/> is <c>false</c>.</param>
[Experimental("ORKEXP002", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record AgentChannelResponse(
    Guid CorrelationId,
    AgentId FromAgentId,
    bool Success,
    string Payload,
    string? Error = null)
{
    /// <summary>Creates a successful response.</summary>
    public static AgentChannelResponse Ok(Guid correlationId, AgentId from, string payload)
        => new(correlationId, from, true, payload);

    /// <summary>Creates a failure response.</summary>
    public static AgentChannelResponse Fail(Guid correlationId, AgentId from, string error)
        => new(correlationId, from, false, string.Empty, error);
}
