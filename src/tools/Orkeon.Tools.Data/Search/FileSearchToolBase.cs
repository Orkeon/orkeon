using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Loaders;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Constants.Search;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Abstract base class for file-based semantic search tools. Thin façade over
/// the shared ephemeral-collection RAG search (RAG-03/C5): resolves the files
/// through the VFS, submits them as inline sources to
/// <see cref="IEphemeralCollectionSearch"/> (incremental ingestion — an
/// unchanged corpus is never re-embedded), and maps the scored chunks back to
/// the tool's stable response contract.
/// </summary>
public abstract partial class FileSearchToolBase<TRequest> : ToolBase<TRequest, FileSearchResponse>
    where TRequest : FileSearchRequestBase, new()
{
    private readonly IEphemeralCollectionSearch _search;
    private readonly IFileSystemService _fs;

    /// <summary>
    /// Minimum number of scored candidates requested from the store before the
    /// tool applies its own threshold + top-K cut (mirrors the legacy behavior
    /// of scoring the whole corpus before filtering).
    /// </summary>
    private const int MinCandidateCount = 50;

    /// <summary>Gets the file extensions supported by this tool (e.g., ".txt").</summary>
    protected abstract IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Gets the tool category label for logging.</summary>
    protected abstract string ToolCategory { get; }

    /// <summary>
    /// Chunking strategy name resolved by the RAG chunking factory. Defaults to
    /// the canonical recursive splitter; Markdown tools override with
    /// <c>structural</c> (heading-aware).
    /// </summary>
    protected virtual string ChunkingStrategyName => "recursive";

    /// <summary>
    /// Optional content preprocessing hook. Override to strip frontmatter, JSX, etc.
    /// </summary>
    protected virtual string PreprocessContent(string content) => content;

    /// <summary>Initializes a new instance of the file search tool façade.</summary>
    /// <param name="searchService">Shared ephemeral-collection RAG search engine.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger.</param>
    protected FileSearchToolBase(
        IEphemeralCollectionSearch searchService,
        IFileSystemService fileSystemService,
        ILogger? logger = null)
        : base(logger)
    {
        _search = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _fs = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        if (request.Path.Contains("..", StringComparison.Ordinal))
            return "Path must not contain '..' (directory traversal)";

        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        if (request.TopK <= 0 || request.TopK > FileSearchDefaults.MaxTopK)
            return $"TopK must be between 1 and {FileSearchDefaults.MaxTopK}";

        if (request.Threshold < 0.0 || request.Threshold > 1.0)
            return "Threshold must be between 0.0 and 1.0";

        if (request.ChunkSize < FileSearchDefaults.MinChunkSize || request.ChunkSize > FileSearchDefaults.MaxChunkSize)
            return $"ChunkSize must be between {FileSearchDefaults.MinChunkSize} and {FileSearchDefaults.MaxChunkSize}";

        return null;
    }

    /// <inheritdoc />
    protected override Task<FileSearchResponse> ExecuteTypedAsync(
        TRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<FileSearchResponse> ExecuteTypedCoreAsync()
        {
            // 1. Resolve files through the VFS.
            var files = await ResolveFilesAsync(request.Path, cancellationToken).ConfigureAwait(false);

            // 2. Read (and preprocess) their content — cheap compared to
            //    embedding, and needed both for change detection and for
            //    mapping chunk offsets back to approximate line numbers.
            var contents = await ReadContentsAsync(files, cancellationToken).ConfigureAwait(false);

            if (contents.Count == 0)
            {
                return new FileSearchResponse
                {
                    Results = [],
                    ResultCount = 0,
                    TotalChunks = 0,
                    FilesProcessed = files.Count,
                    Query = request.Query
                };
            }

            // 3. Delegate to the shared ephemeral-collection search: incremental
            //    ingestion (unchanged corpus = zero embeddings) + vector search.
            var outcome = await _search.SearchAsync(
                new EphemeralSearchRequest
                {
                    Sources = BuildInlineSources(contents),
                    Query = request.Query,
                    TopK = Math.Max(request.TopK, MinCandidateCount),
                    ChunkingStrategy = ChunkingStrategyName,
                    Chunking = new ChunkingOptions { MaxChunkSize = request.ChunkSize, Overlap = 0 },
                    CollectionPrefix = Name,
                },
                cancellationToken).ConfigureAwait(false);

            // 4. Apply the tool's threshold and top-K on the scored candidates.
            var threshold = Math.Clamp(request.Threshold, 0.0, 1.0);
            var topResults = outcome.Results
                .Where(s => s.Score >= threshold)
                .OrderByDescending(s => s.Score)
                .Take(request.TopK)
                .Select(s => new FileSearchResult
                {
                    Content = s.Chunk.Content,
                    Score = (float)s.Score,
                    SourceFile = s.Chunk.SourceId,
                    ApproximateLine = contents.TryGetValue(s.Chunk.SourceId, out var content)
                        ? PlainTextChunking.LineNumberAt(content, s.Chunk.StartOffset)
                        : 1,
                    ChunkIndex = s.Chunk.Index
                })
                .ToList();

            LogSearchCompleted(ToolCategory, request.Query, topResults.Count, outcome.Ingestion.ChunksCreated);

            return new FileSearchResponse
            {
                Results = topResults,
                ResultCount = topResults.Count,
                TotalChunks = outcome.Ingestion.ChunksCreated,
                FilesProcessed = files.Count,
                Query = request.Query
            };
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private Task<List<string>> ResolveFilesAsync(string path, CancellationToken cancellationToken)
        => ResolveSupportedFilesVfsAsync(_fs, path, cancellationToken);

    private async Task<Dictionary<string, string>> ReadContentsAsync(
        List<string> files, CancellationToken cancellationToken)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await _fs.TryReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            content = PreprocessContent(content);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            contents[file] = content;
        }

        return contents;
    }

    private static ImmutableList<SourceDescriptor> BuildInlineSources(
        Dictionary<string, string> contents)
    {
        return
        [
            .. contents
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new SourceDescriptor
                {
                    Location = pair.Key,
                    Kind = InlineTextLoader.TextKind,
                    Options = ImmutableDictionary<string, string>.Empty
                        .Add(InlineTextLoader.ContentOptionKey, pair.Value),
                })
        ];
    }

    private async Task<List<string>> ResolveSupportedFilesVfsAsync(
        IFileSystemService fs, string vPath, CancellationToken ct)
    {
        var entry = await fs.TryGetEntryAsync(vPath, ct).ConfigureAwait(false);
        if (entry is null)
            return [];

        if (entry.Kind == VirtualEntryKind.File)
            return HasSupportedExtension(vPath) ? [vPath] : [];

        var result = new List<string>();
        foreach (var ext in SupportedExtensions)
        {
            var opts = new VirtualEnumerationOptions(Recursive: true, SearchPattern: $"*{ext}");
            await foreach (var sub in fs.EnumerateFilesAsync(vPath, opts, ct).ConfigureAwait(false))
            {
                if (sub.Kind == VirtualEntryKind.File)
                    result.Add(sub.VirtualPath);
            }
        }
        return result;
    }

    private bool HasSupportedExtension(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath);
        return SupportedExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Category} search for '{Query}' returned {Count} results ({ChunksIndexed} chunks (re)indexed this run)")]
    private partial void LogSearchCompleted(string category, string query, int count, int chunksIndexed);
}
