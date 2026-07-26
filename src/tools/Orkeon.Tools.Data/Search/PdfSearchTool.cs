using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Loaders;
using Orkeon.Tools.Abstractions.Base;
using UglyToad.PdfPig;

namespace Orkeon.Tools.Data.Search;

/// <summary>
/// Performs semantic search within PDF documents. Thin façade over the shared
/// ephemeral-collection RAG search (RAG-03/C5): pages are extracted with PdfPig
/// and ingested incrementally as per-page inline sources (unchanged corpus =
/// zero embeddings), then queried through the document store. Supports single
/// PDF files and directories of PDFs, with optional page range filtering.
/// </summary>
[ToolContract("pdf_search",
    Name = "pdf_search",
    Description = "Perform semantic search within PDF documents using RAG embeddings.",
    Category = "Search")]
public partial class PdfSearchTool : ToolBase<PdfSearchRequest, PdfSearchResponse>
{
    private readonly IEphemeralCollectionSearch _search;
    private readonly IFileSystemService _fs;

    /// <summary>Chunk metadata key carrying the 1-based page number of a chunk.</summary>
    private const string PageNumberMetadataKey = "page_number";

    /// <summary>Chunk metadata key carrying the originating PDF file path.</summary>
    private const string SourceFileMetadataKey = "source_file";

    /// <summary>
    /// Minimum number of scored candidates requested from the store before the
    /// tool applies its own threshold + top-K cut.
    /// </summary>
    private const int MinCandidateCount = 50;

    /// <summary>
    /// Initializes a new instance of <see cref="PdfSearchTool"/> with VFS support.
    /// </summary>
    /// <param name="searchService">Shared ephemeral-collection RAG search engine.</param>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="logger">Optional logger instance.</param>
    public PdfSearchTool(IEphemeralCollectionSearch searchService, IFileSystemService fileSystemService, ILogger<PdfSearchTool>? logger = null)
        : base(logger)
    {
        _search = searchService ?? throw new ArgumentNullException(nameof(searchService));
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

            // Step 2: Extract the selected pages as per-page inline sources
            var (sources, totalPages) = await ExtractPageSourcesAsync(pdfFiles, request, cancellationToken).ConfigureAwait(false);
            if (sources.Count == 0)
                return EmptyResponse(request.Query, totalPages, pdfFiles.Count);

            // Step 3: Ephemeral-collection search — incremental ingestion
            // (unchanged pages cost zero embeddings) + vector search.
            var outcome = await _search.SearchAsync(
                new EphemeralSearchRequest
                {
                    Sources = [.. sources],
                    Query = request.Query,
                    TopK = Math.Max(request.TopK, MinCandidateCount),
                    ChunkingStrategy = "recursive",
                    Chunking = new ChunkingOptions { MaxChunkSize = request.ChunkSize, Overlap = 0 },
                    CollectionPrefix = Name,
                },
                cancellationToken).ConfigureAwait(false);

            // Step 4: Apply the tool's threshold and top-K on the scored candidates.
            var threshold = Math.Clamp(request.Threshold, 0.0, 1.0);
            var topResults = outcome.Results
                .Where(s => s.Score >= threshold)
                .OrderByDescending(s => s.Score)
                .Take(request.TopK)
                .Select(s => new PdfSearchResult
                {
                    Content = s.Chunk.Content,
                    Score = (float)s.Score,
                    SourceFile = s.Chunk.Metadata.TryGetValue(SourceFileMetadataKey, out var file)
                        ? file
                        : s.Chunk.SourceId,
                    PageNumber = s.Chunk.Metadata.TryGetValue(PageNumberMetadataKey, out var page)
                        && int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageNumber)
                            ? pageNumber
                            : 0,
                    ChunkIndex = s.Chunk.Index
                })
                .ToList();

            LogSearchCompleted(request.Query, topResults.Count, outcome.Ingestion.ChunksCreated, pdfFiles.Count);

            return new PdfSearchResponse
            {
                Results = topResults,
                ResultCount = topResults.Count,
                TotalChunks = outcome.Ingestion.ChunksCreated,
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

    /// <summary>
    /// Extracts the requested pages of every PDF as one inline source per page
    /// (<c>{file}#page={n}</c>), carrying the page number and file path as
    /// metadata so they survive the store round-trip.
    /// </summary>
    private async Task<(List<SourceDescriptor> Sources, int TotalPages)> ExtractPageSourcesAsync(
        List<string> pdfFiles, PdfSearchRequest request, CancellationToken cancellationToken)
    {
        var sources = new List<SourceDescriptor>();
        var totalPages = 0;

        foreach (var pdfFile in pdfFiles)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Read the PDF bytes through the virtual file system, which resolves the virtual
            // path (e.g. /data/x.pdf) to its physical mount location, then parse from memory.
            var bytes = await _fs.TryReadAllBytesAsync(pdfFile, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {pdfFile}");

            using var document = PdfDocument.Open(bytes);
            var pageIndices = ParsePageRange(request.PageRange, document.NumberOfPages);
            totalPages += pageIndices.Count;

            foreach (var pageIndex in pageIndices)
            {
                var pageText = document.GetPage(pageIndex).Text ?? "";
                if (string.IsNullOrWhiteSpace(pageText))
                    continue;

                var pageLabel = pageIndex.ToString(CultureInfo.InvariantCulture);
                sources.Add(new SourceDescriptor
                {
                    Location = $"{pdfFile}#page={pageLabel}",
                    Kind = InlineTextLoader.TextKind,
                    Options = ImmutableDictionary<string, string>.Empty
                        .Add(InlineTextLoader.ContentOptionKey, pageText)
                        .Add(InlineTextLoader.MetadataOptionPrefix + PageNumberMetadataKey, pageLabel)
                        .Add(InlineTextLoader.MetadataOptionPrefix + SourceFileMetadataKey, pdfFile),
                });
            }
        }

        return (sources, totalPages);
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

    /// <summary>
    /// Splits text into chunks of approximately the given size using the canonical RAG
    /// recursive strategy (RAG-02/C3 consolidation): paragraph boundaries first, then
    /// sentences, words, and characters as needed. Kept as the reference implementation
    /// of the tool's chunking behavior (the runtime path delegates to the same strategy
    /// by name).
    /// </summary>
    internal static List<string> ChunkText(string text, int chunkSize)
    {
        var chunks = new RecursiveChunkingStrategy().ChunkText(text, chunkSize);
        return [.. chunks.Select(c => c.Content)];
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
    /// Computes cosine similarity between two embedding vectors (delegates to the
    /// Domain <see cref="EmbeddingVector"/> value object).
    /// </summary>
    internal static float CosineSimilarity(float[] a, float[] b)
        => new EmbeddingVector(a).CosineSimilarity(new EmbeddingVector(b));

    [LoggerMessage(Level = LogLevel.Information,
        Message = "PDF search completed: query='{Query}', results={ResultCount}, chunksIndexed={ChunkCount}, files={FileCount}")]
    private partial void LogSearchCompleted(string query, int resultCount, int chunkCount, int fileCount);
}
