using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Produces natural-language summaries for RaggableTree nodes.
/// </summary>
public interface INodeSummarizer
{
    Task<string?> SummarizeAsync(
        RaggableNode node,
        SummarizationContext context,
        CancellationToken ct);

    Task SummarizeAsync(
        IEnumerable<RaggableNode> nodes,
        IReadOnlyDictionary<string, RaggableNode> nodeIndex,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return EnrichBatchAsync([.. nodes], nodeIndex, ct);
    }

    Task EnrichBatchAsync(
        IReadOnlyList<RaggableNode> candidates,
        IReadOnlyDictionary<string, RaggableNode> nodeIndex,
        CancellationToken ct);
}

public sealed record SummarizationContext(
    string? ParentSummary,
    IReadOnlyList<CompactNode> TopCallers,
    IReadOnlyList<CompactNode> TopCallees,
    string? TruncatedBody);

public sealed class NullNodeSummarizer : INodeSummarizer
{
    public static readonly NullNodeSummarizer Instance = new();

    public Task<string?> SummarizeAsync(
        RaggableNode node,
        SummarizationContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parentHint = !string.IsNullOrEmpty(node.ParentId) ? $" in {node.ParentId}" : string.Empty;
        var docHint = string.IsNullOrEmpty(node.DocComment) ? string.Empty : " — " + FirstSentence(node.DocComment);
        var summary = $"{node.EffectiveKind} '{node.Name}'{parentHint}{docHint}";
        return Task.FromResult<string?>(summary);
    }

    public Task EnrichBatchAsync(
        IReadOnlyList<RaggableNode> candidates,
        IReadOnlyDictionary<string, RaggableNode> nodeIndex,
        CancellationToken ct) => Task.CompletedTask;

    private static string FirstSentence(string doc)
    {
        var trimmed = doc.Trim();
        var stop = trimmed.IndexOfAny(['.', '\n', '\r']);
        if (stop < 0) return trimmed;
        return trimmed[..stop].Trim();
    }
}
