using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when <c>crew.add(agent)</c> is called for an agent that is already a member
/// of another crew. V1 enforces mono-crew (one crew at a time per agent).
/// </summary>
[Serializable]
public sealed class AgentAlreadyInCrewException : Exception
{
    /// <summary>Name of the offending agent.</summary>
    public string AgentName { get; }

    /// <summary>Name of the crew the agent already belongs to.</summary>
    public string CrewName { get; }

    /// <inheritdoc />
    public AgentAlreadyInCrewException(string agentName, string crewName)
        : base($"Agent '{agentName}' is already a member of crew '{crewName}' (mono-crew enforced).")
    {
        AgentName = agentName;
        CrewName = crewName;
    }

    /// <inheritdoc />
    public AgentAlreadyInCrewException()
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <inheritdoc />
    public AgentAlreadyInCrewException(string message) : base(message)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <inheritdoc />
    public AgentAlreadyInCrewException(string message, Exception innerException) : base(message, innerException)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private AgentAlreadyInCrewException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }
#pragma warning restore SYSLIB0051
}
