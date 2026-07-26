using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Routing;

/// <summary>
/// Constrained lightweight LLM query-complexity classifier (Adaptive-RAG, guide
/// §8.4): one short few-shot chat call asks the model for
/// <c>{"route": "no_retrieval" | "single_shot" | "iterative"}</c>. The JSON
/// constraint is applied twice — <see cref="ChatOptions.ResponseFormat"/> is set
/// to <see cref="ChatResponseFormat.Json"/> for providers that honour it, and
/// the prompt itself demands strict JSON for those that ignore the option (the
/// universal fallback).
/// </summary>
/// <remarks>
/// Parsing is deliberately tolerant: a JSON object anywhere in the response, a
/// case-insensitive <c>route</c> property, and reasonable synonyms
/// (<c>none|no_retrieval</c>, <c>single|single_shot|simple</c>,
/// <c>iterative|multi_hop|complex</c>) are all accepted; a bare route word with
/// no JSON at all still parses. An unusable response falls back to
/// <see cref="QueryRoute.SingleShot"/> with a warning — the safe route: the cost
/// of one unnecessary retrieval is lower than the cost of an ungrounded answer.
/// A bad LLM answer never throws.
/// </remarks>
public sealed partial class LlmQueryComplexityClassifier : IQueryComplexityClassifier
{
    /// <summary>Few-shot system prompt of the routing call (3 examples, one per route).</summary>
    public const string SystemPrompt =
        "You are a query-complexity router for a RAG system. Classify the user query into exactly one route:\n" +
        "- \"no_retrieval\": greetings, small talk, opinions, or instructions on provided text — no documents needed.\n" +
        "- \"single_shot\": a simple factual question answerable with one retrieval pass.\n" +
        "- \"iterative\": a multi-hop, comparative, or multi-document question needing several retrieval passes.\n" +
        "Respond with ONLY a JSON object of the form {\"route\": \"<route>\"}. No prose, no explanation.\n" +
        "\n" +
        "Examples:\n" +
        "Query: \"Hello! How are you doing today?\"\n" +
        "{\"route\": \"no_retrieval\"}\n" +
        "Query: \"What is the capital of Australia?\"\n" +
        "{\"route\": \"single_shot\"}\n" +
        "Query: \"Compare the retention policies described in the 2023 and 2024 compliance reports and list what changed.\"\n" +
        "{\"route\": \"iterative\"}";

    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmQueryComplexityClassifier> _logger;

    /// <summary>Creates the classifier over the host's chat client.</summary>
    public LlmQueryComplexityClassifier(
        IChatClient chatClient,
        ILogger<LlmQueryComplexityClassifier>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _logger = logger ?? NullLogger<LlmQueryComplexityClassifier>.Instance;
    }

    /// <inheritdoc />
    public async Task<QueryRoute> ClassifyAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        // An empty query has nothing to retrieve against — no LLM call needed.
        if (string.IsNullOrWhiteSpace(query))
        {
            return QueryRoute.NoRetrieval;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, "Query: \"" + query + "\""),
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
        var route = ParseRoute(text);
        if (route is null)
        {
            LogUnusableResponse(Truncate(text, 200));
            return QueryRoute.SingleShot;
        }

        return route.Value;
    }

    /// <summary>
    /// Extracts a <see cref="QueryRoute"/> from the model output. Accepts a JSON
    /// object anywhere in the text (case-insensitive <c>route</c> property and
    /// value, synonyms allowed), or a bare route word as a last resort. Returns
    /// <c>null</c> only when nothing usable was found — never throws on
    /// malformed output.
    /// </summary>
    internal static QueryRoute? ParseRoute(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        return TryParseJsonObjects(responseText) ?? TryParseBareRouteWord(responseText);
    }

    private static QueryRoute? TryParseJsonObjects(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = text.IndexOf('}', start + 1);
            if (end < 0)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(text[start..(end + 1)]);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name.Equals("route", StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.String
                            && NormalizeRoute(property.Value.GetString()) is { } route)
                        {
                            return route;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not a valid JSON object at this position — keep scanning.
            }

            start = text.IndexOf('{', start + 1);
        }

        return null;
    }

    private static QueryRoute? TryParseBareRouteWord(string text)
    {
        foreach (var token in text.Split(
            [' ', '\t', '\r', '\n', ',', ';', ':', '.', '!', '"', '\'', '`', '(', ')', '[', ']'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (NormalizeRoute(token) is { } route)
            {
                return route;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a route value to <see cref="QueryRoute"/> — case-insensitive,
    /// <c>-</c>/<c> </c> treated as <c>_</c>, reasonable synonyms accepted.
    /// </summary>
    private static QueryRoute? NormalizeRoute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

#pragma warning disable CA1308 // the synonym table is lowercase by construction, not a comparison normalization
        var normalized = value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
#pragma warning restore CA1308

        return normalized switch
        {
            "none" or "no_retrieval" or "noretrieval" or "direct" => QueryRoute.NoRetrieval,
            "single" or "single_shot" or "singleshot" or "simple" or "one_shot" => QueryRoute.SingleShot,
            "iterative" or "multi_hop" or "multihop" or "complex" or "multi_step" => QueryRoute.Iterative,
            _ => null,
        };
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Query-complexity response is unusable — falling back to the safe SingleShot route " +
                  "(one unnecessary retrieval costs less than an ungrounded answer). Response head: '{ResponseHead}'.")]
    private partial void LogUnusableResponse(string responseHead);
}
