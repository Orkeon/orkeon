using System.Net;
using System.Text;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// PR review HOST-3: an A2A listener that requires no header credential refuses what a web page could forge — a POST
/// without a JSON content type (a cross-site form) and a <c>Host</c> that does not name it (a rebound DNS name).
/// </summary>
public sealed class A2ABrowserGuardTests
{
    private const string Body = """{"id":"t-1","skillId":"researcher","input":"hi"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(HttpStatusCode Status, StubA2ATaskRouter Router)> PostAsync(
        A2ASecurityOptions? security, string mediaType, string? host = null)
    {
        var router = new StubA2ATaskRouter();
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router, security: security), Ct);
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{LoopbackPorts.Host}:{port}/a2a/tasks/send")
            {
                Content = new StringContent(Body, Encoding.UTF8, mediaType),
            };
            if (host is not null)
                request.Headers.Host = host;
            using var response = await client.SendAsync(request, Ct);
            return (response.StatusCode, router);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRefuseAPostWithoutAJsonContentType_WhenNoHeaderCredentialIsRequired()
    {
        var (status, router) = await PostAsync(null, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Null(router.LastRequest);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRefuseAPostWithoutAJsonContentType_WhenOnlyAClientCertificateIsRequired()
    {
        var security = new A2ASecurityOptions { RequireMutualTls = true };
        security.TrustedClientCertificateThumbprints.Add(new string('A', 40));

        var (status, router) = await PostAsync(security, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Null(router.LastRequest);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRefuseAHostThatDoesNotNameTheListener_WhenNoHeaderCredentialIsRequired()
    {
        var (status, router) = await PostAsync(null, "application/json", host: "rebound.example.test");

        Assert.True(
            status is (HttpStatusCode)421 or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"expected 421, 400 or the listener's own 404, got {(int)status}");
        Assert.Null(router.LastRequest);
    }

    [Fact]
    [Trait("BR", "FWK-14")]
    public async Task ShouldRouteAJsonPostOnTheLoopback_WhenNoHeaderCredentialIsRequired()
    {
        var (status, router) = await PostAsync(null, "application/json");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("t-1", router.LastRequest?.Id);
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("APPLICATION/JSON", true)]
    [InlineData("application/jsonp", false)]
    [InlineData("text/plain", false)]
    [InlineData(null, false)]
    [Trait("BR", "FWK-14")]
    public void ShouldTellAJsonContentType(string? contentType, bool json) =>
        Assert.Equal(json, BrowserRequestGuard.IsJson(contentType));

    [Fact]
    [Trait("BR", "FWK-14")]
    public void ShouldAcceptAnyHost_OnAWildcardListener()
    {
        Assert.True(BrowserRequestGuard.NamesListener("http://+:5190/", "anything.example:5190"));
        Assert.True(BrowserRequestGuard.NamesListener("http://0.0.0.0:5190/", "anything.example:5190"));
        Assert.False(BrowserRequestGuard.NamesListener("http://127.0.0.1:5190/", "anything.example:5190"));
    }
}
