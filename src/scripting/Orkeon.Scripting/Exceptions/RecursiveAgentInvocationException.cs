using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown in V1 when an agent attempts to invoke itself recursively (directly or via
/// <c>ctx.spawn</c>). Reentrant invocation is V1.5.
/// </summary>
[Serializable]
public sealed class RecursiveAgentInvocationException : Exception
{
    /// <summary>Name of the agent being recursively invoked.</summary>
    public string AgentName { get; }

    /// <inheritdoc />
    public RecursiveAgentInvocationException(string agentName)
        : base($"Recursive invocation of agent '{agentName}' is not supported in V1.")
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

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private RecursiveAgentInvocationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        AgentName = string.Empty;
    }
#pragma warning restore SYSLIB0051
}
