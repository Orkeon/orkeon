using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions;
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
/// <remarks>
/// <para>What a source means is decided here, and only here (GAP-27): a path relative to the
/// crew's folder (<see cref="RagCrewConfig.CrewDirectory"/>) is anchored there; a glob
/// (<c>*</c>, <c>**</c>, <c>?</c>) is expanded through the VFS by <see cref="SourceGlobExpander"/>,
/// the same semantics as <c>rag_ingest</c>, <c>orkeon rag ingest</c>, <c>rag.ingest</c> and the
/// eval harness; a directory ingests every file below it (<c>&lt;dir&gt;/**</c>); an http(s)
/// address and a plain file path reach the pipeline as written — the loaders' business, which
/// report a file that is not there. A glob or a directory that yields no file, and a relative
/// source of a crew with no folder, are load warnings naming the collection: nothing written
/// in a <c>rag:</c> block is dropped without a word.</para>
/// </remarks>
public sealed partial class RagCollectionsBootstrapper : IRagCollectionsBootstrapper
{
    /// <summary>
    /// Characters-per-token heuristic used to translate the YAML <c>max_tokens</c> chunking
    /// bound into the character-based <see cref="ChunkingOptions.MaxChunkSize"/> — the same
    /// ×4 approximation as the knowledge-context augmenter.
    /// </summary>
    private const int CharactersPerToken = 4;

    private readonly IIngestionPipeline _ingestionPipeline;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<RagCollectionsBootstrapper> _logger;

    /// <summary>Initializes a new instance of <see cref="RagCollectionsBootstrapper"/>.</summary>
    /// <param name="ingestionPipeline">The pipeline each collection is ingested through.</param>
    /// <param name="fileSystem">The VFS the sources are resolved and expanded against.</param>
    /// <param name="logger">The logger.</param>
    public RagCollectionsBootstrapper(
        IIngestionPipeline ingestionPipeline,
        IFileSystemService fileSystem,
        ILogger<RagCollectionsBootstrapper>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ingestionPipeline);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _ingestionPipeline = ingestionPipeline;
        _fileSystem = fileSystem;
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

            var sources = await ResolveSourcesAsync(
                collection, collectionConfig.Sources, config.CrewDirectory, cancellationToken).ConfigureAwait(false);
            if (sources.Count == 0)
            {
                LogCollectionWithNothingToIngest(collection);
                continue;
            }

            var request = new IngestionRequest
            {
                Collection = collection,
                Sources = sources,
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

    /// <summary>
    /// The descriptors the declared <paramref name="sources"/> stand for, in their order, each
    /// file once (see the class remarks for what each kind of source means).
    /// </summary>
    private async Task<ImmutableList<SourceDescriptor>> ResolveSourcesAsync(
        string collection, IReadOnlyList<string> sources, string? crewDirectory, CancellationToken cancellationToken)
    {
        var descriptors = ImmutableList.CreateBuilder<SourceDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string location)
        {
            if (seen.Add(location))
                descriptors.Add(new SourceDescriptor { Location = location });
        }

        foreach (var written in sources)
        {
            if (string.IsNullOrWhiteSpace(written))
                continue;

            var source = written.Trim();
            // An http(s) address is neither a pattern nor a path relative to the crew's folder:
            // it reaches the web loader as written — the expander's rule, the one every
            // ingestion surface applies (GAP-19).
            if (SourceGlobExpander.IsWebAddress(source))
            {
                Add(source);
                continue;
            }

            if (!source.StartsWith('/'))
            {
                if (crewDirectory is null)
                {
                    LogRelativeSourceWithoutCrewDirectory(collection, source);
                    continue;
                }
                source = Anchor(source, crewDirectory);
            }

            var pattern = await PatternOfAsync(source, cancellationToken).ConfigureAwait(false);
            if (pattern is null)
            {
                // A file: the loaders' business, which report it when it is not there.
                Add(source);
                continue;
            }

            foreach (var match in await ExpandAsync(collection, written, pattern, cancellationToken).ConfigureAwait(false))
                Add(match.Location);
        }

        return descriptors.ToImmutable();
    }

    /// <summary>
    /// The glob <paramref name="source"/> stands for: itself when it holds a wildcard, everything
    /// below it when it is a directory; null for a file.
    /// </summary>
    private async Task<string?> PatternOfAsync(string source, CancellationToken cancellationToken)
    {
        if (SourceGlobExpander.HasWildcard(source))
            return source;
        if (await IsDirectoryAsync(source, cancellationToken).ConfigureAwait(false))
            return source.TrimEnd('/') + "/**";
        return null;
    }

    /// <summary>
    /// The files <paramref name="pattern"/> matches through the VFS; none, said in the log, when it
    /// matches nothing or reaches outside every mount.
    /// </summary>
    private async Task<IReadOnlyList<SourceDescriptor>> ExpandAsync(
        string collection, string written, string pattern, CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceDescriptor> matches;
        try
        {
            matches = await SourceGlobExpander.ExpandAsync(_fileSystem, [pattern], cancellationToken).ConfigureAwait(false);
        }
        catch (FileAccessDeniedException ex)
        {
            // Outside every mount: the denial names the mounts there are.
            LogSourceUnreachable(collection, written, ex.Message);
            return [];
        }

        if (matches.Count == 0)
            LogSourceMatchedNothing(collection, written);
        return matches;
    }

    private async Task<bool> IsDirectoryAsync(string location, CancellationToken cancellationToken)
    {
        var probe = location.Length > 1 ? location.TrimEnd('/') : location;
        var entry = await _fileSystem.TryGetEntryAsync(probe, cancellationToken).ConfigureAwait(false);
        return entry?.Kind == VirtualEntryKind.Directory;
    }

    /// <summary><c>./data/faq.txt</c> under <c>/crew</c> is <c>/crew/data/faq.txt</c>.</summary>
    private static string Anchor(string relative, string crewDirectory)
    {
        var path = relative.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        if (path == ".")
            path = string.Empty;

        var root = crewDirectory.Length > 1 ? crewDirectory.TrimEnd('/') : crewDirectory;
        return path.Length == 0 ? root : $"{root.TrimEnd('/')}/{path}";
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG collection '{Collection}' is declared without sources — nothing to ingest.")]
    private partial void LogCollectionWithoutSources(string collection);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG collection '{Collection}': none of its sources names a file — nothing to ingest.")]
    private partial void LogCollectionWithNothingToIngest(string collection);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG collection '{Collection}': source '{Source}' matched no file — nothing ingested from it.")]
    private partial void LogSourceMatchedNothing(string collection, string source);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG collection '{Collection}': source '{Source}' is outside every mount — nothing ingested from it. {Reason}")]
    private partial void LogSourceUnreachable(string collection, string source, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RAG collection '{Collection}': source '{Source}' is relative, and this crew was not read from a folder it could be relative to — write it as a virtual path (/crew/…, /kb/…). Nothing ingested from it.")]
    private partial void LogRelativeSourceWithoutCrewDirectory(string collection, string source);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RAG collection '{Collection}' prepared: {Added} added, {Unchanged} unchanged, {Reingested} re-ingested.")]
    private partial void LogCollectionPrepared(string collection, int added, int unchanged, int reingested);
}
