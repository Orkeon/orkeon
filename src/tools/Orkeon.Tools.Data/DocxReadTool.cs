using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data;

// ── Request / Response records ────────────────────────────────────────

/// <summary>
/// Request parameters for reading a DOCX file.
/// </summary>
public record DocxReadRequest
{
    /// <summary>Gets the file path of the DOCX file to read.</summary>
    [FieldSchema(Description = "Path to the .docx file to read", IsRequired = true, Example = "/docs/report.docx")]
    public string FilePath { get; init; } = "";

    /// <summary>Gets whether to extract tables from the document.</summary>
    [FieldSchema(Description = "Whether to extract tables", IsRequired = false, Example = true)]
    public bool IncludeTables { get; init; } = true;

    /// <summary>Gets whether to include document metadata.</summary>
    [FieldSchema(Description = "Whether to include document metadata", IsRequired = false, Example = true)]
    public bool IncludeMetadata { get; init; } = true;
}

/// <summary>
/// Response from reading a DOCX file, containing extracted text, tables, and metadata.
/// </summary>
public record DocxReadResponse
{
    /// <summary>Gets the extracted text content from paragraphs.</summary>
    [ReturnSchema(Description = "Extracted text content from paragraphs")]
    public string Content { get; init; } = "";

    /// <summary>Gets the extracted tables as a list of rows (each row is a list of cell values).</summary>
    [ReturnSchema(Description = "Extracted tables as list of rows")]
    public IReadOnlyList<List<string>> Tables { get; init; } = [];

    /// <summary>Gets the document metadata (title, author, created date, etc.).</summary>
    [ReturnSchema(Description = "Document metadata")]
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>Gets the number of paragraphs in the document.</summary>
    [ReturnSchema(Description = "Number of paragraphs", Example = 42)]
    public int ParagraphCount { get; init; }
}

/// <summary>
/// Tool for reading DOCX (Word) files and returning structured data.
/// Uses DocumentFormat.OpenXml for robust document parsing.
/// </summary>
[ToolContract("docx_reader",
    Name = "docx_reader",
    Description = "Read DOCX (Word) files and return structured data including text, tables, and metadata.")]
public partial class DocxReadTool : FileToolBase<DocxReadRequest, DocxReadResponse>
{
    /// <summary>Initializes a new instance of <see cref="DocxReadTool"/> with virtual file system support.</summary>
    /// <param name="fileSystemService">Virtual file system service for mount-aware path resolution.</param>
    /// <param name="pathValidator">Path validator for traversal/SSRF protection (defense in depth).</param>
    /// <param name="logger">Optional logger instance.</param>
    public DocxReadTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<DocxReadTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DocxReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return "FilePath cannot be empty";

        if (!request.FilePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return "File must have a .docx extension";

        // VFS path: validate mount + Read rights. Existence is verified during execution.
        var vfsResult = ResolveVirtualPath(request.FilePath, FileAccessRights.Read);
        return vfsResult.IsAllowed ? null : vfsResult.DenialReason ?? "path validation failed";
    }

    /// <inheritdoc />
    protected override Task<DocxReadResponse> ExecuteTypedAsync(
        DocxReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<DocxReadResponse> ExecuteTypedCoreAsync()
        {
            // VFS path: read the bytes through the virtual file system, which resolves the
            // virtual path (e.g. /data/x.docx) to its physical mount location.
            var vfsResult = ResolveVirtualPath(request.FilePath, FileAccessRights.Read);
            if (!vfsResult.IsAllowed)
                throw new InvalidOperationException(vfsResult.DenialReason ?? "path validation failed");

            var bytes = await _fileSystemService.TryReadAllBytesAsync(request.FilePath, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {request.FilePath}");
            var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);

            using (document)
            {
                var body = document.MainDocumentPart?.Document?.Body;

                var paragraphs = body?.Elements<Paragraph>().ToList() ?? [];
                var content = ExtractText(paragraphs);

                var tables = request.IncludeTables
                    ? ExtractTables(body)
                    : [];

                var metadata = request.IncludeMetadata
                    ? ExtractMetadata(document)
                    : [];

                LogReadDocx(request.FilePath, paragraphs.Count);

                return new DocxReadResponse
                {
                    Content = content,
                    Tables = tables,
                    Metadata = metadata,
                    ParagraphCount = paragraphs.Count
                };
            }
        }
    }

    private static string ExtractText(List<Paragraph> paragraphs)
    {
        return string.Join(
            Environment.NewLine,
            paragraphs
                .Select(p => p.InnerText)
                .Where(text => !string.IsNullOrEmpty(text)));
    }

    private static List<List<string>> ExtractTables(Body? body)
    {
        if (body is null) return [];

        return body.Elements<Table>()
            .SelectMany(table => table.Elements<TableRow>())
            .Select(row => row.Elements<TableCell>().Select(cell => cell.InnerText).ToList())
            .ToList();
    }

    private static Dictionary<string, string> ExtractMetadata(WordprocessingDocument document)
    {
        var metadata = new Dictionary<string, string>();
        var props = document.PackageProperties;

        if (props.Title is not null)
            metadata["title"] = props.Title;

        if (props.Creator is not null)
            metadata["author"] = props.Creator;

        if (props.Created.HasValue)
            metadata["created"] = props.Created.Value.ToString("o");

        if (props.Modified.HasValue)
            metadata["modified"] = props.Modified.Value.ToString("o");

        if (props.Subject is not null)
            metadata["subject"] = props.Subject;

        if (props.Description is not null)
            metadata["description"] = props.Description;

        if (props.LastModifiedBy is not null)
            metadata["last_modified_by"] = props.LastModifiedBy;

        return metadata;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully read DOCX: {Path} ({Paragraphs} paragraphs)")]
    private partial void LogReadDocx(string path, int paragraphs);
}
