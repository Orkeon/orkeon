using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Default <see cref="IIngestionPipeline"/>: loaders → security validation →
/// chunking (strategy resolved by name via <see cref="ChunkingStrategyFactory"/>) →
/// embeddings (Application port <see cref="IEmbeddingProvider"/>) →
/// <see cref="IDocumentStore.UpsertAsync"/>.
/// </summary>
/// <remarks>
/// Per-source and per-document failures are reported as
/// <see cref="IngestionReport.Errors"/> entries and do not abort the run; an
/// unknown chunking strategy fails the whole run loudly
/// (<see cref="RagComponentNotFoundException"/>) — never a silent fallback.
/// Documents rejected or quarantined by the <see cref="DataValidationPipeline"/>
/// (anti-injection, provenance, quarantine) never reach the store.
/// </remarks>
public sealed partial class DefaultIngestionPipeline : IIngestionPipeline
{
    private readonly DocumentLoaderFactory _loaderFactory;
    private readonly ChunkingStrategyFactory _chunkingFactory;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IDocumentStore _store;
    private readonly DataValidationPipeline _validation;
    private readonly RagIngestionOptions _options;
    private readonly ILogger<DefaultIngestionPipeline> _logger;

    /// <summary>Initializes the ingestion pipeline.</summary>
    /// <param name="loaderFactory">Resolves the loader able to handle each source.</param>
    /// <param name="chunkingFactory">Resolves the chunking strategy by name.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed chunk contents.</param>
    /// <param name="store">Target document store.</param>
    /// <param name="validation">Ingestion-path security validation (anti-injection, provenance, quarantine).</param>
    /// <param name="options">Pipeline options; <c>null</c> selects the defaults.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public DefaultIngestionPipeline(
        DocumentLoaderFactory loaderFactory,
        ChunkingStrategyFactory chunkingFactory,
        IEmbeddingProvider embeddingProvider,
        IDocumentStore store,
        DataValidationPipeline validation,
        RagIngestionOptions? options = null,
        ILogger<DefaultIngestionPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loaderFactory);
        ArgumentNullException.ThrowIfNull(chunkingFactory);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validation);

        _loaderFactory = loaderFactory;
        _chunkingFactory = chunkingFactory;
        _embeddingProvider = embeddingProvider;
        _store = store;
        _validation = validation;
        _options = options ?? new RagIngestionOptions();
        _logger = logger ?? NullLogger<DefaultIngestionPipeline>.Instance;
    }

    /// <inheritdoc />
    public async Task<IngestionReport> IngestAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var strategyName = request.ChunkingStrategy ?? _options.DefaultChunkingStrategy;

        // Unknown strategy names fail the run loudly (never a silent fallback).
        var strategy = _chunkingFactory.Create(strategyName);

        var documentsLoaded = 0;
        var chunksCreated = 0;
        var chunksEmbedded = 0;
        var chunksSkipped = 0;
        var errors = ImmutableList.CreateBuilder<string>();

        foreach (var source in request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_loaderFactory.TryGetLoader(source, out var loader))
            {
                // Surfaced, never swallowed: the report carries the failure.
                LogNoLoaderForSource(source.Location);
                errors.Add($"No document loader can handle source '{source.Location}' (kind: '{source.Kind ?? "none"}').");
                continue;
            }

            try
            {
                await foreach (var document in loader.LoadAsync(source, cancellationToken).ConfigureAwait(false))
                {
                    documentsLoaded++;

                    var outcome = await IngestDocumentAsync(request, strategy, document, cancellationToken)
                        .ConfigureAwait(false);
                    chunksCreated += outcome.ChunksCreated;
                    chunksEmbedded += outcome.ChunksEmbedded;
                    chunksSkipped += outcome.ChunksSkipped;
                    if (outcome.Error is not null)
                    {
                        errors.Add(outcome.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or FileNotFoundException or InvalidOperationException or ArgumentException)
            {
                LogSourceFailed(source.Location, ex.Message);
                errors.Add($"Source '{source.Location}' failed: {ex.Message}");
            }
        }

        stopwatch.Stop();
        return new IngestionReport
        {
            Collection = request.Collection,
            DocumentsLoaded = documentsLoaded,
            ChunksCreated = chunksCreated,
            ChunksEmbedded = chunksEmbedded,
            ChunksSkipped = chunksSkipped,
            Duration = stopwatch.Elapsed,
            Errors = errors.ToImmutable(),
        };
    }

    private async Task<DocumentOutcome> IngestDocumentAsync(
        IngestionRequest request,
        IChunkingStrategy strategy,
        RagDocument document,
        CancellationToken cancellationToken)
    {
        // Security validation sits on the ingestion path: rejected or quarantined
        // documents never reach the store (plan §3.3).
        var validationContext = new DataValidationContext(
            DocumentId: document.Id,
            Source: document.SourceId,
            Metadata: document.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value));

        var verdict = await _validation.ValidateAsync(document.Content, validationContext, cancellationToken)
            .ConfigureAwait(false);
        if (verdict.Decision != DataValidationDecision.Allow)
        {
            LogDocumentBlocked(document.Id, verdict.Decision.ToString(), verdict.Reason ?? "no reason");
            return new DocumentOutcome(
                Error: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Document '{document.Id}' {verdict.Decision}: {verdict.Reason ?? "no reason"}"));
        }

        var chunks = strategy.Chunk(document, request.Chunking);
        if (chunks.Count == 0)
        {
            return new DocumentOutcome();
        }

        var embeddings = await _embeddingProvider
            .GetEmbeddingsAsync(chunks.Select(c => c.Content).ToList(), cancellationToken)
            .ConfigureAwait(false);

        if (embeddings.Count != chunks.Count)
        {
            // Fail loudly for this document: silently dropping or padding vectors
            // would corrupt the collection.
            return new DocumentOutcome(
                ChunksCreated: chunks.Count,
                ChunksSkipped: chunks.Count,
                Error: $"Document '{document.Id}': embedding provider returned {embeddings.Count} vectors for {chunks.Count} chunks.");
        }

        var embedded = new List<EmbeddedChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            embedded.Add(new EmbeddedChunk
            {
                Chunk = chunks[i],
                Embedding = [.. embeddings[i]],
                EmbeddingModel = _embeddingProvider.Model,
            });
        }

        await _store.UpsertAsync(request.Collection, embedded, cancellationToken).ConfigureAwait(false);

        return new DocumentOutcome(ChunksCreated: chunks.Count, ChunksEmbedded: embedded.Count);
    }

    private sealed record DocumentOutcome(
        int ChunksCreated = 0,
        int ChunksEmbedded = 0,
        int ChunksSkipped = 0,
        string? Error = null);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No document loader can handle source '{Location}'.")]
    private partial void LogNoLoaderForSource(string location);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source '{Location}' failed during ingestion: {Reason}")]
    private partial void LogSourceFailed(string location, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document '{DocumentId}' blocked by validation ({Decision}): {Reason}")]
    private partial void LogDocumentBlocked(string documentId, string decision, string reason);
}
