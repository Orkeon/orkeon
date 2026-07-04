using System.Net;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.Tests.Doubles;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Additional validation and error-mapping coverage for ImageGenerationTool.
/// </summary>
public sealed class ImageGenerationToolExtraTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler = new();
    private readonly HttpClient _httpClient;
    private readonly FakeFileSystemService _fileSystem = new();
    private readonly ImageGenerationTool _tool;
    private const string ValidApiKey = "sk-test-key-1234567890";

    public ImageGenerationToolExtraTests()
    {
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        // In-memory VFS double; writes land in the fake and are inspected per-test.
        _tool = new ImageGenerationTool(_fileSystem, _httpClient);
    }

    private static ToolCallRequest Req(Dictionary<string, object?> extra)
    {
        var p = new Dictionary<string, object?>
        {
            ["prompt"] = "A cute baby sea otter",
            ["api_key"] = ValidApiKey
        };
        foreach (var (k, v) in extra) p[k] = v;
        return new ToolCallRequest("image_generation", p);
    }

    [Fact]
    public async Task CallAsync_PromptTooLong_ReturnsValidationError()
    {
        var request = new ToolCallRequest("image_generation", new Dictionary<string, object?>
        {
            ["prompt"] = new string('a', 4001),
            ["api_key"] = ValidApiKey
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("4000", result.Error);
    }

    [Fact]
    public async Task CallAsync_NumberOfImagesOutOfRange_ReturnsValidationError()
    {
        var result = await _tool.CallAsync(Req(new() { ["model"] = "dall-e-2", ["number_of_images"] = 0 }), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("between 1 and 10", result.Error);
    }

    [Fact]
    public async Task CallAsync_DallE2_MultipleImages_Succeeds()
    {
        const string multi = """
            {
                "created": 1,
                "data": [
                    {"url": "https://img/1.png"},
                    {"url": "https://img/2.png"}
                ]
            }
            """;
        _mockHandler.SetResponse(HttpStatusCode.OK, multi);

        var result = await _tool.CallAsync(Req(new() { ["model"] = "dall-e-2", ["number_of_images"] = 2 }), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.Equal(2, Convert.ToInt32(dict!["image_count"]));
        Assert.Equal("dall-e-2", dict["model"]!.ToString());
    }

    [Fact]
    public async Task CallAsync_InvalidApiKeyErrorCode_MapsFriendlyMessage()
    {
        const string err = """{"error":{"code":"invalid_api_key","message":"bad key"}}""";
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized, err);

        var result = await _tool.CallAsync(Req(new()), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid API key", result.Error);
    }

    [Fact]
    public async Task CallAsync_BillingLimitErrorCode_MapsFriendlyMessage()
    {
        const string err = """{"error":{"code":"billing_hard_limit_reached","message":"quota"}}""";
        _mockHandler.SetResponse(HttpStatusCode.BadRequest, err);

        var result = await _tool.CallAsync(Req(new()), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Billing quota exceeded", result.Error);
    }

    [Fact]
    public async Task CallAsync_UnknownErrorCode_MapsGenericMessage()
    {
        const string err = """{"error":{"code":"some_other","message":"weird"}}""";
        _mockHandler.SetResponse(HttpStatusCode.InternalServerError, err);

        var result = await _tool.CallAsync(Req(new()), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("OpenAI API error", result.Error);
        Assert.Contains("weird", result.Error);
    }

    [Fact]
    public async Task CallAsync_NonJsonErrorBody_MapsRawMessage()
    {
        _mockHandler.SetResponse(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>");

        var result = await _tool.CallAsync(Req(new()), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("OpenAI API error", result.Error);
    }

    [Fact]
    public async Task CallAsync_Base64Image_SavesViaVfsWrite()
    {
        const string b64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var success = $$"""
            {
                "created": 1,
                "data": [
                    {"b64_json": "{{b64}}", "revised_prompt": "x"}
                ]
            }
            """;
        _mockHandler.SetResponse(HttpStatusCode.OK, success);

        // The decoded image is persisted through the VFS (no real disk I/O).
        const string savePath = "/output/out.png";

        var result = await _tool.CallAsync(Req(new()
        {
            ["response_format"] = "b64_json",
            ["save_to_path"] = savePath
        }), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var written = await _fileSystem.TryReadAllBytesAsync(savePath, TestContext.Current.CancellationToken);
        Assert.NotNull(written);
        Assert.Equal(Convert.FromBase64String(b64), written);
    }

    [Fact]
    public async Task CallAsync_SaveToPath_OutsideMount_VfsValidationError()
    {
        // A fake VFS with a single writable mount: any save path outside it is denied
        // by the VFS policy before any image is generated.
        var mountedFs = new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite);
        using var tool = new ImageGenerationTool(mountedFs, _httpClient);

        var result = await tool.CallAsync(
            Req(new() { ["save_to_path"] = "/forbidden/out.png" }),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid save_to_path", result.Error);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
