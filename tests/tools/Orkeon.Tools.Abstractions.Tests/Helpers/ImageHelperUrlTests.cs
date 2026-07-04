using System.Net;
using System.Net.Http.Headers;
using Orkeon.Tools.Abstractions.Helpers;

namespace Orkeon.Tools.Abstractions.Tests.Helpers;

public class ImageHelperUrlTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public async Task LoadFromUrlAsync_ShouldUseContentTypeHeader_WhenPresent()
    {
        using var handler = new StubHandler(PngBytes, "image/png");
        using var client = new HttpClient(handler);

        var result = await ImageHelper.LoadFromUrlAsync(client, new Uri("https://example.com/x.png"), TestContext.Current.CancellationToken);

        Assert.Equal("image/png", result.MimeType);
        Assert.NotNull(result.Data);
        Assert.Equal(PngBytes.Length, result.Data.Count);
    }

    [Fact]
    public async Task LoadFromUrlAsync_ShouldFallBackToMagicBytes_WhenContentTypeMissing()
    {
        using var handler = new StubHandler(PngBytes, contentType: null);
        using var client = new HttpClient(handler);

        var result = await ImageHelper.LoadFromUrlAsync(client, new Uri("https://example.com/x"), TestContext.Current.CancellationToken);

        // No content-type header => DetectMimeType from PNG magic bytes.
        Assert.Equal("image/png", result.MimeType);
    }

    [Fact]
    public async Task LoadFromUrlAsync_ShouldThrow_WhenResponseIsNotSuccessful()
    {
        using var handler = new StubHandler([], contentType: null, statusCode: HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => ImageHelper.LoadFromUrlAsync(client, new Uri("https://example.com/missing.png"), TestContext.Current.CancellationToken));
    }

    private sealed class StubHandler(byte[] body, string? contentType, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(body);
            if (contentType is not null)
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
        }
    }
}
