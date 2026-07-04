using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests;

/// <summary>
/// Happy-path and edge-case coverage for <see cref="PdfReaderTool"/> using real PDFs
/// generated in-memory via PdfPig's writer. No network, no Docker.
/// </summary>
public sealed class PdfReaderToolHappyPathTests : IDisposable
{
    private readonly PdfReaderTool _tool;
    private readonly string _tempDir;

    public PdfReaderToolHappyPathTests()
    {
        _tool = new PdfReaderTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _tempDir = Path.Combine(Path.GetTempPath(), $"PdfReaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private static void CreatePdf(string path, params string[] pageTexts)
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

    private static Dictionary<string, object?> GetResultDict(ToolCallResponse response)
    {
        Assert.NotNull(response.Result);
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    [Fact]
    public async Task ShouldReadAllPages_WhenNoPageRangeGiven()
    {
        var path = Path.Combine(_tempDir, "all.pdf");
        CreatePdf(path, "First page hello world.", "Second page goodbye.", "Third page final.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = path });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(3, Convert.ToInt32(dict["total_pages"]));
        Assert.Equal(3, Convert.ToInt32(dict["pages_read"]));
        Assert.Equal("all", dict["page_range"]?.ToString());

        var content = dict["content"]?.ToString() ?? "";
        Assert.Contains("First page", content);
        Assert.Contains("Third page", content);

        var pages = dict["pages"] as System.Collections.IList;
        Assert.NotNull(pages);
        Assert.Equal(3, pages.Count);
    }

    [Fact]
    public async Task ShouldReadOnlySpecifiedRange_WhenHyphenRangeGiven()
    {
        var path = Path.Combine(_tempDir, "range.pdf");
        CreatePdf(path, "Page one.", "Page two.", "Page three.", "Page four.", "Page five.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = path,
                ["page_range"] = "2-4"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(5, Convert.ToInt32(dict["total_pages"]));
        Assert.Equal(3, Convert.ToInt32(dict["pages_read"]));
        Assert.Equal("2-4", dict["page_range"]?.ToString());

        var content = dict["content"]?.ToString() ?? "";
        Assert.DoesNotContain("Page one", content);
        Assert.Contains("Page two", content);
        Assert.Contains("Page four", content);
        Assert.DoesNotContain("Page five", content);
    }

    [Fact]
    public async Task ShouldReadCommaSeparatedPages_WhenListGiven()
    {
        var path = Path.Combine(_tempDir, "list.pdf");
        CreatePdf(path, "Alpha.", "Beta.", "Gamma.", "Delta.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = path,
                ["page_range"] = "1,3"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(2, Convert.ToInt32(dict["pages_read"]));
        var content = dict["content"]?.ToString() ?? "";
        Assert.Contains("Alpha", content);
        Assert.Contains("Gamma", content);
        Assert.DoesNotContain("Beta", content);
    }

    [Fact]
    public async Task ShouldReadFromStartToEnd_WhenOpenEndedRangeGiven()
    {
        var path = Path.Combine(_tempDir, "open.pdf");
        CreatePdf(path, "One.", "Two.", "Three.", "Four.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = path,
                ["page_range"] = "3-"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(2, Convert.ToInt32(dict["pages_read"]));
        var content = dict["content"]?.ToString() ?? "";
        Assert.Contains("Three", content);
        Assert.Contains("Four", content);
        Assert.DoesNotContain("One", content);
    }

    [Fact]
    public async Task ShouldClampOutOfRangePages_WhenRangeExceedsTotal()
    {
        var path = Path.Combine(_tempDir, "clamp.pdf");
        CreatePdf(path, "Solo page only.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = path,
                ["page_range"] = "1-99"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(1, Convert.ToInt32(dict["pages_read"]));
    }

    [Fact]
    public async Task ShouldIgnoreInvalidPageNumbers_WhenOutsideBounds()
    {
        var path = Path.Combine(_tempDir, "invalidnums.pdf");
        CreatePdf(path, "First.", "Second.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = path,
                // 0 and 99 are out of bounds; only page 2 is valid.
                ["page_range"] = "0,2,99"
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(1, Convert.ToInt32(dict["pages_read"]));
        var content = dict["content"]?.ToString() ?? "";
        Assert.Contains("Second", content);
    }

    [Fact]
    public async Task ShouldIncludePageDimensions_InPerPageInfo()
    {
        var path = Path.Combine(_tempDir, "dims.pdf");
        CreatePdf(path, "Dimension check page.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = path });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        var pages = dict["pages"] as System.Collections.IList;
        Assert.NotNull(pages);
        var firstPage = pages[0] as IDictionary<string, object?>;
        Assert.NotNull(firstPage);
        Assert.Equal(1, Convert.ToInt32(firstPage["page_number"]));
        Assert.True(Convert.ToDouble(firstPage["width"]) > 0);
        Assert.True(Convert.ToDouble(firstPage["height"]) > 0);
    }

    [Fact]
    public async Task ShouldHandleEmptyPageRangeString_AsAllPages()
    {
        var path = Path.Combine(_tempDir, "emptyrange.pdf");
        CreatePdf(path, "A.", "B.");

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = path,
                ["page_range"] = "   "
            });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "expected success");
        var dict = GetResultDict(result);
        Assert.Equal(2, Convert.ToInt32(dict["pages_read"]));
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
