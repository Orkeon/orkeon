namespace Orkeon.Scripting.Configuration;

/// <summary>
/// Transcript caps of an agent instance (RT-03), bound to the <c>Orkeon:Scripting:AgentInstances</c> section.
/// </summary>
public sealed record ScriptAgentInstanceOptions
{
    /// <summary>Configuration section name (<c>Orkeon:Scripting:AgentInstances</c>).</summary>
    public const string SectionName = "Orkeon:Scripting:AgentInstances";

    /// <summary>The most messages one instance keeps in its transcript.</summary>
    public int MaxTranscriptMessages { get; init; } = 40;

    /// <summary>The most characters (sum of message contents) one instance keeps in its transcript.</summary>
    public int MaxTranscriptCharacters { get; init; } = 32_000;

    /// <summary>Refuses a cap that is not positive.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTranscriptMessages, nameof(MaxTranscriptMessages));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTranscriptCharacters, nameof(MaxTranscriptCharacters));
    }
}
