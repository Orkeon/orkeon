using System.Globalization;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Orkeon.Tools.Data;

// ── Request / Response records ────────────────────────────────────────

/// <summary>
/// Request parameters for reading text content from a PDF file.
/// </summary>
public record PdfReaderRequest
{
    /// <summary>Gets the file path of the PDF to read.</summary>
    [FieldSchema(Description = "The file path of the PDF to read", Example = "/docs/report.pdf")]
    public string Path { get; init; } = "";

    /// <summary>Gets the optional page range (e.g., '1-5', '1,3,5', '2-'). Defaults to all pages.</summary>
    [FieldSchema(Description = "Optional page range (e.g., '1-5', '1,3,5', '2-'). Default: all pages.", Example = "1-5")]
    public string? PageRange { get; init; }
}

/// <summary>
/// Per-page information extracted from a PDF document.
/// </summary>
public record PdfReaderPageInfo
{
    /// <summary>Gets the 1-based page number.</summary>
    [ReturnSchema(Description = "1-based page number", Example = 1)]
    public int PageNumber { get; init; }

    /// <summary>Gets the extracted text content of the page.</summary>
    [ReturnSchema(Description = "Extracted text content of the page", Example = "Chapter 1: Introduction\nThis document provides an overview...")]
    public string Text { get; init; } = "";

    /// <summary>Gets the page width in points.</summary>
    [ReturnSchema(Description = "Page width in points", Example = 612.0)]
    public double Width { get; init; }

    /// <summary>Gets the page height in points.</summary>
    [ReturnSchema(Description = "Page height in points", Example = 792.0)]
    public double Height { get; init; }
}

/// <summary>
/// Response from reading a PDF file, containing extracted text and per-page details.
/// </summary>
public record PdfReaderResponse
{
    /// <summary>Gets the combined text content from all read pages.</summary>
    [ReturnSchema(Description = "Combined text content from all read pages", Example = "Chapter 1: Introduction\nThis document provides an overview...")]
    public string Content { get; init; } = "";

    /// <summary>Gets the per-page details including text and dimensions.</summary>
    [ReturnSchema(Description = "Per-page details with text, dimensions")]
    public IReadOnlyList<PdfReaderPageInfo> Pages { get; init; } = [];

    /// <summary>Gets the total number of pages in the PDF.</summary>
    [ReturnSchema(Description = "Total number of pages in the PDF", Example = 12)]
    public int TotalPages { get; init; }

    /// <summary>Gets the number of pages actually read.</summary>
    [ReturnSchema(Description = "Number of pages actually read", Example = 5)]
    public int PagesRead { get; init; }

    /// <summary>Gets the page range that was read (e.g., '1-5' or 'all').</summary>
    [ReturnSchema(Description = "Page range that was read (e.g., '1-5' or 'all')", Example = "1-5")]
    public string PageRange { get; init; } = "all";
}

/// <summary>
/// Tool for extracting text from PDF files.
/// Uses UglyToad.PdfPig for lightweight pure .NET PDF reading.
/// </summary>
[ToolContract("pdf_reader",
    Name = "pdf_reader",
    Description = "Extract text content from PDF files. Supports page range selection.")]
public partial class PdfReaderTool : FileToolBase<PdfReaderRequest, PdfReaderResponse>
{
    /// <summary>Initializes a new instance of <see cref="PdfReaderTool"/> with virtual file system support.</summary>
    /// <param name="fileSystemService">Virtual file system service for mount-aware path resolution.</param>
    /// <param name="pathValidator">Path validator for traversal/SSRF protection (defense in depth).</param>
    /// <param name="logger">Optional logger instance.</param>
    public PdfReaderTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<PdfReaderTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(PdfReaderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        // Validate mount + Read rights. Existence is verified during execution
        // via the byte read (the VFS resolves the virtual path to its physical mount).
        var vfsResult = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        return vfsResult.IsAllowed ? null : vfsResult.DenialReason ?? "path validation failed";
    }

    /// <inheritdoc />
    protected override Task<PdfReaderResponse> ExecuteTypedAsync(
        PdfReaderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<PdfReaderResponse> ExecuteTypedCoreAsync()
        {
            // Read the bytes through the virtual file system, which resolves the
            // virtual path (e.g. /data/x.pdf) to its physical mount location.
            var vfsResult = ResolveVirtualPath(request.Path, FileAccessRights.Read);
            if (!vfsResult.IsAllowed)
                throw new InvalidOperationException(vfsResult.DenialReason ?? "path validation failed");

            var bytes = await _fileSystemService.TryReadAllBytesAsync(request.Path, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {request.Path}");

            using (var document = PdfDocument.Open(bytes))
            {
                var totalPages = document.NumberOfPages;
                var pageIndices = ParsePageRange(request.PageRange, totalPages);

                var pages = new List<PdfReaderPageInfo>();
                var allText = new System.Text.StringBuilder();

                foreach (var pageIndex in pageIndices)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var page = document.GetPage(pageIndex);
                    var pageText = page.Text ?? "";

                    pages.Add(new PdfReaderPageInfo
                    {
                        PageNumber = pageIndex,
                        Text = pageText,
                        Width = page.Width,
                        Height = page.Height
                    });

                    allText.AppendLine(pageText);
                }

                LogReadPdf(request.Path, pages.Count, totalPages);

                return new PdfReaderResponse
                {
                    Content = allText.ToString().Trim(),
                    Pages = pages,
                    TotalPages = totalPages,
                    PagesRead = pages.Count,
                    PageRange = request.PageRange ?? "all"
                };
            }
        }
    }

    private static List<int> ParsePageRange(string? pageRange, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(pageRange))
            return Enumerable.Range(1, totalPages).ToList();

        var pages = new HashSet<int>();

        foreach (var part in pageRange.Split(','))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            AddParsedPages(pages, trimmed, totalPages);
        }

        return pages.OrderBy(p => p).ToList();
    }

    private static void AddParsedPages(HashSet<int> pages, string segment, int totalPages)
    {
        if (segment.Contains('-', StringComparison.Ordinal))
        {
            AddRangePages(pages, segment, totalPages);
            return;
        }

        if (int.TryParse(segment, out var pageNum) && pageNum >= 1 && pageNum <= totalPages)
            pages.Add(pageNum);
    }

    private static void AddRangePages(HashSet<int> pages, string rangeSpec, int totalPages)
    {
        var rangeParts = rangeSpec.Split('-');
        var start = string.IsNullOrEmpty(rangeParts[0]) ? 1 : int.Parse(rangeParts[0], CultureInfo.InvariantCulture);
        var end = string.IsNullOrEmpty(rangeParts[1]) ? totalPages : int.Parse(rangeParts[1], CultureInfo.InvariantCulture);

        start = Math.Max(1, Math.Min(start, totalPages));
        end = Math.Max(1, Math.Min(end, totalPages));

        for (int i = start; i <= end; i++)
            pages.Add(i);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully read PDF: {Path} ({Pages}/{Total} pages)")]
    private partial void LogReadPdf(string path, int pages, int total);
}
