using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// Shared variant generation for <see cref="MultiQueryTransformer"/> and
/// <see cref="RagFusionTransformer"/> (guide §6.6): one <see cref="IChatClient"/>
/// call asks for <see cref="QueryTransformContext.MaxVariants"/> alternative
/// phrasings of the question; the response is parsed tolerantly
/// (<see cref="QueryVariantParser"/> — numbered lines, bullets, or a JSON string
/// array). The returned list always starts with the ORIGINAL query, followed by
/// the deduplicated (case-insensitive) variants. An unusable or failing LLM
/// response degrades to <c>[original]</c> with a logged warning — never an
/// exception. The two subclasses differ only by <see cref="Name"/> and
/// <see cref="Kind"/> (union vs RRF-fusion retrieve semantics).
/// </summary>
public abstract partial class LlmQueryVariantTransformerBase : IQueryTransformer
{
    /// <summary>System prompt of the variant-generation call.</summary>
    public const string SystemPrompt =
        "You are a search query expansion assistant. Given a user question, generate " +
        "alternative phrasings that express the same information need from different " +
        "angles (synonyms, specificity, perspective). Respond with ONLY the requested " +
        "number of variants, one per line, numbered. No preamble, no explanation.";

    // Mild sampling temperature: variants should diverge from each other
    // (a deterministic call tends to produce near-identical paraphrases).
    private const float VariantTemperature = 0.7f;

    private const int MaxLoggedResponseChars = 200;

    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;

    private protected LlmQueryVariantTransformerBase(IChatClient chatClient, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract QueryTransformKind Kind { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> TransformAsync(
        string query,
        QueryTransformContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var requested = context.MaxVariants > 0
            ? context.MaxVariants
            : RagDefaults.QueryTransformVariantCount;

        string responseText;
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, BuildUserPrompt(query, requested)),
            };

            var response = await _chatClient
                .GetResponseAsync(messages, new ChatOptions { Temperature = VariantTemperature }, cancellationToken)
                .ConfigureAwait(false);

            responseText = response.Text ?? string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogGenerationFailed(Name, exception);
            return [query];
        }

        var result = ComposeResult(query, QueryVariantParser.Parse(responseText), requested);
        if (result.Count == 1)
        {
            LogUnusableResponse(Name, Truncate(responseText));
        }

        return result;
    }

    /// <summary>
    /// Builds <c>[original, v1…vN]</c>: the original query first, then up to
    /// <paramref name="requested"/> variants deduplicated case-insensitively
    /// (against the original and against each other).
    /// </summary>
    private static List<string> ComposeResult(string query, List<string> variants, int requested)
    {
        var result = new List<string>(1 + requested) { query };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { query.Trim() };

#pragma warning disable S3267 // pre-seeded dedupe set plus an early break on a count cap; not expressible without a side-effecting predicate
        foreach (var variant in variants)
        {
            if (seen.Add(variant))
            {
                result.Add(variant);
                if (result.Count == requested + 1)
                {
                    break;
                }
            }
        }
#pragma warning restore S3267

        return result;
    }

    private static string BuildUserPrompt(string query, int requested) =>
        $"Question: {query}\n\nGenerate {requested} alternative phrasings of this question, one per line, numbered.";

    private static string Truncate(string text) =>
        text.Length <= MaxLoggedResponseChars ? text : text[..MaxLoggedResponseChars];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Query-variant generation failed for transformer '{TransformerName}' — falling back to the original query only.")]
    private partial void LogGenerationFailed(string transformerName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Query-variant response for transformer '{TransformerName}' yielded no usable variant — falling back to the original query only. Response head: '{ResponseHead}'.")]
    private partial void LogUnusableResponse(string transformerName, string responseHead);
}
