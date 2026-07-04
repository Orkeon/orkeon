using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// SSRF regression tests (R2.3 / SEC-003, SEC-004). These prove that the web tools
/// route every outbound URL through the fail-closed default guard (or an injected
/// <see cref="IUrlValidator"/>) and reject internal/metadata/private addresses.
/// They fail on the pre-remediation baseline where only the scheme was checked.
/// </summary>
public class WebToolsSsrfTests
{
    // Cloud metadata endpoint, loopback and an RFC1918 literal — all must be rejected
    // by the fail-closed default guard without any DNS lookup.
    public static TheoryData<string> BlockedUrls => new()
    {
        "http://169.254.169.254/latest/meta-data/iam/security-credentials/",
        "http://127.0.0.1/admin",
        "http://10.0.0.1/internal",
        "http://192.168.1.1/router",
        "http://localhost/secret",
    };

    [Theory]
    [MemberData(nameof(BlockedUrls))]
    public async Task WebScrape_ShouldReject_WhenUrlTargetsInternalAddress(string url)
    {
        using var handler = new ThrowingHandler();
        using var httpClient = new HttpClient(handler);
        using var tool = new WebScrapeTool(httpClient: httpClient);

        var result = await tool.CallAsync(new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?> { [ParamUrl] = url }), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(BlockedUrls))]
    public async Task ScrapeElement_ShouldReject_WhenUrlTargetsInternalAddress(string url)
    {
        using var handler = new ThrowingHandler();
        using var httpClient = new HttpClient(handler);
        using var tool = new ScrapeElementTool(httpClient);

        var result = await tool.CallAsync(new ToolCallRequest(
            ToolName: "scrape_element",
            Parameters: new Dictionary<string, object?>
            {
                [ParamUrl] = url,
                ["css_selector"] = "div",
            }), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImageGeneration_ShouldReject_WhenReturnedImageUrlIsInternal()
    {
        // DALL-E API returns a poisoned image URL pointing at the metadata endpoint.
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(System.Net.HttpStatusCode.OK,
            "{\"created\":1,\"data\":[{\"url\":\"http://169.254.169.254/latest/meta-data/\"}]}");
        using var http = new HttpClient(handler);

        // url validator that mirrors the real metadata block.
        var validator = new StubUrlValidator(u =>
            u.Contains("169.254", StringComparison.Ordinal)
                ? UrlValidationResult.Denied("private/reserved IP")
                : UrlValidationResult.Allowed(new Uri(u)));

        var fs = new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadOnly | FileAccessRights.Write);
        using var tool = new ImageGenerationTool(
            fs,
            validator,
            new Orkeon.Tools.Abstractions.Security.HttpHeaderSanitizer(),
            http);

        var result = await tool.CallAsync(new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "an otter",
                ["api_key"] = "sk-test",
                ["save_to_path"] = "/output/img.png",
            }), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebScrape_ShouldAllow_WhenUrlValidatorApprovesPublicUrl()
    {
        // Non-regression: a legitimate public URL approved by the validator is fetched.
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(System.Net.HttpStatusCode.OK,
            "<html><head><title>OK</title></head><body>hello world</body></html>");
        using var http = new HttpClient(handler);

        var validator = new StubUrlValidator(u => UrlValidationResult.Allowed(new Uri(u)));
        using var tool = new WebScrapeTool(
            validator, new Orkeon.Tools.Abstractions.Security.HttpHeaderSanitizer(), http);

        var result = await tool.CallAsync(new ToolCallRequest(
            ToolName: "web_scrape",
            Parameters: new Dictionary<string, object?> { [ParamUrl] = "https://example.com/article" }), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = Assert.IsType<Dictionary<string, object?>>(result.Result);
        Assert.Contains("hello world", (string)dict["content"]!, StringComparison.Ordinal);
    }

    private sealed class StubUrlValidator(Func<string, UrlValidationResult> map) : IUrlValidator
    {
        public Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default)
            => Task.FromResult(map(url.ToString()));
    }

    /// <summary>Fails the test loudly if any outbound request is issued for a blocked URL.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                $"Outbound request must not be issued for a blocked URL: {request.RequestUri}");
    }
}
