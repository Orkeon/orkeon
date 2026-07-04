using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when <c>crew.add(agent)</c> would introduce a second agent that shares its
/// human-readable name with one already in the crew.
/// </summary>
[Serializable]
public sealed class DuplicateAgentNameException : Exception
{
    /// <summary>Conflicting name.</summary>
    public string AgentName { get; }

    /// <summary>Name of the affected crew.</summary>
    public string CrewName { get; }

    /// <inheritdoc />
    public DuplicateAgentNameException(string agentName, string crewName)
        : base($"Crew '{crewName}' already contains an agent named '{agentName}'.")
    {
        AgentName = agentName;
        CrewName = crewName;
    }

    /// <inheritdoc />
    public DuplicateAgentNameException()
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <inheritdoc />
    public DuplicateAgentNameException(string message) : base(message)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <inheritdoc />
    public DuplicateAgentNameException(string message, Exception innerException) : base(message, innerException)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private DuplicateAgentNameException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }
#pragma warning restore SYSLIB0051
}
