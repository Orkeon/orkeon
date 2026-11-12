namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when <c>crew.remove(agent)</c> is called on a crew that does not contain
/// the agent — including the case where the agent belongs to a different crew.
/// </summary>
public sealed class AgentNotInThisCrewException : Exception
{
    /// <summary>Name of the agent.</summary>
    public string AgentName { get; }

    /// <summary>Name of the crew the caller targeted.</summary>
    public string CrewName { get; }

    /// <inheritdoc />
    public AgentNotInThisCrewException(string agentName, string crewName)
        : base($"Agent '{agentName}' is not a member of crew '{crewName}'.")
    {
        AgentName = agentName;
        CrewName = crewName;
    }

    /// <inheritdoc />
    public AgentNotInThisCrewException()
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <inheritdoc />
    public AgentNotInThisCrewException(string message) : base(message)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }

    /// <inheritdoc />
    public AgentNotInThisCrewException(string message, Exception innerException) : base(message, innerException)
    {
        AgentName = string.Empty;
        CrewName = string.Empty;
    }
}
