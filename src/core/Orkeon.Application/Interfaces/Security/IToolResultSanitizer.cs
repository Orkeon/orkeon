using Orkeon.Domain.Security;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// What <see cref="IToolResultSanitizer"/> made of a tool result before it enters the
/// conversation.
/// </summary>
public sealed record ToolResultSanitization
{
    /// <summary>Gets the text the model receives: the result tagged as data, or a blocked notice.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the result was withheld (<c>Security:ToolResults:Policy = Block</c>).</summary>
    public bool Blocked { get; init; }

    /// <summary>Gets the injection patterns detected in the result.</summary>
    public IReadOnlyList<ThreatDetection> Threats { get; init; } = [];
}

/// <summary>
/// Port applied to every tool result before it is fed back to the model — the first vector
/// of indirect prompt injection. Implemented in Infrastructure (<c>ToolResultSanitizer</c>):
/// detects injection patterns, tags the result as data rather than instructions, and never
/// rewrites it silently — a result is either passed tagged, or withheld with a notice.
/// </summary>
public interface IToolResultSanitizer
{
    /// <summary>
    /// Screens and tags <paramref name="result"/>, the successful output of
    /// <paramref name="toolName"/> called by <paramref name="agentRole"/>.
    /// </summary>
    ToolResultSanitization Sanitize(string toolName, string result, string agentRole);
}
