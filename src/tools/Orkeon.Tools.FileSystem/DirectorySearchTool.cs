using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Loaders;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.FileSystem;

/// <summary>
/// Tool for semantic search across all files in a directory. Thin façade over
/// the shared ephemeral-collection RAG search (RAG-03/C5): candidate files are
/// resolved through the VFS (patterns, size and binary filters), ingested
/// incrementally as inline sources (unchanged corpus = zero embeddings), and
/// queried through the document store.
/// </summary>
[ToolContract("directory_search",
    Name = "directory_search",
    Description = "Perform semantic search across all files in a directory using RAG embeddings. Finds the most relevant file content matching a query.",
    Category = "File System")]
public partial class DirectorySearchTool : ToolBase<DirectorySearchRequest, DirectorySearchResponse>
{
    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly IEphemeralCollectionSearch _search;
    private readonly IFileSystemService _fileSystemService;

    private const int MaxFiles = 200;
    private const int BinaryDetectionBufferSize = 8192;

    /// <summary>
    /// Minimum number of scored candidates requested from the store before the
    /// tool applies its own threshold + top-K cut.
    /// </summary>
    private const int MinCandidateCount = 50;

    /// <summary>Initializes a new instance of <see cref="DirectorySearchTool"/>.</summary>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="searchService">Shared ephemeral-collection RAG search engine.</param>
    /// <param name="logger">Optional logger.</param>
    public DirectorySearchTool(
        IFileSystemService fileSystemService,
        IEphemeralCollectionSearch searchService,
        ILogger<DirectorySearchTool>? logger = null)
        : base(logger)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _search = searchService ?? throw new ArgumentNullException(nameof(searchService));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DirectorySearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        var pathError = ValidatePath(request.Path);
        if (pathError is not null)
            return pathError;

        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        if (request.TopK <= 0 || request.TopK > 100)
            return "TopK must be between 1 and 100";

        if (request.Threshold < 0.0 || request.Threshold > 1.0)
            return "Threshold must be between 0.0 and 1.0";

        if (request.ChunkSize < 100 || request.ChunkSize > 5000)
            return "ChunkSize must be between 100 and 5000";

        if (request.MaxFileSizeKb <= 0 || request.MaxFileSizeKb > 10240)
            return "MaxFileSizeKb must be between 1 and 10240";

