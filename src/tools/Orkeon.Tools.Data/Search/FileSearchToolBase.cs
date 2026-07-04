using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Constants.Search;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Abstract base class for file-based semantic search tools.
/// Provides shared RAG logic: read files, chunk content, embed, and search via cosine similarity.
/// </summary>
public abstract partial class FileSearchToolBase<TRequest> : ToolBase<TRequest, FileSearchResponse>
    where TRequest : FileSearchRequestBase, new()
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IFileSystemService _fs;
    private const int MaxChunks = 200;

    /// <summary>Gets the file extensions supported by this tool (e.g., ".txt").</summary>
    protected abstract IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Gets the tool category label for logging.</summary>
    protected abstract string ToolCategory { get; }

    /// <summary>
    /// Optional content preprocessing hook. Override to strip frontmatter, JSX, etc.
    /// </summary>
    protected virtual string PreprocessContent(string content) => content;

    /// <summary>
    /// Chunks content into passages for embedding. Default splits by paragraphs, then sentences.
    /// Override to provide format-specific chunking (e.g., heading-aware for Markdown).
    /// </summary>
    protected virtual IReadOnlyList<(string Text, int ApproximateLine)> ChunkContentVirtual(string content, int maxChunkSize)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ChunkContent(content, maxChunkSize);
    }

    /// <summary>Initializes a new instance of the file search tool base with VFS support.</summary>
    protected FileSearchToolBase(IEmbeddingService embeddingService, IFileSystemService fileSystemService, ILogger? logger = null)
        : base(logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
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
        // 1. Resolve files
        var files = await ResolveFilesAsync(request.Path, cancellationToken).ConfigureAwait(false);

        // 2. Read and chunk all files
        var allChunks = await ReadAndChunkFilesAsync(files, request.ChunkSize, cancellationToken).ConfigureAwait(false);

        if (allChunks.Count == 0)
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

        // 3 & 4. Generate embeddings, compute cosine similarity and rank results
        var scored = await ScoreChunksAsync(request, allChunks, cancellationToken).ConfigureAwait(false);

        var topResults = scored
            .OrderByDescending(s => s.Score)
            .Take(request.TopK)
            .Select(s => new FileSearchResult
            {
                Content = s.Chunk.Content,
                Score = s.Score,
                SourceFile = s.Chunk.SourceFile,
                ApproximateLine = s.Chunk.ApproximateLine,
                ChunkIndex = s.Chunk.ChunkIndex
            })
            .ToList();

        LogSearchCompleted(ToolCategory, request.Query, topResults.Count, allChunks.Count);

        return new FileSearchResponse
        {
            Results = topResults,
            ResultCount = topResults.Count,
            TotalChunks = allChunks.Count,
            FilesProcessed = files.Count,
            Query = request.Query
        };
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private Task<List<string>> ResolveFilesAsync(string path, CancellationToken cancellationToken)
        => ResolveSupportedFilesVfsAsync(_fs, path, cancellationToken);

    private async Task<List<ChunkInfo>> ReadAndChunkFilesAsync(
        List<string> files, int chunkSize, CancellationToken cancellationToken)
    {
        var allChunks = new List<ChunkInfo>();
        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var content = await _fs.TryReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(content))
                continue;

            content = PreprocessContent(content);

            if (string.IsNullOrWhiteSpace(content))
                continue;

            var chunks = ChunkContentVirtual(content, chunkSize);
            for (var i = 0; i < chunks.Count; i++)
            {
                allChunks.Add(new ChunkInfo(chunks[i].Text, file, chunks[i].ApproximateLine, i));
            }

            if (allChunks.Count >= MaxChunks)
                break;
        }

        // Cap at MaxChunks
        if (allChunks.Count > MaxChunks)
            allChunks = allChunks.Take(MaxChunks).ToList();

        return allChunks;
    }

    private async Task<List<(ChunkInfo Chunk, float Score)>> ScoreChunksAsync(
        TRequest request, List<ChunkInfo> allChunks, CancellationToken cancellationToken)
    {
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);

        var chunkEmbeddings = new float[allChunks.Count][];
        for (var i = 0; i < allChunks.Count; i++)
        {
            chunkEmbeddings[i] = await _embeddingService.GetEmbeddingAsync(allChunks[i].Content, cancellationToken).ConfigureAwait(false);
        }

        var threshold = (float)Math.Clamp(request.Threshold, 0.0, 1.0);
        var scored = new List<(ChunkInfo Chunk, float Score)>();

        for (var i = 0; i < allChunks.Count; i++)
        {
            var score = CosineSimilarity(queryEmbedding, chunkEmbeddings[i]);
            if (score >= threshold)
            {
                scored.Add((allChunks[i], score));
            }
        }

        return scored;
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

    /// <summary>
    /// Chunks content by paragraphs (split on double newlines), then re-splits long paragraphs by sentences.
    /// </summary>
    internal static List<(string Text, int ApproximateLine)> ChunkContent(string content, int maxChunkSize)
    {
        var paragraphs = content.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<(string Text, int ApproximateLine)>();
        var currentLine = 1;

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                currentLine += CountLines(paragraph) + 2; // +2 for the double newline separator
                continue;
            }

            if (trimmed.Length <= maxChunkSize)
            {
                chunks.Add((trimmed, currentLine));
            }
            else
            {
                AppendSentenceChunks(chunks, trimmed, maxChunkSize, currentLine);
            }

            currentLine += CountLines(paragraph) + 2;
        }

        return chunks;
    }

    private static void AppendSentenceChunks(
        List<(string Text, int ApproximateLine)> chunks, string paragraph, int maxChunkSize, int currentLine)
    {
        var sentences = SplitBySentences(paragraph);
        var buffer = new StringBuilder();
        var chunkStartLine = currentLine;

        foreach (var sentence in sentences)
        {
            if (buffer.Length + sentence.Length > maxChunkSize && buffer.Length > 0)
            {
                chunks.Add((buffer.ToString().Trim(), chunkStartLine));
                buffer.Clear();
                chunkStartLine = currentLine + CountLines(buffer.ToString());
            }
            buffer.Append(sentence);
        }

        if (buffer.Length > 0)
        {
            chunks.Add((buffer.ToString().Trim(), chunkStartLine));
        }
    }

    private static string[] SplitBySentences(string text)
    {
        // Split on sentence boundaries while keeping the delimiter
        var result = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                (i + 1 >= text.Length || text[i + 1] == ' ' || text[i + 1] == '\n'))
            {
                result.Add(text.Substring(start, i - start + 1));
                start = i + 1;
            }
        }

        if (start < text.Length)
            result.Add(text[start..]);

        return result.ToArray();
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 1;
        foreach (var c in text)
        {
            if (c == '\n') count++;
        }
        return count;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;

        float dotProduct = 0f, magnitudeA = 0f, magnitudeB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var denominator = (float)(Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        return denominator == 0 ? 0f : dotProduct / denominator;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Category} search for '{Query}' returned {Count} results from {TotalChunks} chunks")]
    private partial void LogSearchCompleted(string category, string query, int count, int totalChunks);

    private sealed record ChunkInfo(string Content, string SourceFile, int ApproximateLine, int ChunkIndex);
}
