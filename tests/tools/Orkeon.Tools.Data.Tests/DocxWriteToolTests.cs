using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Data.Tests;

public sealed class DocxWriteToolTests : IDisposable
{
    private readonly DocxWriteTool _tool;
    private readonly DocxReadTool _readTool;
    private readonly string _testDir;

    public DocxWriteToolTests()
    {
        _tool = new DocxWriteTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _readTool = new DocxReadTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"docx_write_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task ShouldCreateBasicDocxFile()
    {
        var filePath = Path.Combine(_testDir, "basic.docx");

        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["paragraphs"] = new List<object> { "Hello World" }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True((bool)dict["success"]!);
        Assert.Equal(filePath, dict["file_path"]?.ToString());
        Assert.True(Convert.ToInt64(dict["file_size"]!) > 0);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ShouldCreateDocxWithTitleAndParagraphs()
    {
        var filePath = Path.Combine(_testDir, "titled.docx");

        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["title"] = "My Report",
                ["paragraphs"] = new List<object> { "First paragraph.", "Second paragraph." }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));

        // Verify by reading back
        var readRequest = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["include_metadata"] = true
            }
        );

        var readResult = await _readTool.CallAsync(readRequest, TestContext.Current.CancellationToken);
        Assert.True(readResult.Success);
        var readDict = readResult.Result as Dictionary<string, object?>;
        Assert.NotNull(readDict);

        var content = readDict["content"]?.ToString();
        Assert.NotNull(content);
        Assert.Contains("My Report", content);
        Assert.Contains("First paragraph.", content);
        Assert.Contains("Second paragraph.", content);

        // Check title metadata
        var metadata = readDict["metadata"] as Dictionary<string, object?>;
        Assert.NotNull(metadata);
        Assert.Equal("My Report", metadata["title"]?.ToString());
    }

    [Fact]
    public async Task ShouldCreateDocxWithBulletItems()
    {
        var filePath = Path.Combine(_testDir, "bullets.docx");

        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["paragraphs"] = new List<object> { "Introduction:" },
                ["bullet_items"] = new List<object> { "Item one", "Item two", "Item three" }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));

        // Verify by reading back
        var readRequest = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?> { ["file_path"] = filePath }
        );

        var readResult = await _readTool.CallAsync(readRequest, TestContext.Current.CancellationToken);
        Assert.True(readResult.Success);
        var readDict = readResult.Result as Dictionary<string, object?>;
        Assert.NotNull(readDict);

        var content = readDict["content"]?.ToString();
        Assert.NotNull(content);
        Assert.Contains("Introduction:", content);
        Assert.Contains("Item one", content);
        Assert.Contains("Item two", content);
        Assert.Contains("Item three", content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFilePathIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = "",
                ["paragraphs"] = new List<object> { "Content" }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileExtensionIsNotDocx()
    {
        var filePath = Path.Combine(_testDir, "output.txt");

        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["paragraphs"] = new List<object> { "Content" }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(".docx", result.Error);
    }

    [Fact]
    public async Task ShouldCreateDirectoryIfNotExists()
    {
        var subDir = Path.Combine(_testDir, "subdir", "nested");
        var filePath = Path.Combine(subDir, "output.docx");

        var request = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["paragraphs"] = new List<object> { "Content in nested dir" }
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ShouldProduceReadableFile_WhenReadBack()
    {
        var filePath = Path.Combine(_testDir, "roundtrip.docx");

        // Write
        var writeRequest = new ToolCallRequest(
            ToolName: "docx_writer",
            Parameters: new Dictionary<string, object?>
            {
                ["file_path"] = filePath,
                ["title"] = "Round Trip Test",
                ["paragraphs"] = new List<object> { "Paragraph A", "Paragraph B" },
                ["bullet_items"] = new List<object> { "Bullet 1", "Bullet 2" }
            }
        );

        var writeResult = await _tool.CallAsync(writeRequest, TestContext.Current.CancellationToken);
        Assert.True(writeResult.Success);

        // Read back
        var readRequest = new ToolCallRequest(
            ToolName: "docx_reader",
            Parameters: new Dictionary<string, object?> { ["file_path"] = filePath }
        );

        var readResult = await _readTool.CallAsync(readRequest, TestContext.Current.CancellationToken);
        Assert.True(readResult.Success);

        var dict = readResult.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var content = dict["content"]?.ToString();
        Assert.NotNull(content);
        Assert.Contains("Round Trip Test", content);
        Assert.Contains("Paragraph A", content);
        Assert.Contains("Paragraph B", content);
        Assert.Contains("Bullet 1", content);
        Assert.Contains("Bullet 2", content);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("docx_writer", _tool.Name);
        Assert.Equal("File Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["file_path"].Required);
        Assert.True(_tool.Schema.Parameters["paragraphs"].Required);
        Assert.False(_tool.Schema.Parameters["title"].Required);
        Assert.False(_tool.Schema.Parameters["bullet_items"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
        _readTool.Dispose();
    }
}
