using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests;

public sealed class PdfReaderToolTests : IDisposable
{
    private readonly PdfReaderTool _tool;

    public PdfReaderToolTests()
    {
        _tool = new PdfReaderTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
    }

    [Fact]
    public async Task ShouldReturnError_WhenFileDoesNotExist()
    {
        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/nonexistent/test.pdf"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamPath, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = ""
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPdfFileIsInvalid()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "This is not a PDF file", TestContext.Current.CancellationToken);

            var request = new ToolCallRequest(
                ToolName: "pdf_reader",
                Parameters: new Dictionary<string, object?>
                {
                    [ParamPath] = tempFile
                }
            );

            var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("pdf_reader", _tool.Name);
        Assert.Equal("File Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
        Assert.False(_tool.Schema.Parameters["page_range"].Required);
    }

    [Fact]
    public async Task PdfReader_PathTraversal_ValidatorDenied_FailsFastWithoutReading()
    {
        var deniedValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Denied("path traversal detected"));

        using var tool = new PdfReaderTool(new PassThroughFileSystemService(), deniedValidator);

        var request = new ToolCallRequest(
            ToolName: "pdf_reader",
            Parameters: new Dictionary<string, object?> { [ParamPath] = "../../etc/passwd.pdf" }
        );

        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
        // The defense-in-depth validator is consulted with the resolved physical path
        // (the VFS collapses the traversal segments before delegating), so match on the
        // file name rather than the raw requested path.
        Assert.Contains(deniedValidator.Calls, c => c.Path.EndsWith("passwd.pdf", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _tool.Dispose();
    }
}
