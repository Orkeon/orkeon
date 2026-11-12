namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when a context-level operation that requires crew membership is called on an
/// agent currently detached from any crew (e.g. <c>ctx.delegate</c>, <c>ctx.send</c>,
/// <c>ctx.receive</c> outside a crew).
/// </summary>
public sealed class AgentNotInCrewException : Exception
{
    /// <summary>Name of the agent.</summary>
    public string AgentName { get; }

    /// <inheritdoc />
    public AgentNotInCrewException(string agentName)
        : base($"Agent '{agentName}' is not attached to any crew. Add it via crew.add(...) or include it in crewBuilder().withAgent(...).")
    {
        AgentName = agentName;
    }

    /// <inheritdoc />
    public AgentNotInCrewException()
    {
        AgentName = string.Empty;
    }

    /// <inheritdoc />
    public AgentNotInCrewException(string message, Exception innerException) : base(message, innerException)
    {
        AgentName = string.Empty;
    }
}
