using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Constrained lightweight LLM retrieval evaluator (CRAG, guide §8): one short
/// chat call grades the retrieved chunk set for the query as
/// <c>correct | incorrect | ambiguous</c>, with optional per-chunk relevances.
/// The JSON constraint is applied twice — <see cref="ChatOptions.ResponseFormat"/>
/// is set to <see cref="ChatResponseFormat.Json"/> for providers that honour it,
/// and the prompt itself demands strict JSON for those that ignore the option
/// (the universal fallback, same motif as the RAG-05 complexity classifier).
/// </summary>
/// <remarks>
/// Parsing is deliberately tolerant: a balanced JSON object anywhere in the
/// response (code fences and prose tolerated), case-insensitive property names,
/// grade synonyms, numeric strings for relevances — and a bare grade word with
/// no JSON at all still parses. An unusable response falls back to
/// <see cref="RetrievalGrade.Ambiguous"/> with a warning — the safe grade: it
/// routes to refinement, never to a blind generation nor to a rewrite storm.
/// A malformed LLM output never throws.
/// </remarks>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class LlmRetrievalEvaluator : IRetrievalEvaluator
{
    /// <summary>System prompt of the grading call.</summary>
    public const string SystemPrompt =
        "You are a retrieval grader for a corrective RAG system. Given a user question and the " +
        "retrieved passages, grade whether the passages contain the information needed to answer:\n" +
        "- \"correct\": the passages clearly contain the answer.\n" +
        "- \"incorrect\": the passages are irrelevant to the question.\n" +
        "- \"ambiguous\": the passages are partially relevant or you are unsure.\n" +
        "Respond with ONLY a JSON object of the form " +
        "{\"grade\": \"<correct|incorrect|ambiguous>\", \"reason\": \"<short reason>\", " +
        "\"chunks\": [{\"id\": \"<chunk id>\", \"relevance\": <0..1>}]}. No prose, no explanation.";

    private const int MaxChunkExcerptLength = 500;

    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmRetrievalEvaluator> _logger;

    /// <summary>Creates the evaluator over the host's chat client.</summary>
    public LlmRetrievalEvaluator(
        IChatClient chatClient,
        ILogger<LlmRetrievalEvaluator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _logger = logger ?? NullLogger<LlmRetrievalEvaluator>.Instance;
    }

    /// <inheritdoc />
    public async Task<RetrievalVerdict> EvaluateAsync(
        string query,
        IReadOnlyList<ScoredChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(chunks);
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing retrieved: deterministically Incorrect — no LLM call needed.
        if (chunks.Count == 0)
        {
            return new RetrievalVerdict
            {
                Grade = RetrievalGrade.Incorrect,
                Rationale = "no chunks retrieved",
            };
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildUserMessage(query, chunks)),
        };

        var options = new ChatOptions
        {
            Temperature = 0f,
            ResponseFormat = ChatResponseFormat.Json,
        };

        var response = await _chatClient
            .GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        var text = response.Text ?? string.Empty;
        var verdict = ParseVerdict(text);
        if (verdict is null)
        {
            LogUnusableResponse(Truncate(text, 200));
            return new RetrievalVerdict
            {
                Grade = RetrievalGrade.Ambiguous,
                Rationale = "unusable evaluator response — safe Ambiguous fallback",
            };
        }

        return verdict;
    }

    /// <summary>
    /// Extracts a <see cref="RetrievalVerdict"/> from the model output: a
    /// balanced JSON object anywhere in the text (case-insensitive properties,
    /// grade synonyms, tolerant relevances), or a bare grade word as a last
    /// resort. Returns <c>null</c> only when nothing usable was found — never
    /// throws on malformed output.
    /// </summary>
    internal static RetrievalVerdict? ParseVerdict(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        using var document = TolerantJsonReader.ExtractFirstObject(responseText);
        if (document is not null)
        {
            var root = document.RootElement;
            var grade = NormalizeGrade(TolerantJsonReader.GetString(root, "grade"))
                ?? NormalizeGrade(TolerantJsonReader.GetString(root, "verdict"));
            if (grade is { } parsedGrade)
            {
                return new RetrievalVerdict
                {
                    Grade = parsedGrade,
                    Rationale = TolerantJsonReader.GetString(root, "reason")
                        ?? TolerantJsonReader.GetString(root, "rationale"),
                    ChunkRelevances = ParseChunkRelevances(root),
                };
            }
        }

        return TryParseBareGradeWord(responseText);
    }

    private static ImmutableList<ChunkRelevance> ParseChunkRelevances(JsonElement root)
    {
        var chunks = TolerantJsonReader.GetPropertyIgnoreCase(root, "chunks");
        if (chunks is not { ValueKind: JsonValueKind.Array } array)
            return ImmutableList<ChunkRelevance>.Empty;

        var relevances = ImmutableList.CreateBuilder<ChunkRelevance>();
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            var id = TolerantJsonReader.GetString(entry, "id")
                ?? TolerantJsonReader.GetString(entry, "chunk_id");
            var relevance = TolerantJsonReader.GetDouble(entry, "relevance")
                ?? TolerantJsonReader.GetDouble(entry, "score");
            if (string.IsNullOrWhiteSpace(id) || relevance is not { } value)
                continue;

            relevances.Add(new ChunkRelevance
            {
                ChunkId = id,
                Relevance = Math.Clamp(value, 0.0, 1.0),
            });
        }

        return relevances.ToImmutable();
    }

    private static RetrievalVerdict? TryParseBareGradeWord(string text)
    {
        foreach (var token in text.Split(
            [' ', '\t', '\r', '\n', ',', ';', ':', '.', '!', '"', '\'', '`', '(', ')', '[', ']', '{', '}'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (NormalizeGrade(token) is { } grade)
                return new RetrievalVerdict { Grade = grade };
        }

        return null;
    }

    /// <summary>
    /// Maps a grade value to <see cref="RetrievalGrade"/> — case-insensitive,
    /// reasonable synonyms accepted.
    /// </summary>
    private static RetrievalGrade? NormalizeGrade(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToUpperInvariant() switch
        {
            "CORRECT" or "RELEVANT" or "YES" or "GOOD" => RetrievalGrade.Correct,
            "INCORRECT" or "IRRELEVANT" or "NO" or "BAD" or "WRONG" => RetrievalGrade.Incorrect,
            "AMBIGUOUS" or "PARTIAL" or "UNSURE" or "UNCERTAIN" or "MIXED" => RetrievalGrade.Ambiguous,
            _ => null,
        };
    }

    private static string BuildUserMessage(string query, IReadOnlyList<ScoredChunk> chunks)
    {
        var builder = new StringBuilder();
        builder.Append("Question: ").AppendLine(query).AppendLine().AppendLine("Retrieved passages:");
#pragma warning disable S3267 // StringBuilder append loop — the allocation-free idiom; LINQ+Join would change prompt bytes
        foreach (var scored in chunks)
        {
            var content = scored.Chunk.Content;
            builder
                .Append("- id: ").AppendLine(scored.Chunk.Id)
                .Append("  text: ").AppendLine(Truncate(content, MaxChunkExcerptLength));
        }
#pragma warning restore S3267

        return builder.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Retrieval-evaluator response is unusable — falling back to the safe Ambiguous grade " +
                  "(routes to refinement, never to a blind generation). Response head: '{ResponseHead}'.")]
    private partial void LogUnusableResponse(string responseHead);
}
