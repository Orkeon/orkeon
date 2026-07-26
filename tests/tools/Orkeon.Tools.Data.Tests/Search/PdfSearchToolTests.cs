using Orkeon.Domain.Memory;
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

public sealed class PdfSearchToolTests : IDisposable
{
    private readonly IEmbeddingService _embeddingService;
    private readonly PdfSearchTool _tool;
    private readonly string _tempDir;

    private static readonly string[] Keywords =
    [
        "machine", "learning", "algorithm", "neural", "network",
        "data", "training", "model", "deep", "artificial",
        "intelligence", "classification", "regression", "clustering", "optimization",
        "gradient", "backpropagation", "loss", "function", "layer",
        "weight", "bias", "activation", "transformer", "attention",
        "embedding", "vector", "similarity", "search", "retrieval",
        "document", "text"
    ];

    public PdfSearchToolTests()
    {
        var mock = new MockEmbeddingService();
        // Keyword-based embeddings so tests can control similarity scores
        mock.SetEmbeddingFactory(text =>
        {
            var embedding = new float[32];
            var lower = text.ToLowerInvariant();
            for (int i = 0; i < Math.Min(Keywords.Length, 32); i++)
                embedding[i] = lower.Contains(Keywords[i]) ? 1.0f : 0.0f;
            var mag = MathF.Sqrt(embedding.Sum(x => x * x));
            if (mag > 0) for (int i = 0; i < embedding.Length; i++) embedding[i] /= mag;
            else embedding[0] = 0.01f;
            return embedding;
        });
        _embeddingService = mock;
        _tool = new PdfSearchTool(
            EphemeralSearchHarness.Create(mock),
            new PassThroughFileSystemService());
        _tempDir = Path.Combine(Path.GetTempPath(), $"PdfSearchTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Helper to cast the tool result to a dictionary.</summary>
    private static Dictionary<string, object?> GetResultDict(ToolCallResponse response)
    {
        Assert.NotNull(response.Result);
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    // ── Test 1: Basic semantic search ────────────────────────────────

    [Fact]
    public async Task ShouldReturnResults_WhenQueryMatchesPdfContent()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "ml-report.pdf");
        TestPdfHelper.CreateSimplePdf(pdfPath,
            "Machine learning algorithms are used for data classification and regression tasks.",
            "Neural network models use gradient descent and backpropagation for training.",
            "This page contains unrelated content about cooking recipes and gardening tips.");

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = pdfPath,
                [ParamQuery] = "machine learning algorithm training model",
                ["top_k"] = 3,
                ["threshold"] = 0.1
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error ?? "Expected success");
        var dict = GetResultDict(result);

        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount > 0, "Expected at least one matching result");

