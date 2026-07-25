using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Default <see cref="IIngestionPipeline"/>: loaders → security validation →
/// chunking (strategy resolved by name via <see cref="ChunkingStrategyFactory"/>) →
/// embeddings (Application port <see cref="IEmbeddingProvider"/>) →
/// <see cref="IDocumentStore.UpsertAsync"/>. Incremental (RAG-03/C1): a
/// per-collection JSON manifest (<see cref="IIngestionManifestStore"/>) records
/// each source's content hash, chunker configuration, and the collection's
/// embedding profile.
/// </summary>
/// <remarks>
/// <para><b>Incremental behavior</b> (plan §6.3):</para>
/// <list type="bullet">
///   <item><description>Unchanged source (same content hash, same chunker
///   name/version/options) → skipped: no validation, no chunking, <b>zero
///   embeddings</b>.</description></item>
///   <item><description>Modified source → <see cref="IDocumentStore.DeleteBySourceAsync"/>
///   then re-ingestion of that source only.</description></item>
///   <item><description>Source absent from the request → kept as-is (no implicit
///   purge).</description></item>
///   <item><description>Embedding profile drift (provider, model, or dimensions)
///   → hard <see cref="InvalidOperationException"/>; a full rebuild happens only
///   through the explicit <see cref="IngestionRequest.Reindex"/> flag, which
///   purges every manifest-known source and rewrites the manifest.</description></item>
///   <item><description>Missing or corrupt manifest → full ingestion, never a
///   crash.</description></item>
/// </list>
/// <para>
/// Per-source and per-document failures are reported as
/// <see cref="IngestionReport.Errors"/> entries and do not abort the run; an
/// unknown chunking strategy fails the whole run loudly
/// (<see cref="RagComponentNotFoundException"/>) — never a silent fallback.
/// Documents rejected or quarantined by the <see cref="DataValidationPipeline"/>
/// (anti-injection, provenance, quarantine) never reach the store. A source that
/// did not ingest cleanly is dropped from the manifest so the next run retries it.
/// </para>
/// </remarks>
public sealed partial class DefaultIngestionPipeline : IIngestionPipeline
{
    private readonly DocumentLoaderFactory _loaderFactory;
    private readonly ChunkingStrategyFactory _chunkingFactory;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IDocumentStore _store;
    private readonly DataValidationPipeline _validation;
    private readonly IIngestionManifestStore _manifestStore;
    private readonly RagIngestionOptions _options;
    private readonly ILogger<DefaultIngestionPipeline> _logger;

    /// <summary>Initializes the ingestion pipeline.</summary>
    /// <param name="loaderFactory">Resolves the loader able to handle each source.</param>
    /// <param name="chunkingFactory">Resolves the chunking strategy by name.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed chunk contents.</param>
    /// <param name="store">Target document store.</param>
    /// <param name="validation">Ingestion-path security validation (anti-injection, provenance, quarantine).</param>
    /// <param name="manifestStore">Per-collection ingestion manifest persistence (incremental state).</param>
    /// <param name="options">Pipeline options; <c>null</c> selects the defaults.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public DefaultIngestionPipeline(
        DocumentLoaderFactory loaderFactory,
        ChunkingStrategyFactory chunkingFactory,
        IEmbeddingProvider embeddingProvider,
        IDocumentStore store,
        DataValidationPipeline validation,
        IIngestionManifestStore manifestStore,
        RagIngestionOptions? options = null,
        ILogger<DefaultIngestionPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(loaderFactory);
        ArgumentNullException.ThrowIfNull(chunkingFactory);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(manifestStore);

        _loaderFactory = loaderFactory;
        _chunkingFactory = chunkingFactory;
        _embeddingProvider = embeddingProvider;
        _store = store;
        _validation = validation;
        _manifestStore = manifestStore;
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

        var chunkerProfile = BuildChunkerProfile(strategy, request.Chunking);
        var embeddingProfile = new ManifestEmbeddingProfile
        {
            Provider = _embeddingProvider.Name,
            Model = _embeddingProvider.Model,
            Dimensions = _embeddingProvider.Dimensions,
        };

        // Missing or corrupt manifest loads as null → full ingestion, no crash.
        var manifest = await _manifestStore.LoadAsync(request.Collection, cancellationToken)
            .ConfigureAwait(false);

        EnsureNoEmbeddingDrift(request, manifest, embeddingProfile);

        var previousSources = manifest?.Sources
            ?? new Dictionary<string, ManifestSourceEntry>(StringComparer.Ordinal);

        var manifestDirty = manifest is null || request.Reindex;

        if (request.Reindex && manifest is not null)
        {
            // Explicitly consented full rebuild: purge every source the manifest
            // knows about, then re-ingest the requested sources from scratch.
            foreach (var sourceId in manifest.Sources.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _store.DeleteBySourceAsync(request.Collection, sourceId, cancellationToken)
                    .ConfigureAwait(false);
            }

            LogCollectionPurged(manifest.Sources.Count, request.Collection);
        }

        // Reindex rebuilds the manifest from this run only; otherwise entries of
        // sources absent from the request are preserved (no implicit purge).
        var entries = request.Reindex
            ? new Dictionary<string, ManifestSourceEntry>(StringComparer.Ordinal)
            : new Dictionary<string, ManifestSourceEntry>(previousSources, StringComparer.Ordinal);

        var documentsLoaded = 0;
        var chunksCreated = 0;
        var chunksEmbedded = 0;
        var chunksSkipped = 0;
        var sourcesAdded = 0;
        var sourcesUnchanged = 0;
        var sourcesReingested = 0;
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
                // Materialize the documents: hashing them is what detects change,
                // and loading is cheap compared to embedding.
                var documents = new List<RagDocument>();
                await foreach (var document in loader.LoadAsync(source, cancellationToken).ConfigureAwait(false))
                {
                    documents.Add(document);
                }

