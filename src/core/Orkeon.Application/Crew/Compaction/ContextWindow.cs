namespace Orkeon.Application.Crew.Compaction;

/// <summary>A model's context window in tokens, and where the figure comes from (#432).</summary>
/// <param name="Tokens">The window in tokens, above zero.</param>
/// <param name="Source">Where it comes from: one of <see cref="ContextWindowSources"/>.</param>
public sealed record ContextWindow(int Tokens, string Source);

/// <summary>Where a <see cref="ContextWindow"/> comes from: the <c>windowSource</c> of <c>agent.compacted</c>.</summary>
public static class ContextWindowSources
{
    /// <summary>The profile's own <c>ContextWindowTokens</c>.</summary>
    public const string Profile = "profile";

    /// <summary>The documented window of the model (<c>LlmModelContextWindows</c>).</summary>
    public const string Table = "table";

    /// <summary><c>Orkeon:Cli:Session:ContextWindowTokens</c>, its default included.</summary>
    public const string Settings = "settings";
}
