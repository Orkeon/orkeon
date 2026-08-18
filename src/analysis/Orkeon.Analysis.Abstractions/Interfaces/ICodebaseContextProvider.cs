namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Provides codebase context summaries automatically injected into agent prompts.
/// </summary>
public interface ICodebaseContextProvider
{
    Task<string> GetContextAsync(CodebaseContextOptions options, CancellationToken ct);
}

public sealed record CodebaseContextOptions
{
    public string Format { get; init; } = "markdown";
    public int TopN { get; init; } = 5;
    public bool IncludePatterns { get; init; } = true;
}
