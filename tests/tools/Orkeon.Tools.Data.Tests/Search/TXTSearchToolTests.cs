using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Search;
using Orkeon.Tools.Data.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Search;

public sealed class TXTSearchToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockEmbeddingService _mockEmbeddingService;
    private readonly TxtSearchTool _tool;

    public TXTSearchToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"txt_search_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _mockEmbeddingService = new MockEmbeddingService();
        _tool = new TxtSearchTool(
            EphemeralSearchHarness.Create(_mockEmbeddingService),
            new PassThroughFileSystemService());
    }

    [Fact]
    public async Task ShouldReturnResults_WhenQueryMatchesContent()
    {
        // Arrange: query embedding is close to chunk embedding
        var filePath = CreateTxtFile("notes.txt",
            "Machine learning is a subset of AI.\n\nDeep learning uses neural networks.\n\nStatistics is foundational.");

        // Make the query embedding identical to the first chunk embedding
        _mockEmbeddingService.SetEmbeddingFactory(text =>
            text.Contains("Machine learning", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AI algorithms", StringComparison.OrdinalIgnoreCase)
                ? [1.0f, 0.0f, 0.0f]
                : [0.0f, 1.0f, 0.0f]);

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "AI algorithms",
                ["top_k"] = 5,
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount > 0, "Expected at least one result");
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenQueryDoesNotMatch()
    {
        // Arrange: use a high threshold and different embeddings
        var filePath = CreateTxtFile("empty.txt",
            "The weather today is sunny.\n\nClouds are forming in the west.");

        _mockEmbeddingService.SetEmbeddingFactory(text =>
            text.Contains("quantum", StringComparison.OrdinalIgnoreCase)
                ? [1.0f, 0.0f, 0.0f]
                : [0.0f, 0.0f, 1.0f]);

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "quantum physics equations",
                ["threshold"] = 0.99
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    [Fact]
    public async Task ShouldRespectTopK_WhenManyChunksMatch()
    {
        // Arrange: many chunks, all matching
        var paragraphs = Enumerable.Range(1, 20)
            .Select(i => $"Paragraph number {i} about testing software.")
            .ToArray();
        var filePath = CreateTxtFile("many.txt", string.Join("\n\n", paragraphs));

        _mockEmbeddingService.SetEmbeddingResult([0.5f, 0.5f, 0.5f]);

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "testing software",
                ["top_k"] = 3,
                ["threshold"] = 0.0,
                // The canonical recursive chunker (RAG-02/C3) merges small adjacent
                // paragraphs up to chunk_size; keep chunks small so >3 chunks exist.
                ["chunk_size"] = 100
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3, Convert.ToInt32(dict["result_count"]));
    }

    [Fact]
    public async Task ShouldSearchDirectory_WhenPathIsFolder()
    {
        // Arrange: multiple .txt files in a directory
        CreateTxtFile("file1.txt", "Introduction to algorithms.\n\nSorting and searching.");
        CreateTxtFile("file2.txt", "Data structures are important.\n\nTrees and graphs.");

        _mockEmbeddingService.SetEmbeddingResult([0.5f, 0.5f, 0.5f]);

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = _tempDir,
                [ParamQuery] = "algorithms",
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.True(Convert.ToInt32(dict["files_processed"]) >= 2);
        Assert.True(Convert.ToInt32(dict["result_count"]) > 0);
    }

    [Fact]
    public async Task ShouldReturnNoResults_WhenPathDoesNotExist()
    {
        // VFS contract: an unresolvable path yields zero files processed (empty success),
        // not a hard error — the previous "File not found" failure path was removed when the
        // tool was migrated to resolve everything through IFileSystemService.
        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/nonexistent/path/file.txt",
                [ParamQuery] = "anything"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, Convert.ToInt32(dict!["files_processed"]));
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsEmpty()
    {
        var filePath = CreateTxtFile("valid.txt", "Some content here.");

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = ""
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNoResults_WhenFileExtensionNotSupported()
    {
        // VFS contract: a file whose extension is not supported is silently skipped during
        // file resolution, yielding zero files processed (empty success). The previous
        // "Unsupported extension" failure path was removed during the VFS migration.
        var filePath = Path.Combine(_tempDir, "data.csv");
        await File.WriteAllTextAsync(filePath, "col1,col2\nval1,val2", TestContext.Current.CancellationToken);

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "data"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, Convert.ToInt32(dict!["files_processed"]));
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    [Fact]
    public async Task ShouldChunkLargeFiles_WhenContentExceedsChunkSize()
    {
        // Arrange: create a file with a very long paragraph that must be re-split by sentences
        var longParagraph = string.Join(". ",
            Enumerable.Range(1, 50).Select(i => $"Sentence number {i} about a very important topic"));
        var filePath = CreateTxtFile("large.txt", longParagraph);

        _mockEmbeddingService.SetEmbeddingResult([0.5f, 0.5f, 0.5f]);

        var request = new ToolCallRequest(
            ToolName: "txt_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                [ParamQuery] = "important topic",
                ["chunk_size"] = 200,
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        var totalChunks = Convert.ToInt32(dict["total_chunks"]);
        Assert.True(totalChunks > 1, $"Expected multiple chunks, got {totalChunks}");
    }

    // ── Helper methods ───────────────────────────────────────────────

    private string CreateTxtFile(string fileName, string content)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore cleanup errors */ }
        _tool.Dispose();
    }
}