                documentsLoaded += documents.Count;

                // A descriptor may fan out to several logical sources (e.g. a
                // directory): diff each one independently, keyed by SourceId —
                // the same identity DeleteBySourceAsync operates on.
                foreach (var group in documents
                    .GroupBy(d => d.SourceId, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceId = group.Key;
                    var contentHash = ComputeContentHash(group);
                    var known = previousSources.TryGetValue(sourceId, out var previousEntry);

                    if (!request.Reindex
                        && known
                        && string.Equals(previousEntry!.ContentHash, contentHash, StringComparison.Ordinal)
                        && previousEntry.Chunker.Matches(chunkerProfile))
                    {
                        // Unchanged source: zero validation, chunking, or embedding.
                        sourcesUnchanged++;
                        LogSourceUnchanged(sourceId, request.Collection);
                        continue;
                    }

                    // From here on the source will be (re)written: drop its entry
                    // first so a mid-flight failure leaves no stale "up to date"
                    // record masking partially deleted chunks.
                    entries.Remove(sourceId);
                    manifestDirty = true;

                    if (known && !request.Reindex)
                    {
                        await _store.DeleteBySourceAsync(request.Collection, sourceId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var sourceClean = true;
                    foreach (var document in group)
                    {
                        var outcome = await IngestDocumentAsync(request, strategy, document, cancellationToken)
                            .ConfigureAwait(false);
                        chunksCreated += outcome.ChunksCreated;
                        chunksEmbedded += outcome.ChunksEmbedded;
                        chunksSkipped += outcome.ChunksSkipped;
                        if (outcome.Error is not null)
                        {
                            errors.Add(outcome.Error);
                            sourceClean = false;
                        }
                    }

                    if (known)
                        sourcesReingested++;
                    else
                        sourcesAdded++;

                    if (sourceClean)
                    {
                        entries[sourceId] = new ManifestSourceEntry
                        {
                            ContentHash = contentHash,
                            IngestedAt = DateTimeOffset.UtcNow,
                            Chunker = chunkerProfile,
                        };
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

        if (manifestDirty)
        {
            await _manifestStore.SaveAsync(
                new IngestionManifest
                {
                    Collection = request.Collection,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Embedding = embeddingProfile,
                    Sources = entries,
                },
                cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return new IngestionReport
        {
            Collection = request.Collection,
            DocumentsLoaded = documentsLoaded,
            ChunksCreated = chunksCreated,
            ChunksEmbedded = chunksEmbedded,
            ChunksSkipped = chunksSkipped,
            SourcesAdded = sourcesAdded,
            SourcesUnchanged = sourcesUnchanged,
            SourcesReingested = sourcesReingested,
            Duration = stopwatch.Elapsed,
            Errors = errors.ToImmutable(),
        };
    }

    /// <summary>
    /// Hard failure on embedding drift: vectors produced by different models are
    /// never comparable (plan §3.2), so a collection indexed with another
    /// provider/model/dimensions refuses further ingestion unless the caller
    /// explicitly consents to a full rebuild via <see cref="IngestionRequest.Reindex"/>.
    /// </summary>
    private static void EnsureNoEmbeddingDrift(
        IngestionRequest request,
        IngestionManifest? manifest,
        ManifestEmbeddingProfile current)
    {
        if (manifest is null || request.Reindex)
            return;

        var stored = manifest.Embedding;
        if (string.Equals(stored.Provider, current.Provider, StringComparison.Ordinal)
            && string.Equals(stored.Model, current.Model, StringComparison.Ordinal)
            && stored.Dimensions == current.Dimensions)
        {
            return;
        }

        throw new InvalidOperationException(
            $"RAG collection '{request.Collection}' was indexed with embedding " +
            $"'{stored.Provider}/{stored.Model}' ({stored.Dimensions.ToString(CultureInfo.InvariantCulture)} dimensions), " +
            $"but the current provider is '{current.Provider}/{current.Model}' " +
            $"({current.Dimensions.ToString(CultureInfo.InvariantCulture)} dimensions). " +
            "Vectors produced by different embedding models are not comparable, so the collection " +
            "refuses mixed ingestion. To rebuild it with the current model, re-run the ingestion " +
            "with IngestionRequest.Reindex = true (CLI: --reindex) — this purges the collection " +
            "and re-embeds every source.");
    }

    private static ManifestChunkerProfile BuildChunkerProfile(
        IChunkingStrategy strategy,
        Abstractions.Options.ChunkingOptions chunking)
    {
        var extensions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in chunking.Extensions)
            extensions[key] = value;

        return new ManifestChunkerProfile
        {
            Name = strategy.Name,
            Version = strategy.Version,
            MaxChunkSize = chunking.MaxChunkSize,
            Overlap = chunking.Overlap,
            Extensions = extensions,
        };
    }

    /// <summary>
    /// SHA-256 (lowercase hex) over the source's documents, ordered by id for
    /// determinism, with id/content framing so boundary shifts always change the hash.
    /// </summary>
    private static string ComputeContentHash(IEnumerable<RagDocument> documents)
    {
        var builder = new StringBuilder();
        foreach (var document in documents.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            builder.Append(document.Id).Append('\n')
                .Append(document.Content.Length.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append(document.Content).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Source '{SourceId}' unchanged in collection '{Collection}' — skipped (0 embeddings).")]
    private partial void LogSourceUnchanged(string sourceId, string collection);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reindex: purged {SourceCount} sources from collection '{Collection}'.")]
    private partial void LogCollectionPurged(int sourceCount, string collection);
}
