using System.Net;
using System.Text.Json;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> that returns configurable responses.
/// </summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode StatusCode, string Content)> _responses = [];

    public void SetResponse(string url, HttpStatusCode statusCode, string content)
    {
        _responses[url] = (statusCode, content);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";

        if (_responses.TryGetValue(url, out var response))
        {
            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Content, System.Text.Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found")
        });
    }
}

/// <summary>
/// Fake <see cref="IHttpClientFactory"/> backed by a <see cref="FakeHttpMessageHandler"/>.
/// </summary>
internal class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly FakeHttpMessageHandler _handler;

    public FakeHttpClientFactory(FakeHttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

public sealed class A2AAgentDiscoveryTests : IDisposable
{
    private static readonly string[] MultipleAgentUrls =
    [
        "http://agent1:5002",
        "http://agent2:5002",
        "http://agent3:5002"
    ];

    private readonly FakeHttpMessageHandler _handler;
    private readonly A2AAgentDiscovery _discovery;

    public A2AAgentDiscoveryTests()
    {
        _handler = new FakeHttpMessageHandler();
        var factory = new FakeHttpClientFactory(_handler);
        _discovery = new A2AAgentDiscovery(factory);
    }

    [Fact]
    public async Task DiscoverAsync_ShouldReturnAgentCard_WhenEndpointIsAvailable()
    {
        // Arrange
        var card = new AgentCard
        {
            Name = "RemoteAgent",
            Description = "A remote test agent",
            Url = new Uri("http://remote:5002"),
            Skills =
            [
                new AgentSkill
                {
                    Id = "research",
                    Name = "Researcher",
                    Description = "Does research"
                }
            ]
        };

        _handler.SetResponse(
            "http://remote:5002/.well-known/agent.json",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(card));

        // Act
        var result = await _discovery.DiscoverAsync(new Uri("http://remote:5002"), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("RemoteAgent", result!.Name);
        Assert.Equal("A remote test agent", result.Description);
        Assert.Single(result.Skills);
        Assert.Equal("research", result.Skills[0].Id);
    }

    [Fact]
    public async Task DiscoverAsync_ShouldReturnNull_When404()
    {
        // Arrange — no response configured, handler returns 404 by default

        // Act
        var result = await _discovery.DiscoverAsync(new Uri("http://unknown-agent:5002"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DiscoverAsync_ShouldReturnNull_WhenServerReturns500()
    {
        // Arrange
        _handler.SetResponse(
            "http://broken:5002/.well-known/agent.json",
            HttpStatusCode.InternalServerError,
            "Internal Server Error");

        // Act
        var result = await _discovery.DiscoverAsync(new Uri("http://broken:5002"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DiscoverMultipleAsync_ShouldReturnOnlySuccessful()
    {
        // Arrange
        var card1 = new AgentCard { Name = "Agent1", Url = new Uri("http://agent1:5002") };
        var card2 = new AgentCard { Name = "Agent2", Url = new Uri("http://agent2:5002") };

        _handler.SetResponse(
            "http://agent1:5002/.well-known/agent.json",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(card1));

        // agent2 returns 404 (no response set)

        _handler.SetResponse(
            "http://agent3:5002/.well-known/agent.json",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(card2));

        // Act
        var results = await _discovery.DiscoverMultipleAsync(MultipleAgentUrls, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "Agent1");
        Assert.Contains(results, c => c.Name == "Agent2");
    }

    [Fact]
    public async Task DiscoverAsync_ShouldHandleTrailingSlashInUrl()
    {
        // Arrange
        var card = new AgentCard { Name = "SlashAgent" };
        _handler.SetResponse(
            "http://agent:5002/.well-known/agent.json",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(card));

        // Act — URL with trailing slash
        var result = await _discovery.DiscoverAsync(new Uri("http://agent:5002/"), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("SlashAgent", result!.Name);
    }

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }
}
