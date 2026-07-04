using Microsoft.Extensions.Logging.Abstractions;
using HttpHeaderSanitizerSut = Orkeon.Tools.Abstractions.Security.HttpHeaderSanitizer;

namespace Orkeon.Infrastructure.Tests.Security;

public class HttpHeaderSanitizerTests
{
    private readonly HttpHeaderSanitizerSut _sanitizer = new(NullLogger<HttpHeaderSanitizerSut>.Instance);

    [Fact]
    public void ShouldPassWithoutModification_WhenHeadersAreNormal()
    {
        var headers = new Dictionary<string, string>
        {
            { "Accept", "application/json" },
            { "Content-Type", "application/json" },
            { "X-Custom-Header", "value123" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Equal(3, result.SanitizedHeaders.Count);
        Assert.Equal("application/json", result.SanitizedHeaders["Accept"]);
        Assert.Equal("application/json", result.SanitizedHeaders["Content-Type"]);
        Assert.Equal("value123", result.SanitizedHeaders["X-Custom-Header"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ShouldRemoveHeader_WhenHostHeaderPresent()
    {
        var headers = new Dictionary<string, string>
        {
            { "Host", "evil.com" },
            { "Accept", "text/html" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Single(result.SanitizedHeaders);
        Assert.False(result.SanitizedHeaders.ContainsKey("Host"));
        Assert.True(result.SanitizedHeaders.ContainsKey("Accept"));
        Assert.Contains(result.Warnings, w => w.Contains("Host"));
    }

    [Fact]
    public void ShouldRemoveHeader_WhenValueContainsCRLF()
    {
        var headers = new Dictionary<string, string>
        {
            { "X-Injected", "value\r\nEvil-Header: injected" },
            { "Accept", "text/html" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Single(result.SanitizedHeaders);
        Assert.False(result.SanitizedHeaders.ContainsKey("X-Injected"));
        Assert.Contains(result.Warnings, w => w.Contains("CRLF"));
    }

    [Fact]
    public void ShouldRemoveHeader_WhenValueContainsEncodedCRLF()
    {
        var headers = new Dictionary<string, string>
        {
            { "X-Injected", "value%0d%0aEvil-Header: injected" },
            { "Accept", "text/html" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Single(result.SanitizedHeaders);
        Assert.False(result.SanitizedHeaders.ContainsKey("X-Injected"));
        Assert.Contains(result.Warnings, w => w.Contains("CRLF"));
    }

    [Fact]
    public void ShouldGenerateWarning_WhenAuthorizationHeaderPresent()
    {
        var headers = new Dictionary<string, string>
        {
            { "Authorization", "Bearer token123" },
            { "Accept", "application/json" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Equal(2, result.SanitizedHeaders.Count);
        Assert.True(result.SanitizedHeaders.ContainsKey("Authorization"));
        Assert.Contains(result.Warnings, w => w.Contains("Authorization") && w.Contains("Sensitive"));
    }

    [Fact]
    public void ShouldRemoveHeader_WhenCookieHeaderPresent()
    {
        var headers = new Dictionary<string, string>
        {
            { "Cookie", "session=abc123" },
            { "Accept", "text/html" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Single(result.SanitizedHeaders);
        Assert.False(result.SanitizedHeaders.ContainsKey("Cookie"));
        Assert.Contains(result.Warnings, w => w.Contains("Cookie"));
    }

    [Fact]
    public void ShouldReturnEmptyResult_WhenDictionaryIsEmpty()
    {
        var headers = new Dictionary<string, string>();

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Empty(result.SanitizedHeaders);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ShouldReturnEmptyResult_WhenDictionaryIsNull()
    {
        var result = _sanitizer.SanitizeHeaders(null!);

        Assert.Empty(result.SanitizedHeaders);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("Transfer-Encoding")]
    [InlineData("Connection")]
    [InlineData("Upgrade")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Proxy-Connection")]
    [InlineData("Set-Cookie")]
    [InlineData("Trailer")]
    [InlineData("TE")]
    [InlineData("Content-Length")]
    public void ShouldRemoveHeader_WhenHeaderIsForbidden(string headerName)
    {
        var headers = new Dictionary<string, string>
        {
            { headerName, "some-value" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Empty(result.SanitizedHeaders);
        Assert.Single(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Forbidden"));
    }

    [Theory]
    [InlineData("X-Api-Key")]
    [InlineData("X-Auth-Token")]
    public void ShouldPassWithWarning_WhenHeaderIsSensitive(string headerName)
    {
        var headers = new Dictionary<string, string>
        {
            { headerName, "secret-value" }
        };

        var result = _sanitizer.SanitizeHeaders(headers);

        Assert.Single(result.SanitizedHeaders);
        Assert.True(result.SanitizedHeaders.ContainsKey(headerName));
        Assert.Single(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Sensitive"));
    }
}
