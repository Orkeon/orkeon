using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Analysis.Summarizers;

public sealed class LlmNodeSummarizer : INodeSummarizer
{
    private const int MinSnippetLength = 80;
    private const int TopLinksCount = 3;
    private const int TruncatedBodyLength = 200;

    private readonly ILlmProvider _llm;
    private readonly LlmNodeSummarizerOptions _options;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly IEmbeddingTextComposer? _composer;

    public LlmNodeSummarizer(ILlmProvider llm, LlmNodeSummarizerOptions? options = null, IEmbeddingTextComposer? composer = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _options = options ?? new LlmNodeSummarizerOptions();
        _composer = composer;
    }

    public Task<string?> SummarizeAsync(
        RaggableNode node,
        SummarizationContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(context);
        return SummarizeCoreAsync(node, context, ct);
    }

    private async Task<string?> SummarizeCoreAsync(
        RaggableNode node,
        SummarizationContext context,
        CancellationToken ct)
    {
        if (!IsCandidate(node)) return null;

        var key = ComputeCacheKey(node);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var prompt = BuildPrompt(node, context);
        var config = LlmConfig.Create(_options.Model) with
        {
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxTokens,
        };
        var response = await _llm.GenerateAsync(prompt, config, ct).ConfigureAwait(false);

        var summary = (response?.Content ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(summary)) return null;

        _cache[key] = summary;
        return summary;
    }

    public Task EnrichBatchAsync(
        IReadOnlyList<RaggableNode> candidates,
        IReadOnlyDictionary<string, RaggableNode> nodeIndex,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(nodeIndex);
        return EnrichBatchCoreAsync(candidates, nodeIndex, ct);
    }

    private async Task EnrichBatchCoreAsync(
        IReadOnlyList<RaggableNode> candidates,
        IReadOnlyDictionary<string, RaggableNode> nodeIndex,
        CancellationToken ct)
    {
        var filtered = candidates.Where(IsCandidate).ToList();
        if (filtered.Count == 0) return;

        var ordered = OrderBottomUp(filtered, nodeIndex);
        using var gate = new SemaphoreSlim(_options.Concurrency, _options.Concurrency);

        var tasks = ordered.Select(async node =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var context = BuildContext(node, nodeIndex);
                var summary = await SummarizeAsync(node, context, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(summary)) node.SemanticSummary = summary;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (_composer is not null) _composer.ApplyToAll(candidates, nodeIndex);
    }

    internal static bool IsCandidate(RaggableNode node)
    {
        if (node.Kind is not (UniversalNodeKind.Class
                                or UniversalNodeKind.Interface
                                or UniversalNodeKind.Function
                                or UniversalNodeKind.Method))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(node.SemanticSummary)) return false;
        return (node.SourceSnippet?.Length ?? 0) > MinSnippetLength;
    }

    internal static SummarizationContext BuildContext(RaggableNode node, IReadOnlyDictionary<string, RaggableNode> index)
    {
        string? parentSummary = null;
        if (!string.IsNullOrEmpty(node.ParentId))
        {
            foreach (var candidate in index.Values)
            {
                if (candidate.Id == node.ParentId)
                {
                    parentSummary = candidate.SemanticSummary;
                    break;
                }
            }
        }

        var callers = TakeCompact(node.CalledByIds, index);
        var callees = TakeCompact(node.CallIds, index);
        var snippet = node.SourceSnippet ?? string.Empty;
        var truncated = snippet.Length > TruncatedBodyLength ? snippet[..TruncatedBodyLength] : snippet;
        return new SummarizationContext(parentSummary, callers, callees, truncated);
    }

    private static List<CompactNode> TakeCompact(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, RaggableNode> index)
    {
        if (ids.Count == 0) return [];
        var result = new List<CompactNode>(TopLinksCount);
        foreach (var id in ids)
        {
            if (result.Count >= TopLinksCount) break;
            var node = ResolveNode(id, index);
            if (node is null) continue;
            result.Add(new CompactNode
            {
                Fqn = node.Fqn,
                Kind = node.Kind,
                SummaryShort = node.SemanticSummary,
                Signature = node.Signature,
            });
        }
        return result;
    }

    /// <summary>
    /// Resolves a node by id, preferring the FQN-keyed lookup and falling back to a
    /// linear scan matching <see cref="RaggableNode.Id"/>. Returns null when not found.
    /// </summary>
    private static RaggableNode? ResolveNode(
        string id, IReadOnlyDictionary<string, RaggableNode> index)
    {
        if (index.TryGetValue(id, out var byFqn)) return byFqn;
        foreach (var candidate in index.Values)
        {
            if (candidate.Id == id) return candidate;
        }
        return null;
    }

    private static List<RaggableNode> OrderBottomUp(
        IReadOnlyList<RaggableNode> candidates,
        IReadOnlyDictionary<string, RaggableNode> index)
    {
        int Depth(RaggableNode n)
        {
            var depth = 0;
            var current = n.ParentId;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(current) && seen.Add(current))
            {
                depth++;
                RaggableNode? parent = null;
                foreach (var candidate in index.Values)
                {
                    if (candidate.Id == current) { parent = candidate; break; }
                }
                current = parent?.ParentId;
            }
            return depth;
        }
        return [.. candidates.OrderByDescending(Depth)];
    }

    private static string BuildPrompt(RaggableNode node, SummarizationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Summarize the following code symbol in 1-2 sentences. Focus on intent and responsibility, not mechanics. No code. No quotes.");
        sb.AppendLine();
        sb.Append("Kind: ").AppendLine(node.EffectiveKind.ToString());
        sb.Append("Name: ").AppendLine(node.Name);
        AppendLabeled(sb, "Signature: ", node.Signature);
        AppendLabeled(sb, "Doc: ", node.DocComment);
        AppendLabeled(sb, "Parent: ", context.ParentSummary);
        AppendLinkSection(sb, "Callers:", context.TopCallers);
        AppendLinkSection(sb, "Callees:", context.TopCallees);
        if (!string.IsNullOrEmpty(context.TruncatedBody))
        {
            sb.AppendLine("Body excerpt:");
            sb.AppendLine(context.TruncatedBody);
        }
        return sb.ToString();
    }

    private static void AppendLabeled(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value)) sb.Append(label).AppendLine(value);
    }

    private static void AppendLinkSection(StringBuilder sb, string header, IReadOnlyList<CompactNode> links)
    {
        if (links.Count == 0) return;
        sb.AppendLine(header);
        foreach (var c in links) sb.Append("- ").AppendLine(c.Fqn);
    }

    private static string ComputeCacheKey(RaggableNode node)
    {
        var material = $"{node.Signature}\u0001{node.DocComment}\u0001{string.Join(',', node.CallIds)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record LlmNodeSummarizerOptions
{
    public string Model { get; init; } = "claude-haiku-4-5";
    public int MaxTokens { get; init; } = 120;
    public double Temperature { get; init; } = 0.2;
    public int Concurrency { get; init; } = 5;
}
