using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Security;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Tools;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>#445 - the serving side: the token, the Origin header, the body limit, the list, the raw call and its audit.</summary>
public sealed class ToolBridgeServerTests
{
    private static HttpClient Client(Uri address, string? bearer)
    {
        var client = new HttpClient { BaseAddress = new Uri(address, ToolBridgeProtocol.BasePath) };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private static HttpClient Client(Uri address) => Client(address, BridgeLoopback.Token);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task ShouldRefuseTheRequest_WhenItCarriesAnOriginHeader()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        using var client = Client(address);
        using var request = new HttpRequestMessage(HttpMethod.Get, ToolBridgeProtocol.ToolsEndpoint);
        request.Headers.Add("Origin", "https://example.org");

        // Act
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-one")]
    public async Task ShouldRefuseTheRequest_WhenTheTokenIsMissingOrWrong(string? bearer)
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        using var client = Client(address, bearer);

        // Act
        using var response = await client.GetAsync(ToolBridgeProtocol.ToolsEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShouldRefuseTheBody_WhenItExceedsTheLimit()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"], maxRequestBytes: 64);
        await using var _ = server;
        using var client = Client(address);
        var text = new string('x', 200);
        using var body = Json("{\"call_id\":\"1\",\"tool\":\"firefox\",\"arguments\":{\"text\":\"" + text + "\"}}");

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task ShouldListTheServedToolsWithTheirHashes_WhenAskedForTheTools()
    {
        // Arrange
        var firefox = new FakeBridgeTool("firefox");
        var (server, address) = await BridgeLoopback.StartServerAsync([firefox, new FakeBridgeTool("teams_chat")], ["firefox"]);
        await using var _ = server;
        using var client = Client(address);

        // Act
        var list = await client.GetFromJsonAsync<BridgeToolListResponse>(
            ToolBridgeProtocol.ToolsEndpoint, ToolBridgeProtocol.JsonOptions, TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(list!.Tools);
        Assert.Equal(("firefox", ToolBridgeContract.HashOf(firefox)), (served.Name, served.ContractHash));
    }

    [Fact]
    public async Task ShouldRunTheToolRawAndAuditItUnderTheBridge_WhenACallArrives()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox") { Response = new ToolCallResponse(true, new string('r', 50_000), null) };
        var audit = new FakeAuditLogger();
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], audit);
        await using var _ = server;
        using var client = Client(address);
        using var body = Json("""{"call_id":"c1","tool":"firefox","arguments":{"action":"read","text":"hi"}}""");

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        var answer = await response.Content.ReadFromJsonAsync<BridgeCallResponse>(ToolBridgeProtocol.JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal(50_000, answer!.Result!.Value.GetString()!.Length);
        Assert.Equal("hi", Assert.Single(tool.Calls).Parameters["text"]);
        var recorded = Assert.Single(audit.Events);
        Assert.Equal((AuditCategory.ToolExecution, "c1", "bridge"), (recorded.Category, recorded.CorrelationId, recorded.AgentRole));
    }

    [Fact]
    public async Task ShouldRefuseTheCall_WhenTheToolIsNotServed()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox"), new FakeBridgeTool("teams_chat")], ["firefox"]);
        await using var _ = server;
        using var client = Client(address);
        using var body = Json("""{"call_id":"c1","tool":"teams_chat","arguments":{}}""");

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ShouldRefuseToStart_WhenTheTokenVariableIsNotSet()
    {
        // Arrange
        var options = new ToolBridgeServerOptions { TokenEnvVar = "ORKEON_TEST_BRIDGE_UNSET" };
        options.Allow.Add("*");
        await using var server = new ToolBridgeServer(new ToolRegistry([]), options, _ => null, null, NullLogger.Instance);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => server.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ORKEON_TEST_BRIDGE_UNSET", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseToStartWithThePrefix_WhenAnotherListenerHoldsIt()
    {
        // Arrange
        var (first, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = first;
        var options = new ToolBridgeServerOptions { Prefix = address.ToString() };
        options.Allow.Add("*");
        await using var second = new ToolBridgeServer(new ToolRegistry([new FakeBridgeTool("firefox")]), options, BridgeLoopback.Environment, null, NullLogger.Instance);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => second.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(address.ToString(), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseAnHttpsPrefix_WhenThePlatformIsNotWindows()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "HTTP.sys serves https under Windows.");
        var options = new ToolBridgeServerOptions { Prefix = "https://127.0.0.1:7412/" };
        options.Allow.Add("*");
        await using var server = new ToolBridgeServer(new ToolRegistry([new FakeBridgeTool("firefox")]), options, BridgeLoopback.Environment, null, NullLogger.Instance);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => server.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("Windows only", refusal.Message, StringComparison.Ordinal);
    }
}
