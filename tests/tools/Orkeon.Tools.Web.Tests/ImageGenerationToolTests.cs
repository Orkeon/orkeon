using System.Net;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

public sealed class ImageGenerationToolTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly ImageGenerationTool _tool;

    private const string ValidApiKey = "sk-test-key-1234567890";

    private const string SuccessResponseUrl = """
        {
            "created": 1589478378,
            "data": [
                {
                    "url": "https://oaidalleapiprodscus.blob.core.windows.net/image.png",
                    "revised_prompt": "A cute baby sea otter floating on its back in calm water"
                }
            ]
        }
        """;

    private const string SuccessResponseB64 = """
        {
            "created": 1589478378,
            "data": [
                {
                    "b64_json": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
                    "revised_prompt": "A cute baby sea otter floating on its back in calm water"
                }
            ]
        }
        """;

    private const string ContentPolicyErrorResponse = """
        {
            "error": {
                "code": "content_policy_violation",
                "message": "Your request was rejected as a result of our safety system.",
                "type": "invalid_request_error"
            }
        }
        """;

    private const string RateLimitErrorResponse = """
        {
            "error": {
                "code": "rate_limit_exceeded",
                "message": "Rate limit reached for dall-e-3 in organization.",
                "type": "rate_limit_error"
            }
        }
        """;

    public ImageGenerationToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
        // No test here exercises save_to_path, so a permissive (no-mount) in-memory
        // fake satisfies the now-mandatory IFileSystemService dependency.
        _tool = new ImageGenerationTool(new FakeFileSystemService(), _httpClient);
    }

    [Fact]
    public async Task ShouldReturnImageUrl_WhenPromptIsValid()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, SuccessResponseUrl);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, Convert.ToInt32(dict["image_count"]));
        Assert.Equal("dall-e-3", dict["model"]!.ToString());

        var images = dict["images"] as List<object>;
        Assert.NotNull(images);
        Assert.Single(images);

        var image = images[0] as Dictionary<string, object?>;
        Assert.NotNull(image);
        Assert.Contains("oaidalleapiprodscus", image[ParamUrl]!.ToString());
    }

    [Fact]
    public async Task ShouldReturnBase64_WhenResponseFormatIsB64()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, SuccessResponseB64);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["response_format"] = "b64_json"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var images = dict["images"] as List<object>;
        Assert.NotNull(images);
        Assert.Single(images);

        var image = images[0] as Dictionary<string, object?>;
        Assert.NotNull(image);
        Assert.NotNull(image["base64"]);
        Assert.Contains("iVBORw0KGgo", image["base64"]!.ToString());
    }

    [Fact]
    public async Task ShouldReturnRevisedPrompt_WhenDallE3()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, SuccessResponseUrl);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute otter",
                ["api_key"] = ValidApiKey,
                ["model"] = "dall-e-3"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);

        var images = dict["images"] as List<object>;
        Assert.NotNull(images);

        var image = images[0] as Dictionary<string, object?>;
        Assert.NotNull(image);
        Assert.Equal("A cute baby sea otter floating on its back in calm water", image["revised_prompt"]!.ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenPromptIsEmpty()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "",
                ["api_key"] = ValidApiKey
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Prompt", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiKeyIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("api_key", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiKeyHasInvalidFormat()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = "invalid-key-format"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("sk-", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenSizeIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["size"] = "800x600"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid size", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiReturnsContentPolicyViolation()
    {
        _mockHandler.SetResponse(HttpStatusCode.BadRequest, ContentPolicyErrorResponse);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "Some prompt that violates policy",
                ["api_key"] = ValidApiKey
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Content policy violation", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenApiReturnsRateLimit()
    {
        _mockHandler.SetResponse(HttpStatusCode.TooManyRequests, RateLimitErrorResponse);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Rate limit", result.Error);
    }

    [Fact]
    public async Task ShouldValidateNumberOfImages_ForDallE3()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["model"] = "dall-e-3",
                ["number_of_images"] = 3
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("DALL-E 3", result.Error);
        Assert.Contains("1 image", result.Error);
    }

    [Fact]
    public async Task ShouldSendCorrectAuthorizationHeader()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, SuccessResponseUrl);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey
            }
        );

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(_mockHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, _mockHandler.LastRequest!.Method);
        Assert.True(_mockHandler.LastRequest.Headers.Contains("Authorization"));
        var authValues = _mockHandler.LastRequest.Headers.GetValues("Authorization").ToList();
        Assert.Single(authValues);
        Assert.Equal($"Bearer {ValidApiKey}", authValues[0]);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQualityIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["quality"] = "ultra"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid quality", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenResponseFormatIsInvalid()
    {
        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["response_format"] = "png"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Invalid response format", result.Error);
    }

    [Fact]
    public void ShouldHaveCorrectSchemaProperties()
    {
        Assert.Equal("image_generation", _tool.Name);
        Assert.Equal("Web Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters["prompt"].Required);
        Assert.True(_tool.Schema.Parameters["api_key"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
        _tool.Dispose();
    }
}
