using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Reranking;

/// <summary>
/// Universal listwise LLM reranker (guide §7): one <see cref="IChatClient"/>
/// call presents the query plus the numbered candidates and asks for a JSON
/// array of candidate numbers ordered by relevance. Works with all 12 Orkeon
/// LLM providers — the built-in fallback when the ONNX cross-encoder package
/// is not referenced.
/// </summary>
/// <remarks>
/// Parsing is deliberately tolerant: out-of-range or duplicate indices are
/// dropped, missing indices are appended in their original order, and a fully
/// malformed response falls back to the original candidate order with a
/// warning — a bad LLM answer never throws. Returned scores are the normalized
/// descending rank (best = 1.0) with <see cref="ScoredChunk.ScoreOrigin"/> set
/// to <c>llm-rerank</c>.
/// </remarks>
public sealed partial class LlmListwiseReranker : IReranker
{
    /// <summary>Canonical factory name (<c>listwise</c> is the registered alias).</summary>
    public const string RerankerName = "llm";

    /// <summary>Value stamped on <see cref="ScoredChunk.ScoreOrigin"/>.</summary>
    public const string ScoreOrigin = "llm-rerank";

    /// <summary>System prompt of the listwise ranking call.</summary>
    public const string SystemPrompt =
        "You are a relevance ranking assistant. Given a query and numbered passages, " +
        "respond with ONLY a JSON array of the passage numbers ordered from most to " +
        "least relevant to the query (example: [3,1,2]). No prose, no explanation.";

    // Candidates are truncated to keep the single ranking call within any
    // provider's context window (50 candidates x 1000 chars ≈ 12k tokens).
    private const int MaxPassageChars = 1000;

    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmListwiseReranker> _logger;

    /// <summary>Creates the reranker over the host's chat client.</summary>
    public LlmListwiseReranker(IChatClient chatClient, ILogger<LlmListwiseReranker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _logger = logger ?? NullLogger<LlmListwiseReranker>.Instance;
    }

    /// <inheritdoc />
    public string Name => RerankerName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0 || topN <= 0)
        {
            return [];
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildUserPrompt(query, candidates)),
        };

        var response = await _chatClient
            .GetResponseAsync(messages, new ChatOptions { Temperature = 0f }, cancellationToken)
            .ConfigureAwait(false);

        var order = ParseOrder(response.Text ?? string.Empty, candidates.Count);
        if (order is null)
        {
            LogMalformedResponse(Truncate(response.Text ?? string.Empty, 200));
            order = [.. Enumerable.Range(0, candidates.Count)];
        }

        var kept = Math.Min(topN, order.Count);
        var result = new ScoredChunk[kept];
        for (var i = 0; i < kept; i++)
        {
            result[i] = candidates[order[i]] with
            {
                Score = (double)(kept - i) / kept,
                ScoreOrigin = ScoreOrigin,
            };
        }

        return result;
    }

    /// <summary>
    /// Extracts a 0-based candidate order from the model output. Accepts a JSON
    /// array anywhere in the text, or bare integers as a last resort. Invalid
    /// (out-of-range) and duplicate indices are dropped; indices the model
    /// omitted are appended in their original order. Returns <c>null</c> only
    /// when no usable index was found — never throws on malformed output.
    /// </summary>
    internal static List<int>? ParseOrder(string responseText, int candidateCount)
    {
        var ranked = TryParseJsonArray(responseText) ?? TryParseBareIntegers(responseText);
        if (ranked is null)
        {
#pragma warning disable S1168 // null signals "no usable index" (documented contract), distinct from an empty ranking
            return null;
#pragma warning restore S1168
        }

        var seen = new bool[candidateCount];
        var order = new List<int>(candidateCount);
        foreach (var oneBased in ranked)
        {
            var index = oneBased - 1;
            if (index >= 0 && index < candidateCount && !seen[index])
            {
                seen[index] = true;
                order.Add(index);
            }
        }

        if (order.Count == 0)
        {
            return null;
        }

        // Tolerance: indices the model forgot keep their original relative order.
        for (var i = 0; i < candidateCount; i++)
        {
            if (!seen[i])
            {
                order.Add(i);
            }
        }

        return order;
    }

    private static List<int>? TryParseJsonArray(string text)
    {
        var start = text.IndexOf('[', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = text.IndexOf(']', start + 1);
            if (end < 0)
            {
#pragma warning disable S1168 // null signals a parse failure — returning [] would short-circuit the `??` fallback chain
                return null;
#pragma warning restore S1168
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<int>>(text[start..(end + 1)]);
                if (parsed is { Count: > 0 })
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Not a valid JSON int array at this position — keep scanning.
            }

            start = text.IndexOf('[', end + 1);
        }

        return null;
    }

    private static List<int>? TryParseBareIntegers(string text)
    {
        var matches = BareIntegerRegex().Matches(text);
        if (matches.Count == 0)
        {
#pragma warning disable S1168 // null signals a parse failure — returning [] would short-circuit the `??` fallback chain
            return null;
#pragma warning restore S1168
        }

        var result = new List<int>(matches.Count);
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                result.Add(value);
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static string BuildUserPrompt(string query, IReadOnlyList<ScoredChunk> candidates)
    {
        var builder = new StringBuilder();
        builder.Append("Query: ").AppendLine(query).AppendLine().AppendLine("Passages:");
        for (var i = 0; i < candidates.Count; i++)
        {
            builder.Append('[').Append(i + 1).Append("] ")
                .AppendLine(Truncate(candidates[i].Chunk.Content, MaxPassageChars));
        }

        builder.Append("Return the JSON array of the ").Append(candidates.Count)
            .Append(" passage numbers, most relevant first.");
        return builder.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"-?\d+")]
    private static partial Regex BareIntegerRegex();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Listwise rerank response is malformed — keeping the original candidate order. Response head: '{ResponseHead}'.")]
    private partial void LogMalformedResponse(string responseHead);
}
