using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Configuration;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Ingestion;

/// <summary>
/// Default <see cref="IRagCollectionsBootstrapper"/>: maps each declared collection of a
/// crew's <see cref="RagCrewConfig"/> to an <see cref="IngestionRequest"/> and runs it
/// through the incremental <see cref="IIngestionPipeline"/> — a fresh manifest makes the
/// call a no-op (0 embeddings), a stale one re-ingests only the changed sources.
/// </summary>
public sealed partial class RagCollectionsBootstrapper : IRagCollectionsBootstrapper
{
    /// <summary>
    /// Characters-per-token heuristic used to translate the YAML <c>max_tokens</c> chunking
    /// bound into the character-based <see cref="ChunkingOptions.MaxChunkSize"/> — the same
    /// ×4 approximation as the knowledge-context augmenter.
    /// </summary>
    private const int CharactersPerToken = 4;

    private readonly IIngestionPipeline _ingestionPipeline;
    private readonly ILogger<RagCollectionsBootstrapper> _logger;

    /// <summary>Initializes a new instance of <see cref="RagCollectionsBootstrapper"/>.</summary>
    public RagCollectionsBootstrapper(
        IIngestionPipeline ingestionPipeline,
        ILogger<RagCollectionsBootstrapper>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ingestionPipeline);
        _ingestionPipeline = ingestionPipeline;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RagCollectionsBootstrapper>.Instance;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task PrepareAsync(
        RagCrewConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return PrepareCoreAsync(config, cancellationToken);
    }

    private async System.Threading.Tasks.Task PrepareCoreAsync(
        RagCrewConfig config, CancellationToken cancellationToken)
    {
        foreach (var (collection, collectionConfig) in config.Collections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (collectionConfig.Sources.Count == 0)
            {
                LogCollectionWithoutSources(collection);
                continue;
            }

            var request = new IngestionRequest
            {
                Collection = collection,
                Sources = collectionConfig.Sources
                    .Select(source => new SourceDescriptor { Location = source })
                    .ToImmutableList(),
                ChunkingStrategy = collectionConfig.Chunking?.Strategy,
                Chunking = collectionConfig.Chunking is { } chunking
                    ? new ChunkingOptions
                    {
                        MaxChunkSize = chunking.MaxTokens * CharactersPerToken,
                        Overlap = chunking.Overlap * CharactersPerToken,
                    }
                    : new ChunkingOptions(),
            };

            var report = await _ingestionPipeline.IngestAsync(request, cancellationToken).ConfigureAwait(false);
            LogCollectionPrepared(
                collection, report.SourcesAdded, report.SourcesUnchanged, report.SourcesReingested);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG collection '{Collection}' is declared without sources — nothing to ingest.")]
    private partial void LogCollectionWithoutSources(string collection);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RAG collection '{Collection}' prepared: {Added} added, {Unchanged} unchanged, {Reingested} re-ingested.")]
    private partial void LogCollectionPrepared(string collection, int added, int unchanged, int reingested);
}
