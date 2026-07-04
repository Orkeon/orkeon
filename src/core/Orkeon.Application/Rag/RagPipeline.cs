using System.Diagnostics;
using Orkeon.Application.Interfaces.Rag;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Rag;

/// <summary>
/// Default RAG pipeline implementation that orchestrates retrieval, augmentation, and generation.
/// </summary>
public partial class RagPipeline : IRagPipeline
{
    private readonly IRetriever _retriever;
    private readonly IContextAugmenter _augmenter;
    private readonly IResponseGenerator _generator;
    private readonly ILogger<RagPipeline> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RagPipeline"/>.
    /// </summary>
    public RagPipeline(
        IRetriever retriever,
        IContextAugmenter augmenter,
        IResponseGenerator generator,
        ILogger<RagPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        _retriever = retriever;
        ArgumentNullException.ThrowIfNull(augmenter);
        _augmenter = augmenter;
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<RagResult> ExecuteAsync(string question, RagOptions? options = null, CancellationToken ct = default)
        => ExecuteAsync(new RagQuery { Question = question, Options = options ?? new() }, ct);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<RagResult> ExecuteAsync(RagQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ExecuteCoreAsync(query, ct);
    }

    private async System.Threading.Tasks.Task<RagResult> ExecuteCoreAsync(RagQuery query, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();

        // STEP 1: Retrieval
        var retrievalSw = Stopwatch.StartNew();
        var chunks = await _retriever.RetrieveAsync(query.Question, query.Options.Retrieval, ct).ConfigureAwait(false);
        retrievalSw.Stop();

        LogChunksRetrieved(chunks.Count, retrievalSw.ElapsedMilliseconds);

        // STEP 2: Augmentation
        var augmentedPrompt = await _augmenter.AugmentAsync(
            query.Question, chunks, query.Options.Augmentation, ct).ConfigureAwait(false);

        // STEP 3: Generation
        var generationSw = Stopwatch.StartNew();
        var response = await _generator.GenerateAsync(
            augmentedPrompt, query.Options.Generation, ct).ConfigureAwait(false);
        generationSw.Stop();

        totalSw.Stop();

        return new RagResult
        {
            Answer = response.Text,
            Sources = chunks,
            Metrics = new RagMetrics
            {
                TotalChunksRetrieved = chunks.Count,
                ChunksUsedInPrompt = augmentedPrompt.UsedChunks.Count,
                AverageRelevanceScore = chunks.Count > 0
                    ? chunks.Average(c => c.RelevanceScore)
                    : 0f,
                RetrievalTime = retrievalSw.Elapsed,
                GenerationTime = generationSw.Elapsed,
                TotalTime = totalSw.Elapsed,
                TokensUsed = response.TokensUsed
            }
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrieved {Count} chunks in {ElapsedMs}ms")]
    private partial void LogChunksRetrieved(int count, long elapsedMs);
}