        var filesProcessed = Convert.ToInt32(dict["files_processed"]);
        Assert.Equal(1, filesProcessed);
    }

    // ── Test 2: Page range filtering ─────────────────────────────────

    [Fact]
    public async Task ShouldRespectPageRange_WhenSpecified()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "multi-page.pdf");
        TestPdfHelper.CreateSimplePdf(pdfPath,
            "Page one: machine learning algorithms for data analysis.",
            "Page two: neural network deep learning model training.",
            "Page three: more machine learning data classification.",
            "Page four: unrelated content about nature.",
            "Page five: artificial intelligence optimization.");

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = pdfPath,
                [ParamQuery] = "machine learning data",
                ["page_range"] = "1-2",
                ["threshold"] = 0.0,
                ["top_k"] = 10
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error ?? "Expected success");
        var dict = GetResultDict(result);

        var totalPages = Convert.ToInt32(dict["total_pages"]);
        Assert.Equal(2, totalPages); // only pages 1-2 were processed
    }

    // ── Test 3: Directory search ─────────────────────────────────────

    [Fact]
    public async Task ShouldSearchDirectory_WhenPathIsFolder()
    {
        // Arrange
        TestPdfHelper.CreateSimplePdf(
            Path.Combine(_tempDir, "doc1.pdf"),
            "Machine learning algorithms for classification.");

        TestPdfHelper.CreateSimplePdf(
            Path.Combine(_tempDir, "doc2.pdf"),
            "Neural network deep learning model architecture.");

        TestPdfHelper.CreateSimplePdf(
            Path.Combine(_tempDir, "doc3.pdf"),
            "Data training and optimization techniques.");

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = _tempDir,
                [ParamQuery] = "machine learning model",
                ["threshold"] = 0.0,
                ["top_k"] = 10
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error ?? "Expected success");
        var dict = GetResultDict(result);

        var filesProcessed = Convert.ToInt32(dict["files_processed"]);
        Assert.Equal(3, filesProcessed);

        var totalChunks = Convert.ToInt32(dict["total_chunks"]);
        Assert.True(totalChunks >= 3, "Expected at least 3 chunks (one per file)");
    }

    // ── Test 4: TopK limit ───────────────────────────────────────────

    [Fact]
    public async Task ShouldRespectTopK()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "many-pages.pdf");
        TestPdfHelper.CreateSimplePdf(pdfPath,
            "Machine learning algorithm for data classification using neural networks.",
            "Deep learning model training with gradient descent optimization.",
            "Artificial intelligence and machine learning algorithms overview.",
            "Neural network backpropagation and loss function optimization.",
            "Machine learning data regression model for training.");

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = pdfPath,
                [ParamQuery] = "machine learning algorithm training",
                ["top_k"] = 2,
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error ?? "Expected success");
        var dict = GetResultDict(result);

        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount <= 2, $"Expected at most 2 results, got {resultCount}");
    }

    // ── Test 5: File does not exist ──────────────────────────────────

    [Fact]
    public async Task ShouldReturnNoResults_WhenFileDoesNotExist()
    {
        // VFS contract: an unresolvable path yields zero files processed (empty success),
        // not a hard error — the previous "File not found" failure path was removed when the
        // tool was migrated to resolve everything through IFileSystemService.
        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/nonexistent/document.pdf",
                [ParamQuery] = "search query",
                ["Threshold"] = 0.0
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = GetResultDict(result);
        Assert.Equal(0, Convert.ToInt32(dict["files_processed"]));
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    // ── Test 6: Empty query ──────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsEmpty()
    {
        var pdfPath = Path.Combine(_tempDir, "valid.pdf");
        TestPdfHelper.CreateSimplePdf(pdfPath, "Some content here.");

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = pdfPath,
                [ParamQuery] = ""
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 7: Non-PDF file ─────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnNoResults_WhenFileIsNotPdf()
    {
        // VFS contract: a non-PDF file is silently skipped during PDF resolution, yielding
        // zero files processed (empty success). The previous ".pdf required" failure path
        // was removed during the VFS migration.
        var txtPath = Path.Combine(_tempDir, "document.txt");
        await File.WriteAllTextAsync(txtPath, "This is a text file, not a PDF.", TestContext.Current.CancellationToken);

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = txtPath,
                [ParamQuery] = "search query",
                ["Threshold"] = 0.0
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = GetResultDict(result);
        Assert.Equal(0, Convert.ToInt32(dict["files_processed"]));
        Assert.Equal(0, Convert.ToInt32(dict["result_count"]));
    }

    // ── Test 8: Page number in results ───────────────────────────────

    [Fact]
    public async Task ShouldIncludePageNumber_InResults()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "paged.pdf");
        TestPdfHelper.CreateSimplePdf(pdfPath,
            "Unrelated content about cooking and gardening.",
            "Machine learning algorithm for data classification and neural network training.");

        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = pdfPath,
                [ParamQuery] = "machine learning algorithm data classification neural network",
                ["top_k"] = 1,
                ["threshold"] = 0.0
            });

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error ?? "Expected success");
        var dict = GetResultDict(result);

        var resultCount = Convert.ToInt32(dict["result_count"]);
        Assert.True(resultCount >= 1, "Expected at least one result");

        // Verify the results contain page_number field
        var results = dict["results"] as System.Collections.IList;
        Assert.NotNull(results);
        Assert.True(results.Count >= 1);

        // The top result should come from page 2 (the ML content page)
        var firstResult = results[0] as IDictionary<string, object?>;
        Assert.NotNull(firstResult);
        Assert.True(firstResult.ContainsKey("page_number"), "Result should include page_number");

        var pageNumber = Convert.ToInt32(firstResult["page_number"]);
        Assert.Equal(2, pageNumber); // ML content is on page 2
    }

    // ── Test 9: Schema validation ────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectSchema()
    {
        Assert.Equal("pdf_search", _tool.Name);
        Assert.Equal("Search", _tool.Category);
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamPath));
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamQuery));
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.False(_tool.Schema.Parameters["page_range"].Required);
        Assert.False(_tool.Schema.Parameters["top_k"].Required);
        Assert.False(_tool.Schema.Parameters["threshold"].Required);
        Assert.False(_tool.Schema.Parameters["chunk_size"].Required);
    }

    // ── Test 10: Empty path ──────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenPathIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "pdf_search",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "",
                [ParamQuery] = "some query"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cleanup ──────────────────────────────────────────────────────

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}

// ── Test PDF helper ──────────────────────────────────────────────────

internal static class TestPdfHelper
{
    public static void CreateSimplePdf(string path, params string[] pageTexts)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(text, 12, new PdfPoint(50, 700), font);
        }
        File.WriteAllBytes(path, builder.Build());
    }
}
