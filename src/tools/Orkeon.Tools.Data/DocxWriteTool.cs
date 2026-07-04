using DocumentFormat.OpenXml;
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
/// Request parameters for creating a DOCX file.
/// </summary>
public record DocxWriteRequest
{
    /// <summary>Gets the output file path for the DOCX file.</summary>
    [FieldSchema(Description = "Output file path for the .docx", IsRequired = true, Example = "/docs/output.docx")]
    public string FilePath { get; init; } = "";

    /// <summary>Gets the document title.</summary>
    [FieldSchema(Description = "Document title", IsRequired = false, Example = "My Report")]
    public string Title { get; init; } = "";

    /// <summary>Gets the content paragraphs to write.</summary>
    [FieldSchema(Description = "Content paragraphs to write", IsRequired = true)]
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>Gets the bullet list items (optional).</summary>
    [FieldSchema(Description = "Bullet list items (optional)", IsRequired = false)]
    public IReadOnlyList<string> BulletItems { get; init; } = [];
}

/// <summary>
/// Response from creating a DOCX file.
/// </summary>
public record DocxWriteResponse
{
    /// <summary>Gets whether the file was created successfully.</summary>
    [ReturnSchema(Description = "Whether the file was created successfully", Example = true)]
    public bool Success { get; init; }

    /// <summary>Gets the path of the created file.</summary>
    [ReturnSchema(Description = "Path of the created file")]
    public string FilePath { get; init; } = "";

    /// <summary>Gets the file size in bytes.</summary>
    [ReturnSchema(Description = "File size in bytes", Example = 12345)]
    public long FileSize { get; init; }
}

/// <summary>
/// Tool for creating DOCX (Word) files with title, paragraphs, and bullet lists.
/// Uses DocumentFormat.OpenXml for document generation.
/// </summary>
[ToolContract("docx_writer",
    Name = "docx_writer",
    Description = "Create DOCX (Word) files with title, paragraphs, and bullet lists.")]
public partial class DocxWriteTool : FileToolBase<DocxWriteRequest, DocxWriteResponse>
{
    /// <summary>Initializes a new instance of <see cref="DocxWriteTool"/> with VFS support.</summary>
    /// <param name="fileSystemService">Virtual file system service.</param>
    /// <param name="pathValidator">Path validator for traversal/SSRF protection (defense in depth).</param>
    /// <param name="logger">Optional logger instance.</param>
    public DocxWriteTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<DocxWriteTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DocxWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
            return "FilePath cannot be empty";

        var pathResult = ResolveVirtualPath(request.FilePath, FileAccessRights.Write);
        if (!pathResult.IsAllowed)
            return pathResult.DenialReason ?? "Invalid or unsafe file path";

        if (!request.FilePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return "File must have a .docx extension";

        return null;
    }

    /// <inheritdoc />
    protected override Task<DocxWriteResponse> ExecuteTypedAsync(
        DocxWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<DocxWriteResponse> ExecuteTypedCoreAsync()
        {
            // Build the document in memory, then commit the bytes through the
            // virtual file system so the physical mount location is resolved by the VFS.
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                using (var document = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
                {
                    PopulateDocument(document, request);
                }
                bytes = ms.ToArray();
            }

            var fileSize = await _fileSystemService.WriteAllBytesAsync(request.FilePath, bytes, cancellationToken).ConfigureAwait(false);

            LogWriteDocx(request.FilePath, request.Paragraphs.Count, request.BulletItems.Count);

            return new DocxWriteResponse
            {
                Success = true,
                FilePath = request.FilePath,
                FileSize = fileSize
            };
        }
    }

    private static void PopulateDocument(WordprocessingDocument document, DocxWriteRequest request)
    {
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            AddTitle(body, request.Title);
            document.PackageProperties.Title = request.Title;
        }

        foreach (var text in request.Paragraphs)
        {
            AddParagraph(body, text);
        }

        if (request.BulletItems.Count > 0)
        {
            AddNumberingPart(mainPart);
            foreach (var item in request.BulletItems)
            {
                AddBulletItem(body, item);
            }
        }

        mainPart.Document.Save();
    }

    private static void AddTitle(Body body, string title)
    {
        var paragraph = new Paragraph();
        var runProperties = new RunProperties();
        runProperties.AppendChild(new Bold());
        runProperties.AppendChild(new FontSize { Val = "48" }); // 24pt (half-points)

        var run = new Run();
        run.AppendChild(runProperties);
        run.AppendChild(new Text(title));

        var paragraphProperties = new ParagraphProperties();
        paragraphProperties.AppendChild(new ParagraphStyleId { Val = "Heading1" });
        paragraph.AppendChild(paragraphProperties);
        paragraph.AppendChild(run);

        body.AppendChild(paragraph);
    }

    private static void AddParagraph(Body body, string text)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
        body.AppendChild(paragraph);
    }

    private static void AddNumberingPart(MainDocumentPart mainPart)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();

        var level = new Level(
            new NumberingFormat { Val = NumberFormatValues.Bullet },
            new LevelText { Val = "\u2022" })
        { LevelIndex = 0 };

        var abstractNum = new AbstractNum { AbstractNumberId = 1 };
        abstractNum.AppendChild(level);

        var numberingInstance = new NumberingInstance { NumberID = 1 };
        numberingInstance.AppendChild(new AbstractNumId { Val = 1 });

        numbering.AppendChild(abstractNum);
        numbering.AppendChild(numberingInstance);
        numberingPart.Numbering = numbering;
    }

    private static void AddBulletItem(Body body, string text)
    {
        var paragraph = new Paragraph();

        var paragraphProperties = new ParagraphProperties();
        var numberingProperties = new NumberingProperties();
        numberingProperties.AppendChild(new NumberingLevelReference { Val = 0 });
        numberingProperties.AppendChild(new NumberingId { Val = 1 });
        paragraphProperties.AppendChild(numberingProperties);
        paragraph.AppendChild(paragraphProperties);

        var run = new Run();
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);

        body.AppendChild(paragraph);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully wrote DOCX: {Path} ({Paragraphs} paragraphs, {Bullets} bullet items)")]
    private partial void LogWriteDocx(string path, int paragraphs, int bullets);
}
