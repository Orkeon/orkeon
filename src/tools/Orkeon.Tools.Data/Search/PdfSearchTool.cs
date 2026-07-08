using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Tools.Abstractions.Base;
using UglyToad.PdfPig;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Performs semantic search within PDF documents using RAG embeddings.
/// Supports single PDF files and directories of PDFs, with optional page range filtering.
/// Uses PdfPig for text extraction and cosine similarity for ranking.
/// </summary>
[ToolContract("pdf_search",
    Name = "pdf_search",
    Description = "Perform semantic search within PDF documents using RAG embeddings.",
    Category = "Search")]
public partial class PdfSearchTool : ToolBase<PdfSearchRequest, PdfSearchResponse>
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IFileSystemService _fs;

    /// <summary>
    /// Initializes a new instance of <see cref="PdfSearchTool"/> with VFS support.
    /// </summary>
    /// <param name="embeddingService">Embedding service for generating vector representations.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger instance.</param>
    public PdfSearchTool(IEmbeddingService embeddingService, IFileSystemService fileSystemService, ILogger<PdfSearchTool>? logger = null)
        : base(logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _fs = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(PdfSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        if (request.TopK <= 0 || request.TopK > 100)
            return "TopK must be between 1 and 100";

        if (request.Threshold < 0.0 || request.Threshold > 1.0)
            return "Threshold must be between 0.0 and 1.0";

        if (request.ChunkSize < 100 || request.ChunkSize > 5000)
            return "ChunkSize must be between 100 and 5000";

        if (!string.IsNullOrWhiteSpace(request.PageRange) && !IsValidPageRange(request.PageRange))
            return $"Invalid page range format: {request.PageRange}";

        return null;
    }

    /// <inheritdoc />
    protected override Task<PdfSearchResponse> ExecuteTypedAsync(
        PdfSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<PdfSearchResponse> ExecuteTypedCoreAsync()
        {
            // Step 1: Resolve PDF files
            var pdfFiles = await ResolvePdfFilesVfsAsync(_fs, request.Path, cancellationToken).ConfigureAwait(false);
            if (pdfFiles.Count == 0)
                return EmptyResponse(request.Query, totalPages: 0, filesProcessed: 0);

            // Step 2: Extract and chunk text from all PDFs
            var (allChunks, totalPages) = await ExtractAllChunksAsync(pdfFiles, request, cancellationToken).ConfigureAwait(false);
            if (allChunks.Count == 0)
                return EmptyResponse(request.Query, totalPages, pdfFiles.Count);

            // Steps 3 & 4: Embed query + chunks, then compute cosine similarity and rank
            var scoredResults = await ScoreChunksAsync(allChunks, request, cancellationToken).ConfigureAwait(false);

            // Step 5: Sort by score descending, take top-K
            var topResults = scoredResults
                .OrderByDescending(r => r.Score)
                .Take(request.TopK)
                .Select(r => new PdfSearchResult
                {
                    Content = r.Chunk.Text,
                    Score = r.Score,
                    SourceFile = r.Chunk.SourceFile,
                    PageNumber = r.Chunk.PageNumber,
                    ChunkIndex = r.Chunk.ChunkIndex
                })
                .ToList();

            LogSearchCompleted(request.Query, topResults.Count, allChunks.Count, pdfFiles.Count);

            return new PdfSearchResponse
            {
                Results = topResults,
                ResultCount = topResults.Count,
                TotalChunks = allChunks.Count,
                TotalPages = totalPages,
                FilesProcessed = pdfFiles.Count,
                Query = request.Query
            };
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static PdfSearchResponse EmptyResponse(string query, int totalPages, int filesProcessed)
        => new()
        {
            Query = query,
            Results = [],
            ResultCount = 0,
            TotalChunks = 0,
            TotalPages = totalPages,
            FilesProcessed = filesProcessed
        };

    private async Task<(List<PdfChunk> Chunks, int TotalPages)> ExtractAllChunksAsync(
        List<string> pdfFiles, PdfSearchRequest request, CancellationToken cancellationToken)
    {
        var allChunks = new List<PdfChunk>();
        var totalPages = 0;

        foreach (var pdfFile in pdfFiles)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Read the PDF bytes through the virtual file system, which resolves the virtual
            // path (e.g. /data/x.pdf) to its physical mount location, then parse from memory.
            var bytes = await _fs.TryReadAllBytesAsync(pdfFile, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {pdfFile}");
            var (chunks, pageCount) = ExtractChunks(bytes, pdfFile, request.PageRange, request.ChunkSize);
            allChunks.AddRange(chunks);
            totalPages += pageCount;
        }

        return (allChunks, totalPages);
    }

    private async Task<List<(PdfChunk Chunk, float Score)>> ScoreChunksAsync(
        List<PdfChunk> allChunks, PdfSearchRequest request, CancellationToken cancellationToken)
    {
        // Step 3: Generate embeddings for query and all chunks
        var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);

        var chunkEmbeddings = new float[allChunks.Count][];
        for (var i = 0; i < allChunks.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            chunkEmbeddings[i] = await _embeddingService.GetEmbeddingAsync(allChunks[i].Text, cancellationToken).ConfigureAwait(false);
        }

        // Step 4: Compute cosine similarity and rank
        var scoredResults = new List<(PdfChunk Chunk, float Score)>();
        for (var i = 0; i < allChunks.Count; i++)
        {
            var score = CosineSimilarity(queryEmbedding, chunkEmbeddings[i]);
            if (score >= request.Threshold)
            {
                scoredResults.Add((allChunks[i], score));
            }
        }

        return scoredResults;
    }

    private static async Task<List<string>> ResolvePdfFilesVfsAsync(
        IFileSystemService fs, string vPath, CancellationToken ct)
    {
        var entry = await fs.TryGetEntryAsync(vPath, ct).ConfigureAwait(false);
        if (entry is null)
            return [];

        if (entry.Kind == VirtualEntryKind.File)
            return vPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? [vPath] : [];

        var opts = new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*.pdf");
        var result = new List<string>();
        await foreach (var sub in fs.EnumerateFilesAsync(vPath, opts, ct).ConfigureAwait(false))
        {
            if (sub.Kind == VirtualEntryKind.File)
                result.Add(sub.VirtualPath);
        }
        result.Sort();
        return result;
    }

    private static (List<PdfChunk> Chunks, int PageCount) ExtractChunks(
        byte[] bytes, string sourceLabel, string? pageRange, int chunkSize)
    {
        using var document = PdfDocument.Open(bytes);
        return ExtractChunksFromDocument(document, sourceLabel, pageRange, chunkSize);
    }

    private static (List<PdfChunk> Chunks, int PageCount) ExtractChunksFromDocument(
        PdfDocument document, string sourceLabel, string? pageRange, int chunkSize)
    {
        var chunks = new List<PdfChunk>();

        var totalPages = document.NumberOfPages;
        var pageIndices = ParsePageRange(pageRange, totalPages);

        foreach (var pageIndex in pageIndices)
        {
            var page = document.GetPage(pageIndex);
            var pageText = page.Text ?? "";

            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            var pageChunks = ChunkText(pageText, chunkSize);
            for (var i = 0; i < pageChunks.Count; i++)
            {
                chunks.Add(new PdfChunk
                {
                    Text = pageChunks[i],
                    SourceFile = sourceLabel,
                    PageNumber = pageIndex,
                    ChunkIndex = i
                });
            }
        }

        return (chunks, pageIndices.Count);
    }

    /// <summary>
    /// Splits text into chunks of approximately the given size, splitting on paragraph
    /// boundaries first, then sentence boundaries if a paragraph exceeds the chunk size.
    /// </summary>
    internal static List<string> ChunkText(string text, int chunkSize)
    {
        var chunks = new List<string>();

        // Split into paragraphs on double-newline or single-newline
        var paragraphs = text.Split(["\r\n\r\n", "\n\n", "\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var currentChunk = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > chunkSize)
            {
                FlushChunk(chunks, currentChunk);
                AppendSentenceChunks(chunks, paragraph, chunkSize);
            }
            else if (currentChunk.Length + paragraph.Length + 1 > chunkSize && currentChunk.Length > 0)
            {
                FlushChunk(chunks, currentChunk);
                currentChunk.Append(paragraph);
            }
            else
            {
                if (currentChunk.Length > 0)
                    currentChunk.Append(' ');
                currentChunk.Append(paragraph);
            }
        }

        FlushChunk(chunks, currentChunk);

        return chunks;
    }

    private static void FlushChunk(List<string> chunks, StringBuilder buffer)
    {
        if (buffer.Length == 0)
            return;

        chunks.Add(buffer.ToString().Trim());
        buffer.Clear();
    }

    private static void AppendSentenceChunks(List<string> chunks, string paragraph, int chunkSize)
    {
        var sentences = SplitIntoSentences(paragraph);
        var sentenceChunk = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (sentenceChunk.Length + sentence.Length + 1 > chunkSize && sentenceChunk.Length > 0)
            {
                FlushChunk(chunks, sentenceChunk);
            }
            if (sentenceChunk.Length > 0)
                sentenceChunk.Append(' ');
            sentenceChunk.Append(sentence);
        }
        FlushChunk(chunks, sentenceChunk);
    }

    private static List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting on . ! ? followed by space or end
        var parts = Regex.Split(text, @"(?<=[.!?])\s+", RegexOptions.None, TimeSpan.FromSeconds(5));
        return parts
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Parses a page range string into a list of 1-based page numbers.
    /// Supports formats: "1-5", "1,3,5", "2-" (from page 2 to end).
    /// </summary>
    internal static List<int> ParsePageRange(string? pageRange, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(pageRange))
            return Enumerable.Range(1, totalPages).ToList();

        var pages = new HashSet<int>();

        foreach (var part in pageRange.Split(','))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            AddPagesForPart(pages, trimmed, totalPages);
        }

        return pages.OrderBy(p => p).ToList();
    }

    private static void AddPagesForPart(HashSet<int> pages, string part, int totalPages)
    {
        if (part.Contains('-', StringComparison.Ordinal))
        {
            var rangeParts = part.Split('-');
            var start = string.IsNullOrEmpty(rangeParts[0]) ? 1 : int.Parse(rangeParts[0], CultureInfo.InvariantCulture);
            var end = string.IsNullOrEmpty(rangeParts[1]) ? totalPages : int.Parse(rangeParts[1], CultureInfo.InvariantCulture);

            start = Math.Max(1, Math.Min(start, totalPages));
            end = Math.Max(1, Math.Min(end, totalPages));

            for (int i = start; i <= end; i++)
                pages.Add(i);
        }
        else if (int.TryParse(part, out var pageNum) && pageNum >= 1 && pageNum <= totalPages)
        {
            pages.Add(pageNum);
        }
    }

    private static bool IsValidPageRange(string pageRange)
    {
        // Accept formats: "1-5", "1,3,5", "2-", "-5", "1-5,7,9-11"
        return Regex.IsMatch(pageRange.Trim(), @"^\d*-?\d*(,\s*\d*-?\d*)*$", RegexOptions.None, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Computes cosine similarity between two embedding vectors.
    /// </summary>
    internal static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0f ? 0f : dot / denominator;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "PDF search completed: query='{Query}', results={ResultCount}, chunks={ChunkCount}, files={FileCount}")]
    private partial void LogSearchCompleted(string query, int resultCount, int chunkCount, int fileCount);

    // ── Internal chunk model ─────────────────────────────────────────

    private sealed class PdfChunk
    {
        public string Text { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public int PageNumber { get; init; }
        public int ChunkIndex { get; init; }
    }
}
