using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Constrained lightweight LLM <see cref="IGroundednessChecker"/> (RAG-06, guide
/// §8) — the first real implementation of the hook the staged pipeline traced as
/// «skipped» until now: one short chat call verifies that the generated answer
/// is anchored in the retrieved context and lists the unsupported claims. The
/// JSON constraint is applied twice — <see cref="ChatOptions.ResponseFormat"/>
/// is set to <see cref="ChatResponseFormat.Json"/> for providers that honour it,
/// and the prompt itself demands strict JSON for those that ignore the option.
/// </summary>
/// <remarks>
/// Parsing is deliberately tolerant: a balanced JSON object anywhere in the
/// response, case-insensitive property names, boolean synonyms
/// (<c>yes|no|grounded|ungrounded</c>), numeric strings for the score. An
/// unusable response falls back to <b>grounded</b> with a warning — the safe
/// side for a verification stage: a broken verifier must not veto answers nor
/// fuel a rewrite storm. A malformed LLM output never throws.
/// </remarks>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class LlmGroundednessChecker : IGroundednessChecker
{
    /// <summary>System prompt of the verification call.</summary>
    public const string SystemPrompt =
        "You are a groundedness verifier for a RAG system. Given a question, a candidate answer, " +
        "and the source passages the answer must be based on, verify that every claim of the answer " +
        "is supported by the passages.\n" +
        "Respond with ONLY a JSON object of the form " +
        "{\"grounded\": <true|false>, \"score\": <0..1>, \"unsupported_claims\": [\"<claim>\"], " +
        "\"reason\": \"<short reason>\"}. No prose, no explanation.";

    private const int MaxChunkExcerptLength = 500;

    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmGroundednessChecker> _logger;

    /// <summary>Creates the checker over the host's chat client.</summary>
    public LlmGroundednessChecker(
        IChatClient chatClient,
        ILogger<LlmGroundednessChecker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _logger = logger ?? NullLogger<LlmGroundednessChecker>.Instance;
    }

    /// <inheritdoc />
    public async Task<GroundednessResult> CheckAsync(
        string question,
        string answer,
        IReadOnlyList<ScoredChunk> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // No context to verify against: an empty answer is vacuously grounded,
        // anything else cannot be supported. Deterministic — no LLM call.
        if (context.Count == 0)
        {
            var empty = string.IsNullOrWhiteSpace(answer);
            return new GroundednessResult
            {
                IsGrounded = empty,
                Score = empty ? 1.0 : 0.0,
                Rationale = "no context to verify against",
            };
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildUserMessage(question, answer, context)),
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
        var result = ParseResult(text);
        if (result is null)
        {
            LogUnusableResponse(Truncate(text, 200));
            return new GroundednessResult
            {
                IsGrounded = true,
                Score = 0.0,
                Rationale = "unusable checker response — safe grounded fallback",
            };
        }

        return result;
    }

    /// <summary>
    /// Extracts a <see cref="GroundednessResult"/> from the model output: a
    /// balanced JSON object anywhere in the text with a usable
    /// <c>grounded</c>/<c>is_grounded</c> property (boolean synonyms accepted).
    /// Returns <c>null</c> only when nothing usable was found — never throws on
    /// malformed output.
    /// </summary>
    internal static GroundednessResult? ParseResult(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        using var document = TolerantJsonReader.ExtractFirstObject(responseText);
        if (document is null)
            return null;

        var root = document.RootElement;
        var grounded = TolerantJsonReader.GetBool(root, "grounded")
            ?? TolerantJsonReader.GetBool(root, "is_grounded");
        if (grounded is not { } isGrounded)
            return null;

        var score = TolerantJsonReader.GetDouble(root, "score");
        return new GroundednessResult
        {
            IsGrounded = isGrounded,
            Score = Math.Clamp(score ?? (isGrounded ? 1.0 : 0.0), 0.0, 1.0),
            UnsupportedClaims = ParseClaims(root),
            Rationale = TolerantJsonReader.GetString(root, "reason")
                ?? TolerantJsonReader.GetString(root, "rationale"),
        };
    }

    private static ImmutableList<string> ParseClaims(JsonElement root)
    {
        var claims = TolerantJsonReader.GetPropertyIgnoreCase(root, "unsupported_claims")
            ?? TolerantJsonReader.GetPropertyIgnoreCase(root, "unsupportedClaims")
            ?? TolerantJsonReader.GetPropertyIgnoreCase(root, "claims");
        if (claims is not { ValueKind: JsonValueKind.Array } array)
            return ImmutableList<string>.Empty;

        var builder = ImmutableList.CreateBuilder<string>();
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String
                && entry.GetString() is { } claim
                && !string.IsNullOrWhiteSpace(claim))
            {
                builder.Add(claim);
            }
        }

        return builder.ToImmutable();
    }

    private static string BuildUserMessage(
        string question,
        string answer,
        IReadOnlyList<ScoredChunk> context)
    {
        var builder = new StringBuilder();
        builder
            .Append("Question: ").AppendLine(question).AppendLine()
            .Append("Answer to verify: ").AppendLine(answer).AppendLine()
            .AppendLine("Source passages:");
#pragma warning disable S3267 // StringBuilder append loop — the allocation-free idiom; LINQ+Join would change prompt bytes
        foreach (var scored in context)
        {
            builder
                .Append("- id: ").AppendLine(scored.Chunk.Id)
                .Append("  text: ").AppendLine(Truncate(scored.Chunk.Content, MaxChunkExcerptLength));
        }
#pragma warning restore S3267

        return builder.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Groundedness-checker response is unusable — falling back to the safe grounded verdict " +
                  "(a broken verifier must not veto answers). Response head: '{ResponseHead}'.")]
    private partial void LogUnusableResponse(string responseHead);
}
