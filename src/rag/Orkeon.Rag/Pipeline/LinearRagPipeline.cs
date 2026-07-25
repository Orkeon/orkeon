using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Linear <see cref="IRagPipeline"/>: embeds the query, retrieves candidates from
/// the <see cref="IDocumentStore"/>, assembles a numbered context, generates a
/// grounded answer through an <see cref="IChatClient"/> (same generation approach
/// as the legacy <c>ChatClientResponseGenerator</c>), and returns a
/// <see cref="RagAnswer"/> with citations and a trace.
/// </summary>
/// <remarks>
/// When retrieval yields no candidate, generation is skipped and
/// <see cref="NoContextAnswer"/> is returned with an explanatory trace — the
/// pipeline never lets the model answer ungrounded.
/// </remarks>
public sealed partial class LinearRagPipeline : IRagPipeline
{
    /// <summary>Default grounded system prompt (anti-hallucination, <c>[n]</c> citation markers).</summary>
    public const string DefaultSystemPrompt =
        "You are a retrieval-augmented assistant. Answer ONLY from the provided context. " +
        "Cite the context passages you use with their [n] markers. " +
        "If the context does not contain the answer, say so explicitly instead of guessing.";

    /// <summary>Deterministic answer returned when retrieval yields no candidate.</summary>
    public const string NoContextAnswer =
        "No relevant context was found in the knowledge base for this question.";

    private readonly IDocumentStore _store;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IChatClient _chatClient;
    private readonly LinearRagPipelineOptions _options;
    private readonly ILogger<LinearRagPipeline> _logger;

    /// <summary>Initializes the linear pipeline.</summary>
    /// <param name="store">Document store queried at retrieval.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed the query.</param>
    /// <param name="chatClient">Chat client used for grounded generation.</param>
    /// <param name="options">Pipeline options; <c>null</c> selects the defaults.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public LinearRagPipeline(
        IDocumentStore store,
        IEmbeddingProvider embeddingProvider,
        IChatClient chatClient,
        LinearRagPipelineOptions? options = null,
        ILogger<LinearRagPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(chatClient);

        _store = store;
        _embeddingProvider = embeddingProvider;
        _chatClient = chatClient;
        _options = options ?? new LinearRagPipelineOptions();
        _logger = logger ?? NullLogger<LinearRagPipeline>.Instance;
    }

    /// <inheritdoc />
    public async Task<RagAnswer> QueryAsync(
        RagQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Collection);

        var steps = ImmutableList.CreateBuilder<RagTraceStep>();

        // 1 — Retrieve (vector search when the provider yields a vector, text otherwise).
        var retrieveWatch = Stopwatch.StartNew();
        var queryVector = await _embeddingProvider.GetEmbeddingAsync(query.Text, cancellationToken)
            .ConfigureAwait(false);

        var retrievalQuery = new RetrievalQuery
        {
            Text = query.Text,
            Embedding = queryVector.Length > 0 ? [.. queryVector] : null,
            TopK = _options.CandidateK,
            Filters = query.Filters,
        };

        var candidates = await _store.SearchAsync(query.Collection, retrievalQuery, cancellationToken)
            .ConfigureAwait(false);
        retrieveWatch.Stop();

        steps.Add(new RagTraceStep
        {
            Name = "retrieve",
            Duration = retrieveWatch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("candidates", candidates.Count.ToString(CultureInfo.InvariantCulture))
                .Add("top_k", _options.CandidateK.ToString(CultureInfo.InvariantCulture))
                .Add("mode", retrievalQuery.Embedding is null ? "text" : "vector"),
        });

        // 2 — Truncate to TopN (rerankers slot in here in later batches).
        var context = candidates.Take(Math.Max(0, query.TopN)).ToList();

        if (context.Count == 0)
        {
            LogNoContextRetrieved(query.Collection);
            steps.Add(new RagTraceStep
            {
                Name = "generate",
                Detail = "skipped — no retrieved context",
            });

            return new RagAnswer
            {
                Text = NoContextAnswer,
                Citations = ImmutableList<Citation>.Empty,
                Trace = new RagTrace { Steps = steps.ToImmutable() },
            };
        }

        // 3 — Assemble the numbered context ([n] markers resolved by the citations).
        var assembleWatch = Stopwatch.StartNew();
        var contextBlock = BuildContextBlock(context);
        assembleWatch.Stop();

        steps.Add(new RagTraceStep
        {
            Name = "assemble",
            Duration = assembleWatch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("chunks", context.Count.ToString(CultureInfo.InvariantCulture)),
        });

        // 4 — Grounded generation (same approach as the legacy ChatClientResponseGenerator).
        var generateWatch = Stopwatch.StartNew();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _options.SystemPrompt ?? DefaultSystemPrompt),
            new(ChatRole.User, $"Context:\n{contextBlock}\n\nQuestion: {query.Text}"),
        };

        var chatOptions = new ChatOptions
        {
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxOutputTokens,
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken)
            .ConfigureAwait(false);
        generateWatch.Stop();

        var generateData = ImmutableDictionary<string, string>.Empty;
        if (!string.IsNullOrEmpty(response.ModelId))
        {
            generateData = generateData.Add("model", response.ModelId);
        }

        steps.Add(new RagTraceStep
        {
            Name = "generate",
            Duration = generateWatch.Elapsed,
            Data = generateData,
        });

        return new RagAnswer
        {
            Text = response.Text ?? string.Empty,
            Citations = BuildCitations(context),
            Trace = new RagTrace { Steps = steps.ToImmutable() },
        };
    }

    private static string BuildContextBlock(List<ScoredChunk> context)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < context.Count; i++)
        {
            var chunk = context[i].Chunk;
            builder.Append('[').Append(i + 1).Append("] (source: ").Append(chunk.SourceId).AppendLine(")");
            builder.AppendLine(chunk.Content);
            if (i < context.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static ImmutableList<Citation> BuildCitations(List<ScoredChunk> context)
    {
        const int snippetLength = 200;

        var citations = ImmutableList.CreateBuilder<Citation>();
        for (var i = 0; i < context.Count; i++)
        {
            var scored = context[i];
            var chunk = scored.Chunk;
            citations.Add(new Citation
            {
                Marker = i + 1,
                ChunkId = chunk.Id,
                SourceId = chunk.SourceId,
                DocumentId = chunk.DocumentId,
                Snippet = chunk.Content.Length <= snippetLength
                    ? chunk.Content
                    : chunk.Content[..snippetLength],
                Score = scored.Score,
                StartOffset = chunk.StartOffset,
                EndOffset = chunk.EndOffset,
            });
        }

        return citations.ToImmutable();
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No context retrieved from collection '{Collection}' — generation skipped, deterministic no-context answer returned.")]
    private partial void LogNoContextRetrieved(string collection);
}
