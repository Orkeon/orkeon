namespace Orkeon.Analysis.Abstractions.Interfaces;

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
