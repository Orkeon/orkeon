using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class BiDiEndpointTests
{
    [Theory]
    [InlineData("WebDriver BiDi listening on ws://127.0.0.1:41233", "ws://127.0.0.1:41233/")]
    [InlineData("1730899999 Remote\tWebDriver BiDi listening on ws://127.0.0.1:9222  ", "ws://127.0.0.1:9222/")]
    [InlineData("WebDriver BiDi listening on ws://[::1]:9222 (ready)", "ws://[::1]:9222/")]
    public void Should_ReadTheEndpoint_When_TheLineAnnouncesIt(string line, string expected)
    {
        Assert.True(BiDiEndpoint.TryParse(line, out var endpoint));
        Assert.Equal(expected, endpoint.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DevTools listening on ws://127.0.0.1:9222/devtools/browser/1")]
    [InlineData("WebDriver BiDi listening on ")]
    [InlineData("WebDriver BiDi listening on http://127.0.0.1:9222")]
    [InlineData("console.warn: something else")]
    public void Should_ReadNothing_When_TheLineIsNotTheAnnouncement(string? line)
    {
        Assert.False(BiDiEndpoint.TryParse(line, out var endpoint));
        Assert.Null(endpoint);
    }

    [Fact]
    public void Should_AddressTheSessionPath_UnderTheEndpoint()
    {
        var session = BiDiEndpoint.Session(new Uri("ws://127.0.0.1:41233"));

        Assert.Equal("ws://127.0.0.1:41233/session", session.ToString());
    }
}
