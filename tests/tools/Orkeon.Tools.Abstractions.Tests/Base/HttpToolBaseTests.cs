using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Security;

namespace Orkeon.Tools.Abstractions.Tests.Base;

public class HttpToolBaseTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFactoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FactoryHttpTool(null!));
    }

    [Fact]
    public void Constructor_ShouldUseFactoryClient_WhenFactoryProvided()
    {
        var factory = new StubHttpClientFactory();
        using var tool = new FactoryHttpTool(factory);

        Assert.True(factory.CreateClientCalled);
        Assert.Equal("Web Operations", tool.Category);
    }

    [Fact]
    public void Category_ShouldDefaultToWebOperations_WhenNoContract()
    {
        using var tool = new SimpleHttpTool();

        Assert.Equal("Web Operations", tool.Category);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    public async Task ValidateUrlAsync_ShouldDeny_WhenSchemeInvalidOrMalformed(string url)
    {
        using var tool = new SimpleHttpTool();

        var result = await tool.TestValidateUrlAsync(url);

        Assert.False(result.IsAllowed);
    }

    // Fail-closed default guard (R2.3): with no IUrlValidator, internal/metadata/private
    // addresses must be rejected — the validator no longer fail-opens on scheme alone.
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://10.0.0.1/internal")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://localhost/secret")]
    public async Task ValidateUrlAsync_ShouldDenyPrivateAddress_WhenNoValidatorConfigured(string url)
    {
        using var tool = new SimpleHttpTool();

        var result = await tool.TestValidateUrlAsync(url);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
    }

    [Fact]
    public async Task ValidateUrlAsync_ShouldDenyEmbeddedCredentials_WhenNoValidatorConfigured()
    {
        using var tool = new SimpleHttpTool();

        var result = await tool.TestValidateUrlAsync("http://user:pass@8.8.8.8/");

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task ValidateUrlAsync_ShouldDelegateToValidator_WhenConfigured()
    {
        var validator = new StubUrlValidator(UrlValidationResult.Denied("blocked by policy"));
        using var tool = new SimpleHttpTool(validator, new HttpHeaderSanitizer());

        var result = await tool.TestValidateUrlAsync("https://internal.local");

        Assert.False(result.IsAllowed);
        Assert.Equal("blocked by policy", result.DenialReason);
        Assert.Equal("https://internal.local", validator.LastUrl);
    }

    [Fact]
    public void SanitizeHeaders_ShouldReturnNull_WhenNoSanitizerConfigured()
    {
        using var tool = new SimpleHttpTool();

        var result = tool.TestSanitizeHeaders(new Dictionary<string, string> { ["X-Test"] = "v" });

        Assert.Null(result);
    }

    [Fact]
    public void SanitizeHeaders_ShouldRemoveForbiddenHeaders_WhenSanitizerConfigured()
    {
        var sanitizer = new HttpHeaderSanitizer();
        using var tool = new SimpleHttpTool(new StubUrlValidator(UrlValidationResult.Allowed(new Uri("https://x"))), sanitizer);

        var result = tool.TestSanitizeHeaders(new Dictionary<string, string>
        {
            ["Host"] = "evil.com",
            ["X-Custom"] = "ok"
        });

        Assert.NotNull(result);
        Assert.False(result!.SanitizedHeaders.ContainsKey("Host"));
        Assert.True(result.SanitizedHeaders.ContainsKey("X-Custom"));
    }

    [Fact]
    public void Dispose_ShouldNotThrow_WhenUsingSharedClient()
    {
        using var tool = new SimpleHttpTool();

        var ex = Record.Exception(() => tool.Dispose());

        Assert.Null(ex);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public bool CreateClientCalled { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateClientCalled = true;
            return new HttpClient();
        }
    }

    private sealed class StubUrlValidator(UrlValidationResult result) : IUrlValidator
    {
        public string? LastUrl { get; private set; }

        public Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default)
        {
            LastUrl = url.OriginalString;
            return Task.FromResult(result);
        }
    }

    private sealed class FactoryHttpTool : HttpToolBase
    {
        public FactoryHttpTool(IHttpClientFactory factory) : base(factory) { }

        protected override Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ToolCallResponse(true, null, null));
    }

    private sealed class SimpleHttpTool : HttpToolBase
    {
        public SimpleHttpTool() : base((HttpClient?)null) { }
        public SimpleHttpTool(IUrlValidator validator, HttpHeaderSanitizer sanitizer) : base(validator, sanitizer) { }

        public Task<UrlValidationResult> TestValidateUrlAsync(string url) =>
            ValidateUrlAsync(new Uri(url, UriKind.RelativeOrAbsolute), CancellationToken.None);

        public HeaderSanitizationResult? TestSanitizeHeaders(IDictionary<string, string> headers) => SanitizeHeaders(headers);

        protected override Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ToolCallResponse(true, null, null));
    }
}
