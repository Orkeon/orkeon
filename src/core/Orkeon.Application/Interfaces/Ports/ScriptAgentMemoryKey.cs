namespace Orkeon.Application.Interfaces.Ports;

/// <summary>Durable memory key of an agent: <c>agent:&lt;project&gt;:&lt;crew&gt;:&lt;agent&gt;</c>.</summary>
public sealed record ScriptAgentMemoryKey
{
    private const string Prefix = "agent:";

    /// <summary>Raw crew segment of a crew without a name; a crew named <c>%none</c> is written <c>%25none</c>.</summary>
    internal const string NoneSegment = "%none";

    /// <summary>Project path.</summary>
    public required string Project { get; init; }

    /// <summary>Crew name; <c>null</c> when the crew has no name.</summary>
    public required string? Crew { get; init; }

    /// <summary>Agent name.</summary>
    public required string Agent { get; init; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Prefix}{Project}:{(Crew?.Replace("%", "%25", StringComparison.Ordinal).Replace(":", "%3A", StringComparison.Ordinal) ?? NoneSegment)}:{Agent}";

    /// <summary>
    /// Parses a key written by <see cref="ToString"/>. Only the crew segment is escaped (<c>%</c> as <c>%25</c>,
    /// <c>:</c> as <c>%3A</c>). The project ends at its first <c>:</c> after an optional drive letter
    /// (<c>C:\</c> or <c>C:/</c>), so a project holding another <c>:</c> does not round-trip; the agent is the rest
    /// of the key and may hold any character.
    /// </summary>
    /// <exception cref="ArgumentException">The prefix is not <c>agent:</c> or a segment is empty.</exception>
    public static ScriptAgentMemoryKey Parse(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            throw Malformed(key);

        var start = Prefix.Length;
        var driveColon = start + 2 < key.Length && char.IsAsciiLetter(key[start]) && key[start + 1] == ':' && key[start + 2] is '\\' or '/';
        var projectEnd = key.IndexOf(':', driveColon ? start + 2 : start);
        var crewEnd = projectEnd < 0 ? -1 : key.IndexOf(':', projectEnd + 1);
        if (crewEnd < 0)
            throw Malformed(key);

        var project = key[start..projectEnd];
        var crew = key[(projectEnd + 1)..crewEnd];
        var agent = key[(crewEnd + 1)..];
        if (project.Length == 0 || crew.Length == 0 || agent.Length == 0)
            throw Malformed(key);

        return new ScriptAgentMemoryKey
        {
            Project = project,
            Crew = crew == NoneSegment ? null : crew.Replace("%3A", ":", StringComparison.Ordinal).Replace("%25", "%", StringComparison.Ordinal),
            Agent = agent,
        };
    }

    private static ArgumentException Malformed(string key)
        => new($"Agent key '{key}' is not agent:<project>:<crew>:<agent>.", nameof(key));
}
