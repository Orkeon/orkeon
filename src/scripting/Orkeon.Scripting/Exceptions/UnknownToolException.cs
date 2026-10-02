using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown by <c>ctx.llm.act</c> when the agent's <c>.tools([...])</c> names a tool the host's
/// catalogue does not offer (GAP-27) — the procedural twin of the load failure a declarative crew
/// gets for the same name. It reaches the script as an <c>UnknownToolError</c>.
/// </summary>
[Serializable]
public sealed class UnknownToolException : Exception
{
    /// <summary>The agent whose <c>.tools([...])</c> names them.</summary>
    public string AgentName { get; }

    /// <summary>The names no tool of the host answers to, as the agent wrote them.</summary>
    public IReadOnlyList<string> ToolNames { get; }

    /// <summary>The tools the host offers, by name, sorted.</summary>
    public IReadOnlyList<string> AvailableTools { get; }

    /// <summary>Initializes the exception for <paramref name="agentName"/>.</summary>
    /// <param name="agentName">The agent whose tool list names the unknown tools.</param>
    /// <param name="toolNames">The names the host does not offer.</param>
    /// <param name="availableTools">The names the host offers.</param>
    public UnknownToolException(string agentName, IReadOnlyList<string> toolNames, IReadOnlyList<string> availableTools)
        : base(Describe(agentName, toolNames, availableTools))
    {
        AgentName = agentName;
        ToolNames = toolNames;
        AvailableTools = availableTools;
    }

    /// <inheritdoc />
    public UnknownToolException() : this(string.Empty, [], []) { }

    /// <inheritdoc />
    public UnknownToolException(string message) : base(message)
    {
        AgentName = string.Empty;
        ToolNames = [];
        AvailableTools = [];
    }

    /// <inheritdoc />
    public UnknownToolException(string message, Exception innerException) : base(message, innerException)
    {
        AgentName = string.Empty;
        ToolNames = [];
        AvailableTools = [];
    }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private UnknownToolException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        AgentName = string.Empty;
        ToolNames = [];
        AvailableTools = [];
    }
#pragma warning restore SYSLIB0051

    private static string Describe(string agentName, IReadOnlyList<string> toolNames, IReadOnlyList<string> availableTools)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        ArgumentNullException.ThrowIfNull(availableTools);
        var unknown = string.Join(", ", toolNames.Select(n => $"'{n}'"));
        var offered = availableTools.Count > 0 ? string.Join(", ", availableTools) : "none — the host offers no built-in tool";
        return $"Agent '{agentName}' lists unknown tool(s) in .tools([...]): {unknown}. " +
            $"ctx.llm.act offers only the host's tools. Available tools: {offered}.";
    }
}
