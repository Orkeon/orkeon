using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Targets the validation branches and the long-paragraph / sentence-splitting chunking
/// path of <see cref="DirectorySearchTool"/> not covered by <see cref="DirectorySearchToolTests"/>.
/// </summary>
public sealed class DirectorySearchToolEdgeCaseTests : IDisposable
{
    private readonly MockEmbeddingService _embeddingService = new();
    private readonly DirectorySearchTool _tool;
    private readonly string _testDir;

    public DirectorySearchToolEdgeCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"dirsearch_edge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _tool = new DirectorySearchTool(new PassThroughFileSystemService(), _embeddingService);
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
    }

    private async Task<ToolCallResponse> CallAsync(Dictionary<string, object?> parameters)
        => await _tool.CallAsync(new ToolCallRequest("directory_search", parameters));

    private static Dictionary<string, object?> ResultDict(ToolCallResponse response)
    {
        Assert.True(response.Success, response.Error ?? "Expected success");
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict!;
    }

    // -- Validation branches ─────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenPathIsEmpty()
    {
        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = "",
            [ParamQuery] = "anything"
        });
        Assert.False(response.Success);
        Assert.Contains("Path cannot be empty", response.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ShouldReturnError_WhenTopKOutOfRange(int topK)
    {
        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "q",
            ["top_k"] = topK
        });
        Assert.False(response.Success);
        Assert.Contains("TopK", response.Error);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public async Task ShouldReturnError_WhenThresholdOutOfRange(double threshold)
    {
        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "q",
            ["threshold"] = threshold
        });
        Assert.False(response.Success);
        Assert.Contains("Threshold", response.Error);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(6000)]
    public async Task ShouldReturnError_WhenChunkSizeOutOfRange(int chunkSize)
    {
        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "q",
            ["chunk_size"] = chunkSize
        });
        Assert.False(response.Success);
        Assert.Contains("ChunkSize", response.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20000)]
    public async Task ShouldReturnError_WhenMaxFileSizeOutOfRange(int maxKb)
    {
        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "q",
            ["max_file_size_kb"] = maxKb
        });
        Assert.False(response.Success);
        Assert.Contains("MaxFileSizeKb", response.Error);
    }

    // -- Empty / no-chunk path ───────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnZeroResults_WhenDirectoryHasNoTextFiles()
    {
        // Empty directory => no chunks => the early-return branch
        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "anything",
            ["threshold"] = 0.0
        });

        var dict = ResultDict(response);
        Assert.Equal(0, (int)dict["result_count"]!);
        Assert.Equal(0, (int)dict["total_chunks"]!);
        Assert.Equal(0, (int)dict["files_processed"]!);
    }

    [Fact]
    public async Task ShouldSkipWhitespaceOnlyFile()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);
        CreateFile("blank.txt", "   \n\n   \n\n");

        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "q",
            ["threshold"] = 0.0
        });

        var dict = ResultDict(response);
        Assert.Equal(1, (int)dict["files_skipped"]!);
        Assert.Equal(0, (int)dict["total_chunks"]!);
    }

    // -- Long-paragraph chunking (sentence splitting) ────────────────────

    [Fact]
    public async Task ShouldSplitLongParagraph_IntoMultipleChunks()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        // Single paragraph (no blank line) longer than chunk_size, made of many sentences.
        var sentences = string.Join(" ",
            Enumerable.Range(1, 60).Select(i => $"This is sentence number {i} about widgets."));
        CreateFile("long.txt", sentences);

        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "widgets",
            ["chunk_size"] = 100,
            ["top_k"] = 100,
            ["threshold"] = 0.0
        });

        var dict = ResultDict(response);
        // The long paragraph must be split into more than one chunk.
        Assert.True((int)dict["total_chunks"]! > 1, $"Expected >1 chunk, got {dict["total_chunks"]}");
        Assert.Equal(1, (int)dict["files_processed"]!);
    }

    [Fact]
    public async Task ShouldThresholdFilterOutLowSimilarity()
    {
        // The query embeds to one axis, every file chunk to an orthogonal axis =>
        // cosine similarity is 0, below the 0.9 threshold => all chunks filtered out.
        _embeddingService.SetEmbeddingFactory(text =>
            text.Contains("FILECHUNK") ? [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f] : [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("doc.txt", "FILECHUNK first paragraph.\n\nFILECHUNK second paragraph.");

        var response = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "unrelated search terms",
            ["threshold"] = 0.9
        });

        var dict = ResultDict(response);
        Assert.True((int)dict["total_chunks"]! > 0);
        Assert.Equal(0, (int)dict["result_count"]!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { /* best effort */ }
        _tool.Dispose();
    }
}
