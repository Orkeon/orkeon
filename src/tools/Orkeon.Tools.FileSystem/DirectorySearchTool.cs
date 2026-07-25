using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Chunking;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.FileSystem;

/// <summary>
/// Tool for semantic search across all files in a directory using RAG embeddings.
/// Reads files, chunks them into paragraphs, generates embeddings, and performs
/// cosine similarity search to find the most relevant content.
/// </summary>
[ToolContract("directory_search",
    Name = "directory_search",
    Description = "Perform semantic search across all files in a directory using RAG embeddings. Finds the most relevant file content matching a query.",
    Category = "File System")]
public partial class DirectorySearchTool : ToolBase<DirectorySearchRequest, DirectorySearchResponse>
{
    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly IEmbeddingService _embeddingService;
    private readonly IFileSystemService _fileSystemService;

    private const int MaxFiles = 200;
    private const int MaxChunks = 500;
    private const int BinaryDetectionBufferSize = 8192;

    /// <summary>Initializes a new instance of <see cref="DirectorySearchTool"/>.</summary>
    public DirectorySearchTool(
        IFileSystemService fileSystemService,
        IEmbeddingService embeddingService,
        ILogger<DirectorySearchTool>? logger = null)
        : base(logger)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
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

            // 2. Read and chunk files
            var chunked = await BuildChunksAsync(request, allEntries, cancellationToken).ConfigureAwait(false);

            if (chunked.Chunks.Count == 0)
            {
                return new DirectorySearchResponse
                {
                    Results = [],
                    ResultCount = 0,
                    TotalChunks = 0,
                    FilesProcessed = chunked.FilesProcessed,
                    FilesSkipped = chunked.FilesSkipped,
                    Query = request.Query
                };
            }

            // 3. Score chunks against the query and take top K
            var topResults = await ScoreAndRankAsync(request, chunked.Chunks, cancellationToken).ConfigureAwait(false);

            LogSearchCompleted(request.Query, topResults.Count, chunked.Chunks.Count, chunked.FilesProcessed);

            return new DirectorySearchResponse
            {
                Results = topResults,
                ResultCount = topResults.Count,
                TotalChunks = chunked.Chunks.Count,
                FilesProcessed = chunked.FilesProcessed,
                FilesSkipped = chunked.FilesSkipped,
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

    private async Task<ChunkedFiles> BuildChunksAsync(
        DirectorySearchRequest request, List<VirtualFileEntry> allEntries, CancellationToken cancellationToken)
    {
        var chunks = new List<ChunkInfo>();
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

            var fileExtension = Path.GetExtension(entry.VirtualPath);
            chunks.AddRange(ChunkText(content, request.ChunkSize, entry.VirtualPath, fileExtension));
            filesProcessed++;

            // Cap total chunks
            if (chunks.Count >= MaxChunks)
            {
                chunks = chunks.Take(MaxChunks).ToList();
                break;
            }
        }

        return new ChunkedFiles(chunks, filesProcessed, filesSkipped);
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

    private async Task<List<DirectorySearchResult>> ScoreAndRankAsync(
        DirectorySearchRequest request, List<ChunkInfo> chunks, CancellationToken cancellationToken)
    {
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);
        var queryVector = new EmbeddingVector(queryEmbedding);

        var scoredResults = new List<(ChunkInfo Chunk, float Score)>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkEmbedding = await _embeddingService.GetEmbeddingAsync(chunk.Content, cancellationToken).ConfigureAwait(false);
            var chunkVector = new EmbeddingVector(chunkEmbedding);

            var similarity = chunkVector.CosineSimilarity(queryVector);

            if (similarity >= (float)request.Threshold)
                scoredResults.Add((chunk, similarity));
        }

        return scoredResults
            .OrderByDescending(r => r.Score)
            .Take(request.TopK)
            .Select(r => new DirectorySearchResult
            {
                Content = r.Chunk.Content,
                Score = r.Score,
                SourceFile = r.Chunk.SourceFile,
                ApproximateLine = r.Chunk.ApproximateLine,
                ChunkIndex = r.Chunk.ChunkIndex,
                FileExtension = r.Chunk.FileExtension
            })
            .ToList();
    }

    /// <summary>Aggregates the chunking pass result over a set of candidate files.</summary>
    private sealed record ChunkedFiles(List<ChunkInfo> Chunks, int FilesProcessed, int FilesSkipped);

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

    // -- Text chunking ────────────────────────────────────────────────────

    /// <summary>
    /// Chunks file content with the canonical RAG recursive strategy (RAG-02/C3
    /// consolidation), mapping chunk offsets back to 1-based approximate source lines.
    /// </summary>
    internal static List<ChunkInfo> ChunkText(
        string content, int maxChunkSize, string sourceFile, string fileExtension)
    {
        var chunks = new RecursiveChunkingStrategy().ChunkText(content, maxChunkSize, overlap: 0, sourceId: sourceFile);

        return [.. chunks.Select((c, i) => new ChunkInfo(
            c.Content,
            sourceFile,
            PlainTextChunking.LineNumberAt(content, c.StartOffset),
            i,
            fileExtension))];
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Directory search for '{Query}' returned {ResultCount} results from {TotalChunks} chunks across {FilesProcessed} files")]
    private partial void LogSearchCompleted(string query, int resultCount, int totalChunks, int filesProcessed);

    /// <summary>Internal record for tracking chunk metadata during processing.</summary>
    internal sealed record ChunkInfo(
        string Content,
        string SourceFile,
        int ApproximateLine,
        int ChunkIndex,
        string FileExtension);
}
