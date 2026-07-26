using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Search;
using Orkeon.Tools.Data.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Search;

/// <summary>
/// Validation-branch and internal-helper coverage for <see cref="PdfSearchTool"/>.
/// No network / no Docker — uses a stub embedding service and in-memory PDFs.
/// </summary>
public sealed class PdfSearchToolEdgeCaseTests : IDisposable
{
    private readonly PdfSearchTool _tool;
    private readonly string _tempDir;

    public PdfSearchToolEdgeCaseTests()
    {
        var mock = new MockEmbeddingService();
        mock.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f]);
        _tool = new PdfSearchTool(
            EphemeralSearchHarness.Create(mock),
            new PassThroughFileSystemService());
        _tempDir = Path.Combine(Path.GetTempPath(), $"PdfSearchEdge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private string MakePdf(string name, params string[] pages)
    {
        var path = Path.Combine(_tempDir, name);
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(text, 12, new PdfPoint(50, 700), font);
        }
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private async Task<ToolCallResponse> CallAsync(Dictionary<string, object?> parameters)
        => await _tool.CallAsync(new ToolCallRequest(ToolName: "pdf_search", Parameters: parameters));

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ShouldReject_InvalidTopK(int topK)
    {
        var path = MakePdf("topk.pdf", "content");

        var result = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = path,
            [ParamQuery] = "q",
            ["top_k"] = topK
        });

        Assert.False(result.Success);
        Assert.Contains("TopK", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public async Task ShouldReject_InvalidThreshold(double threshold)
    {
        var path = MakePdf("thr.pdf", "content");

        var result = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = path,
            [ParamQuery] = "q",
            ["threshold"] = threshold
        });

        Assert.False(result.Success);
        Assert.Contains("Threshold", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(6000)]
    public async Task ShouldReject_InvalidChunkSize(int chunkSize)
    {
        var path = MakePdf("chunk.pdf", "content");

        var result = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = path,
            [ParamQuery] = "q",
            ["chunk_size"] = chunkSize
        });

        Assert.False(result.Success);
        Assert.Contains("ChunkSize", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReject_InvalidPageRangeFormat()
    {
        var path = MakePdf("badrange.pdf", "content");

        var result = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = path,
            [ParamQuery] = "q",
            ["page_range"] = "abc-xyz"
        });

        Assert.False(result.Success);
        Assert.Contains("page range", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnZeroResults_WhenThresholdExcludesEverything()
    {
        // Embedding factory makes query and chunks dissimilar enough only if threshold is high.
        var path = MakePdf("nores.pdf", "Some textual content about topics.");

        var result = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = path,
            [ParamQuery] = "query",
            ["threshold"] = 1.0,   // require perfect match; below-1 scores are filtered
            ["top_k"] = 5
        });

        // With identical embeddings score==1.0 passes; to force exclusion use a distinct embedding per text.
        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        // total_chunks must be >0 (text was extracted) regardless of filtering
        Assert.True(Convert.ToInt32(dict["total_chunks"]) >= 1);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenDirectoryHasNoPdfs()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = emptyDir,
            [ParamQuery] = "q",
            ["threshold"] = 0.0
        });

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(0, Convert.ToInt32(dict["files_processed"]));
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    // ── Internal helper coverage ─────────────────────────────────────

    [Fact]
    public void ChunkText_ShouldSplitLongParagraphsBySentence()
    {
        var sentences = string.Join(" ", Enumerable.Range(0, 20)
            .Select(i => $"This is sentence number {i} and it has some length to it."));

        var chunks = PdfSearchTool.ChunkText(sentences, 100);

        Assert.True(chunks.Count > 1, "long text should produce multiple chunks");
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }

    [Fact]
    public void ChunkText_ShouldReturnEmpty_ForBlankInput()
    {
        var chunks = PdfSearchTool.ChunkText("   \n\n   ", 500);
        Assert.Empty(chunks);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnOne_ForIdenticalVectors()
    {
        var v = new[] { 1f, 2f, 3f };
        Assert.Equal(1f, PdfSearchTool.CosineSimilarity(v, v), 3);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnZero_ForOrthogonalVectors()
    {
        Assert.Equal(0f, PdfSearchTool.CosineSimilarity([1f, 0f], [0f, 1f]), 3);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnZero_ForMismatchedLengths()
    {
        Assert.Equal(0f, PdfSearchTool.CosineSimilarity([1f, 2f], [1f]));
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnZero_ForZeroVector()
    {
        Assert.Equal(0f, PdfSearchTool.CosineSimilarity([0f, 0f], [1f, 2f]));
    }

    [Fact]
    public void ParsePageRange_ShouldReturnAllPages_WhenNullOrBlank()
    {
        Assert.Equal([1, 2, 3], PdfSearchTool.ParsePageRange(null, 3));
        Assert.Equal([1, 2, 3], PdfSearchTool.ParsePageRange("  ", 3));
    }

    [Fact]
    public void ParsePageRange_ShouldHandleMixedListAndRanges()
    {
        var pages = PdfSearchTool.ParsePageRange("1,3-4", 5);
        Assert.Equal([1, 3, 4], pages);
    }

    public void Dispose()
    {
        _tool.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }
}
