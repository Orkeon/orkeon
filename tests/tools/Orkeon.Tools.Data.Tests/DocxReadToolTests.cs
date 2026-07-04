using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Data.Tests;

public sealed class DocxReadToolTests : IDisposable
{
    private readonly DocxReadTool _tool;
    private readonly string _testDir;

    public DocxReadToolTests()
    {
        _tool = new DocxReadTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"docx_read_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ShouldExtractText_WhenDocxIsValid()
    {
        var docxPath = CreateDocxFile("Hello World", ["First paragraph.", "Second paragraph."]);

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?> { ["file_path"] = docxPath }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var content = dict["content"]?.ToString();
        Assert.NotNull(content);
        Assert.Contains("Hello World", content);
        Assert.Contains("First paragraph.", content);
        Assert.Contains("Second paragraph.", content);
        Assert.True((int)dict["paragraph_count"]! >= 3);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileDoesNotExist()
    {
        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = "/nonexistent/file.docx"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileIsNotDocx()
    {
        var txtPath = Path.Combine(_testDir, $"{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(txtPath, "This is a text file", TestContext.Current.CancellationToken);

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = txtPath
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(".docx", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFilePathIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldExtractTables_WhenDocxHasTables()
    {
        var docxPath = CreateDocxFileWithTable();

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = docxPath,
                ["include_tables"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var tables = dict["tables"] as List<object>;
        Assert.NotNull(tables);
        Assert.True(tables.Count >= 2); // At least header + 1 data row

        // First row (header)
        var headerRow = tables[0] as List<object>;
        Assert.NotNull(headerRow);
        Assert.Contains("Name", headerRow.Select(c => c?.ToString()));
        Assert.Contains("Age", headerRow.Select(c => c?.ToString()));
    }

    [Fact]
    public async Task ShouldSkipTables_WhenIncludeTablesIsFalse()
    {
        var docxPath = CreateDocxFileWithTable();

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = docxPath,
                ["include_tables"] = false
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var tables = dict["tables"] as List<object>;
        Assert.NotNull(tables);
        Assert.Empty(tables);
    }

    [Fact]
    public async Task ShouldExtractMetadata_WhenDocxHasMetadata()
    {
        var docxPath = CreateDocxFileWithMetadata("Test Title", "Test Author");

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = docxPath,
                ["include_metadata"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var metadata = dict["metadata"] as Dictionary<string, object?>;
        Assert.NotNull(metadata);
        Assert.Equal("Test Title", metadata["title"]?.ToString());
        Assert.Equal("Test Author", metadata["author"]?.ToString());
    }

    [Fact]
    public async Task ShouldSkipMetadata_WhenIncludeMetadataIsFalse()
    {
        var docxPath = CreateDocxFileWithMetadata("Test Title", "Test Author");

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = docxPath,
                ["include_metadata"] = false
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var metadata = dict["metadata"] as Dictionary<string, object?>;
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("docx_reader", _tool.Name);
        Assert.Equal("File Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["file_path"].Required);
        Assert.False(_tool.Schema.Parameters["include_tables"].Required);
        Assert.False(_tool.Schema.Parameters["include_metadata"].Required);
    }

    private string CreateDocxFile(string title, List<string> paragraphs)
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.docx");
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Add title
        var titlePara = new Paragraph();
        var titleRun = new Run();
        titleRun.AppendChild(new Text(title));
        titlePara.AppendChild(titleRun);
        body.AppendChild(titlePara);

        // Add paragraphs
        foreach (var text in paragraphs)
        {
            var para = new Paragraph();
            var run = new Run();
            run.AppendChild(new Text(text));
            para.AppendChild(run);
            body.AppendChild(para);
        }

        mainPart.Document.Save();
        return path;
    }

    private string CreateDocxFileWithTable()
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.docx");
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Add a paragraph
        var para = new Paragraph();
        var run = new Run();
        run.AppendChild(new Text("Document with table"));
        para.AppendChild(run);
        body.AppendChild(para);

        // Add a table
        var table = new Table();

        // Header row
        var headerRow = new TableRow();
        headerRow.AppendChild(CreateTableCell("Name"));
        headerRow.AppendChild(CreateTableCell("Age"));
        table.AppendChild(headerRow);

        // Data row
        var dataRow = new TableRow();
        dataRow.AppendChild(CreateTableCell("Alice"));
        dataRow.AppendChild(CreateTableCell("30"));
        table.AppendChild(dataRow);

        body.AppendChild(table);

        mainPart.Document.Save();
        return path;
    }

    private static TableCell CreateTableCell(string text)
    {
        var cell = new TableCell();
        var para = new Paragraph();
        var run = new Run();
        run.AppendChild(new Text(text));
        para.AppendChild(run);
        cell.AppendChild(para);
        return cell;
    }

    private string CreateDocxFileWithMetadata(string title, string author)
    {
        var path = Path.Combine(_testDir, $"{Guid.NewGuid():N}.docx");
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        var para = new Paragraph();
        var run = new Run();
        run.AppendChild(new Text("Content"));
        para.AppendChild(run);
        body.AppendChild(para);

        // Set metadata
        document.PackageProperties.Title = title;
        document.PackageProperties.Creator = author;
        document.PackageProperties.Created = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        mainPart.Document.Save();
        return path;
    }

    [Fact]
    public async Task DocxRead_PathTraversal_ValidatorDenied_FailsFastWithoutReading()
    {
        var deniedValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Denied("path traversal detected"));

        using var tool = new DocxReadTool(new PassThroughFileSystemService(), deniedValidator);

        var request = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?> { ["file_path"] = "../../etc/passwd.docx" }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
        // The defense-in-depth validator is consulted with the resolved physical path
        // (the VFS collapses the traversal segments before delegating), so match on the
        // file name rather than the raw requested path.
        Assert.Contains(deniedValidator.Calls, c => c.Path.EndsWith("passwd.docx", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