        return null;
    }

    private string? ValidatePath(string path)
    {
        var pathResult = _fileSystemService.ResolveAndValidate(path, FileAccessRights.Read);
        return pathResult.IsAllowed ? null : pathResult.DenialReason ?? "Invalid or unsafe path";
    }

    /// <inheritdoc />
    protected override Task<DirectorySearchResponse> ExecuteTypedAsync(
        DirectorySearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<DirectorySearchResponse> ExecuteTypedCoreAsync()
        {
            // 1. Resolve files (capped at MaxFiles)
            var allEntries = await ResolveEntriesAsync(request, cancellationToken).ConfigureAwait(false);
            if (allEntries.Count > MaxFiles)
                allEntries = allEntries.Take(MaxFiles).ToList();

            // 2. Read the eligible files (size/binary/read filters)
            var (contents, filesProcessed, filesSkipped) =
                await ReadContentsAsync(request, allEntries, cancellationToken).ConfigureAwait(false);

            if (contents.Count == 0)
            {
                return new DirectorySearchResponse
                {
                    Results = [],
                    ResultCount = 0,
                    TotalChunks = 0,
                    FilesProcessed = filesProcessed,
                    FilesSkipped = filesSkipped,
                    Query = request.Query
                };
            }

            // 3. Ephemeral-collection search: incremental ingestion (unchanged
            //    corpus = zero embeddings) + vector search over the collection.
            var outcome = await _search.SearchAsync(
                new EphemeralSearchRequest
                {
                    Sources = BuildInlineSources(contents),
                    Query = request.Query,
                    TopK = Math.Max(request.TopK, MinCandidateCount),
                    ChunkingStrategy = "recursive",
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
                .Select(s => new DirectorySearchResult
                {
                    Content = s.Chunk.Content,
                    Score = (float)s.Score,
                    SourceFile = s.Chunk.SourceId,
                    ApproximateLine = contents.TryGetValue(s.Chunk.SourceId, out var content)
                        ? PlainTextChunking.LineNumberAt(content, s.Chunk.StartOffset)
                        : 1,
                    ChunkIndex = s.Chunk.Index,
                    FileExtension = Path.GetExtension(s.Chunk.SourceId)
                })
                .ToList();

            LogSearchCompleted(request.Query, topResults.Count, outcome.Ingestion.ChunksCreated, filesProcessed);

            return new DirectorySearchResponse
            {
                Results = topResults,
                ResultCount = topResults.Count,
                TotalChunks = outcome.Ingestion.ChunksCreated,
                FilesProcessed = filesProcessed,
                FilesSkipped = filesSkipped,
                Query = request.Query
            };
        }
    }

    private async Task<List<VirtualFileEntry>> ResolveEntriesAsync(
        DirectorySearchRequest request, CancellationToken cancellationToken)
    {
        if (!await _fileSystemService.ExistsAsync(request.Path, cancellationToken).ConfigureAwait(false))
            throw new DirectoryNotFoundException($"Directory not found: {request.Path}");

        return await ResolveFilesVfsAsync(
            _fileSystemService, request.Path, request.FilePatterns, request.Recursive, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(Dictionary<string, string> Contents, int FilesProcessed, int FilesSkipped)> ReadContentsAsync(
        DirectorySearchRequest request, List<VirtualFileEntry> allEntries, CancellationToken cancellationToken)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        int filesProcessed = 0;
        int filesSkipped = 0;

        foreach (var entry in allEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await TryReadTextAsync(entry, request.MaxFileSizeKb, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                filesSkipped++;
                continue;
            }

            contents[entry.VirtualPath] = content;
            filesProcessed++;
        }

        return (contents, filesProcessed, filesSkipped);
    }

    private static ImmutableList<SourceDescriptor> BuildInlineSources(Dictionary<string, string> contents)
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

    /// <summary>
    /// Reads a candidate file's text, returning <c>null</c> when it should be skipped
    /// (oversized, binary, unreadable, or empty).
    /// </summary>
    private async Task<string?> TryReadTextAsync(
        VirtualFileEntry entry, int maxFileSizeKb, CancellationToken cancellationToken)
    {
        if (entry.SizeBytes > maxFileSizeKb * 1024L)
            return null;

        if (await IsBinaryFileVfsAsync(entry.VirtualPath, cancellationToken).ConfigureAwait(false))
            return null;

        string? content;
        try
        {
            content = await _fileSystemService.TryReadAllTextAsync(entry.VirtualPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    // -- File resolution ──────────────────────────────────────────────────

    private static async Task<List<VirtualFileEntry>> ResolveFilesVfsAsync(
        IFileSystemService fs, string virtualRoot, string filePatterns, bool recursive, CancellationToken ct)
    {
        var patterns = filePatterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0)
            patterns = ["*"];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<VirtualFileEntry>();

        foreach (var pattern in patterns)
        {
            var opts = new VirtualEnumerationOptions(Recursive: recursive, SearchPattern: pattern);
            await foreach (var entry in fs.EnumerateFilesAsync(virtualRoot, opts, ct).ConfigureAwait(false))
            {
                if (entry.Kind != VirtualEntryKind.File) continue;
                if (seen.Add(entry.VirtualPath))
                    results.Add(entry);
            }
        }

        results.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.VirtualPath, b.VirtualPath));
        return results;
    }

    // -- Binary detection ─────────────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort binary-detection probe: any failure opening or reading the file is treated as 'binary' (returns true) so an unreadable file is excluded from the text search rather than aborting the directory scan.")]
    private async Task<bool> IsBinaryFileVfsAsync(string virtualPath, CancellationToken ct)
    {
        try
        {
            var stream = await OpenReadStreamAsync(virtualPath, ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var buffer = new byte[BinaryDetectionBufferSize];
                var bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0x00)
                        return true;
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the opened stream is transferred to the caller (IsBinaryFileVfsAsync), which disposes it via an 'await using' block. This factory only opens and returns the stream.")]
    private async Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
        => await _fileSystemService.OpenReadStreamAsync(virtualPath, ct).ConfigureAwait(false);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Directory search for '{Query}' returned {ResultCount} results ({ChunksIndexed} chunks (re)indexed) across {FilesProcessed} files")]
    private partial void LogSearchCompleted(string query, int resultCount, int chunksIndexed, int filesProcessed);
}
