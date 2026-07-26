using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Search;

/// <summary>
/// Default <see cref="IEphemeralCollectionSearch"/> (RAG-03/C5): computes a
/// deterministic collection name from the source identities and chunking
/// options, runs an incremental <see cref="IIngestionPipeline.IngestAsync"/>
/// (the per-collection manifest guarantees an unchanged corpus costs
/// <b>zero embeddings</b>), embeds the query once, and searches the collection
/// through <see cref="IDocumentStore.SearchAsync"/>.
/// </summary>
/// <remarks>
/// The collection identity deliberately excludes source <i>content</i>: a
/// modified file keeps the same collection and is re-ingested incrementally
/// (only that source is re-embedded). It includes everything that changes the
/// vector space or the chunk layout — chunking strategy, chunk size, overlap,
/// strategy extensions — plus the caller prefix and the stable identity of
/// every source (order-independent).
/// </remarks>
public sealed partial class EphemeralCollectionSearchService : IEphemeralCollectionSearch
{
    private const string CollectionNamePrefix = "eph";
    private const int CollectionHashLength = 16;

    private readonly IIngestionPipeline _ingestionPipeline;
    private readonly IDocumentStore _store;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<EphemeralCollectionSearchService> _logger;

    /// <summary>Initializes the service.</summary>
    /// <param name="ingestionPipeline">Incremental ingestion pipeline (manifest-backed).</param>
    /// <param name="store">Document store hosting the ephemeral collections.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed the query.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public EphemeralCollectionSearchService(
        IIngestionPipeline ingestionPipeline,
        IDocumentStore store,
        IEmbeddingProvider embeddingProvider,
        ILogger<EphemeralCollectionSearchService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ingestionPipeline);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddingProvider);

        _ingestionPipeline = ingestionPipeline;
        _store = store;
        _embeddingProvider = embeddingProvider;
        _logger = logger ?? NullLogger<EphemeralCollectionSearchService>.Instance;
    }

    /// <inheritdoc />
    public async Task<EphemeralSearchResult> SearchAsync(
        EphemeralSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        if (request.TopK <= 0)
        {
            throw new ArgumentException(
                $"EphemeralSearchRequest.TopK must be positive (got {request.TopK}).", nameof(request));
        }

        var collection = ComputeCollectionName(request);

        if (request.Sources.IsEmpty)
        {
            return new EphemeralSearchResult
            {
                Collection = collection,
                Results = [],
                Ingestion = new IngestionReport { Collection = collection },
            };
        }

        // Incremental ingestion: unchanged sources are skipped by the collection
        // manifest — no chunking, no embedding, no store write.
        var report = await _ingestionPipeline.IngestAsync(
            new IngestionRequest
            {
                Collection = collection,
                Sources = request.Sources,
                ChunkingStrategy = request.ChunkingStrategy,
                Chunking = request.Chunking,
            },
            cancellationToken).ConfigureAwait(false);

        var queryEmbedding = await _embeddingProvider
            .GetEmbeddingAsync(request.Query, cancellationToken)
            .ConfigureAwait(false);

        var results = await _store.SearchAsync(
            collection,
            new RetrievalQuery
            {
                Text = request.Query,
                Embedding = [.. queryEmbedding],
                TopK = request.TopK,
            },
            cancellationToken).ConfigureAwait(false);

        LogSearchCompleted(collection, results.Count, report.SourcesUnchanged, report.ChunksEmbedded);

        return new EphemeralSearchResult
        {
            Collection = collection,
            Results = results,
            Ingestion = report,
        };
    }

    /// <summary>
    /// Deterministic collection name: <c>eph-{prefix}-{hash16}</c> where the
    /// hash covers the caller prefix, the chunking configuration, and the
    /// sorted stable identities of the sources (never their content).
    /// </summary>
    private static string ComputeCollectionName(EphemeralSearchRequest request)
    {
        var builder = new StringBuilder();
        builder.Append(request.CollectionPrefix ?? string.Empty).Append('\n');
        builder.Append(request.ChunkingStrategy ?? string.Empty).Append('\n');
        builder.Append(request.Chunking.MaxChunkSize).Append('\n');
        builder.Append(request.Chunking.Overlap).Append('\n');

        foreach (var (key, value) in request.Chunking.Extensions.OrderBy(p => p.Key, StringComparer.Ordinal))
            builder.Append(key).Append('=').Append(value).Append('\n');

        var identities = request.Sources
            .Select(SourceIdentity)
            .Order(StringComparer.Ordinal);
        foreach (var identity in identities)
            builder.Append(identity).Append('\n');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        var suffix = Convert.ToHexStringLower(hash)[..CollectionHashLength];

        var prefix = SanitizePrefix(request.CollectionPrefix);
        return prefix.Length == 0
            ? $"{CollectionNamePrefix}-{suffix}"
            : $"{CollectionNamePrefix}-{prefix}-{suffix}";
    }

    /// <summary>
    /// Stable identity of a source across requests: its kind plus its
    /// <see cref="SourceDescriptor.SourceId"/> (or location). Content is
    /// deliberately excluded — change detection is the manifest's job.
    /// </summary>
    private static string SourceIdentity(SourceDescriptor source) =>
        $"{source.Kind ?? string.Empty}|{(string.IsNullOrWhiteSpace(source.SourceId) ? source.Location : source.SourceId)}";

    /// <summary>
    /// Normalizes the caller prefix into a store-safe label: lowercase
    /// alphanumerics and dashes only (collection names must not contain ':').
    /// </summary>
    private static string SanitizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return string.Empty;

        var builder = new StringBuilder(prefix.Length);
        foreach (var c in prefix.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
            else if (c is '-' or '_' or '.' or ' ')
                builder.Append('-');
            // Anything else is dropped; the hash already carries the raw prefix.
        }

        return builder.ToString().Trim('-');
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Ephemeral search on collection '{Collection}': {ResultCount} candidates ({SourcesUnchanged} sources unchanged, {ChunksEmbedded} chunks embedded).")]
    private partial void LogSearchCompleted(string collection, int resultCount, int sourcesUnchanged, int chunksEmbedded);
}
