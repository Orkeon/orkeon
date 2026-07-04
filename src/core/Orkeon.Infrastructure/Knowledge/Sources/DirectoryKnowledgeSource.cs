using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Knowledge;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Knowledge.Sources;

/// <summary>
/// Knowledge source backed by a virtual directory of files.
/// Loads all supported files using IDocumentLoaderFactory and chunks them using ITextChunker.
/// </summary>
public partial class DirectoryKnowledgeSource : IKnowledgeSource
{
    private readonly string _vPath;
    private readonly IFileSystemService _fs;
    private readonly IDocumentLoaderFactory _loaderFactory;
    private readonly ITextChunker _chunker;
    private readonly ChunkingOptions? _chunkingOptions;
    private readonly string? _searchPattern;
    private readonly ILogger<DirectoryKnowledgeSource> _logger;
    private List<KnowledgeContent>? _chunks;

    /// <summary>Initializes a new instance of <see cref="DirectoryKnowledgeSource"/>.</summary>
    /// <param name="vPath">The virtual path to the directory containing files to load.</param>
    /// <param name="fs">The virtual file system service.</param>
    /// <param name="loaderFactory">Factory for obtaining document loaders by file type.</param>
    /// <param name="chunker">The text chunker used to split content into chunks.</param>
    /// <param name="chunkingOptions">Optional chunking configuration options.</param>
    /// <param name="searchPattern">Optional glob pattern to filter files (default: all files).</param>
    /// <param name="logger">Optional logger used to trace files that cannot be loaded.</param>
    public DirectoryKnowledgeSource(
        string vPath,
        IFileSystemService fs,
        IDocumentLoaderFactory loaderFactory,
        ITextChunker chunker,
        ChunkingOptions? chunkingOptions = null,
        string? searchPattern = null,
        ILogger<DirectoryKnowledgeSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(vPath);
        _vPath = vPath;
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
        ArgumentNullException.ThrowIfNull(loaderFactory);
        _loaderFactory = loaderFactory;
        ArgumentNullException.ThrowIfNull(chunker);
        _chunker = chunker;
        _chunkingOptions = chunkingOptions;
        _searchPattern = searchPattern;
        _logger = logger ?? NullLogger<DirectoryKnowledgeSource>.Instance;
    }

    private readonly KnowledgeSourceId _id = KnowledgeSourceId.Create();

    /// <inheritdoc />
    public KnowledgeSourceId Id => _id;

    /// <inheritdoc />
    public string Name
    {
        get
        {
            var last = _vPath.TrimEnd('/').Split('/').LastOrDefault();
            return string.IsNullOrEmpty(last) ? "root" : last;
        }
    }

    /// <inheritdoc />
    public string Type => "directory";

    /// <inheritdoc />
    public async Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var combinedContent = string.Join("\n\n", _chunks!.Select(c => c.Content));

        return new KnowledgeContent
        {
            Id = KnowledgeContentId.Create(),
            Title = Name,
            Content = combinedContent,
            Source = _vPath,
            Metadata = new Dictionary<string, object>
            {
                ["chunk_count"] = _chunks!.Count,
                ["source_type"] = Type,
                ["directory_path"] = _vPath
            }
        };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<KnowledgeContent>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(query))
            return _chunks!.Take(limit);

        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var scored = _chunks!
            .Select(chunk =>
            {
                var content = chunk.Content;
                int matchCount = queryTerms.Count(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));

                double relevance = queryTerms.Length > 0
                    ? (double)matchCount / queryTerms.Length
                    : 0.0;

                return (Chunk: chunk, Relevance: relevance);
            })
            .Where(x => x.Relevance > 0)
            .OrderByDescending(x => x.Relevance)
            .Take(limit)
            .Select(x => x.Chunk with { Relevance = x.Relevance });

        return scored;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-file fault barrier: a file that fails to load is logged and skipped so the rest of the directory still feeds the knowledge base.")]
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_chunks != null)
            return;

        if (!await _fs.ExistsAsync(_vPath, cancellationToken).ConfigureAwait(false))
            throw new DirectoryNotFoundException($"Directory not found: {_vPath}");

        _chunks = [];

        var opts = new VirtualEnumerationOptions(Recursive: true, SearchPattern: _searchPattern);
        await foreach (var entry in _fs.EnumerateFilesAsync(_vPath, opts, cancellationToken).ConfigureAwait(false))
        {
            if (entry.Kind != VirtualEntryKind.File)
                continue;

            var loader = _loaderFactory.GetLoaderForSource(entry.VirtualPath);
            if (loader == null)
                continue;

            try
            {
                var document = await loader.LoadAsync(entry.VirtualPath, cancellationToken).ConfigureAwait(false);
                var textChunks = _chunker.Chunk(document.Content, _chunkingOptions);
                var fileName = entry.VirtualPath.TrimEnd('/').Split('/').LastOrDefault() ?? entry.VirtualPath;

                for (int i = 0; i < textChunks.Count; i++)
                {
                    var chunk = textChunks[i];
                    _chunks.Add(new KnowledgeContent
                    {
                        Id = KnowledgeContentId.Create(),
                        Title = $"{fileName} (chunk {i + 1}/{textChunks.Count})",
                        Content = chunk.Content,
                        Source = entry.VirtualPath,
                        Metadata = new Dictionary<string, object>(document.Metadata)
                        {
                            ["chunk_index"] = i,
                            ["chunk_start"] = chunk.StartIndex,
                            ["chunk_end"] = chunk.EndIndex,
                            ["total_chunks"] = textChunks.Count,
                            ["parent_directory"] = _vPath
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Best-effort: skip files that can't be loaded so the rest of the
                // directory still feeds the RAG base, but trace it so a silently
                // missing knowledge file is diagnosable.
                LogFileLoadFailed(ex, entry.VirtualPath);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to load knowledge file '{VirtualPath}'; it will be excluded from the knowledge base.")]
    private partial void LogFileLoadFailed(Exception ex, string virtualPath);
}
