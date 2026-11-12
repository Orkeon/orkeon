namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when an agent attempts to invoke itself recursively (directly or via
/// <c>ctx.spawn</c>): an agent's invocation is not reentrant.
/// </summary>
public sealed class RecursiveAgentInvocationException : Exception
{
    /// <summary>Name of the agent being recursively invoked.</summary>
    public string AgentName { get; }

    /// <inheritdoc />
    public RecursiveAgentInvocationException(string agentName)
        : base($"Recursive invocation of agent '{agentName}' is not supported.")
    {
        AgentName = agentName;
    }

    /// <inheritdoc />
    public RecursiveAgentInvocationException()
    {
        AgentName = string.Empty;
    }

    /// <inheritdoc />
    public RecursiveAgentInvocationException(string message, Exception innerException) : base(message, innerException)
    {
        AgentName = string.Empty;
    }
}
