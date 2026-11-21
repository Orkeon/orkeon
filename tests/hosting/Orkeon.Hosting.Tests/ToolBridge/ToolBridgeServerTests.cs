using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Security;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;
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

    [Theory]
    [InlineData("""{"call_id":"c1","tool":null,"arguments":{}}""")]
    [InlineData("""{"call_id":"c1","tool":"firefox","arguments":{},"mounts":[null]}""")]
    [InlineData("""{"call_id":"c1","tool":"firefox","arguments":{},"mounts":[{"path":null}]}""")]
    [InlineData("""{"call_id":"c1","tool":"firefox","arguments":{},"files":[{"path":null,"content":""}]}""")]
    public async Task ShouldAnswerBadRequest_WhenTheCallHoldsANullWhereAValueIsRequired(string json)
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        using var client = Client(address);
        using var body = Json(json);

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShouldAnswerUnsupportedMediaType_WhenAPostIsNotJson()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        using var client = Client(address);
        using var body = new StringContent("""{"call_id":"c1","tool":"firefox","arguments":{}}""", Encoding.UTF8, "text/plain");

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task ShouldAnswerConflict_WhenACallIdIsAlreadyInFlight()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox") { WaitsForCancellation = true };
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"]);
        await using var _ = server;
        using var client = Client(address);
        const string Call = """{"call_id":"c1","tool":"firefox","arguments":{}}""";
        var first = Task.Run(async () =>
        {
            using var firstBody = Json(Call);
            using var answered = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, firstBody, TestContext.Current.CancellationToken);
            return answered.StatusCode;
        }, TestContext.Current.CancellationToken);
        await tool.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var secondBody = Json(Call);

        // Act
        using var second = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, secondBody, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var cancel = Json("""{"call_id":"c1"}""");
        using var cancelled = await client.PostAsync(ToolBridgeProtocol.CancelEndpoint, cancel, TestContext.Current.CancellationToken);
        await first;
    }

    [Fact]
    public async Task ShouldFailTheCall_WhenASentFileEscapesTheCallersMounts()
    {
        // Arrange
        using var files = new BridgeFileSystem();
        var tool = new FakeFileBridgeTool("firefox", files.Service);
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], files: files);
        await using var _ = server;
        using var client = Client(address);
        using var body = Json("""{"call_id":"c1","tool":"firefox","arguments":{},"mounts":[{"path":"/output","writable":true}],"files":[{"path":"/output/../etc/passwd","content":"AA=="}]}""");

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        var answer = await response.Content.ReadFromJsonAsync<BridgeCallResponse>(ToolBridgeProtocol.JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal((false, 0), (answer!.Success, tool.Calls.Count));
    }

    [Theory]
    [InlineData("""{"call_id":"c1","tool":"firefox","arguments":{"action":"read","read_from":"/workspace/secret.txt"}}""")]
    [InlineData("""{"call_id":"c1","tool":"firefox","arguments":{"action":"read","read_from":"/workspace/secret.txt"},"mounts":[{"path":"/output","writable":true}]}""")]
    public async Task ShouldKeepTheBridgesOwnMountsOutOfReach_WhenACallOfAToolThatUsesFilesArrives(string json)
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("orkeon-bridge-boot-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "secret.txt"), "the bridge's own file", TestContext.Current.CancellationToken);
            using var files = new BridgeFileSystem(new FileSystemMount(folder, "/workspace", FileAccessRights.ReadWrite));
            var tool = new FakeFileBridgeTool("firefox", files.Service);
            var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], files: files);
            await using var _ = server;
            using var client = Client(address);
            using var body = Json(json);

            // Act
            using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

            // Assert
            Assert.Null(Assert.Single(tool.Read));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldAuditAFailureWithACleanError_WhenTheToolThrows()
    {
        // Arrange
        var tool = new FakeBridgeTool("firefox") { Throws = new IOException("/home/someone/secret/profile is locked") };
        var audit = new FakeAuditLogger();
        var (server, address) = await BridgeLoopback.StartServerAsync([tool], ["*"], audit);
        await using var _ = server;
        using var client = Client(address);
        using var body = Json("""{"call_id":"c1","tool":"firefox","arguments":{"action":"read"}}""");

        // Act
        using var response = await client.PostAsync(ToolBridgeProtocol.CallEndpoint, body, TestContext.Current.CancellationToken);

        // Assert
        var answer = await response.Content.ReadFromJsonAsync<BridgeCallResponse>(ToolBridgeProtocol.JsonOptions, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(answer!.Success);
        Assert.Contains("IOException", answer.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/someone", answer.Error, StringComparison.Ordinal);
        Assert.Equal(AuditOutcome.Failure, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task ShouldLogTheRefusalWithoutTheToken_WhenARequestCarriesAWrongToken()
    {
        // Arrange
        var logger = new RecordingLogger();
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"], logger: logger);
        await using var _ = server;
        using var client = Client(address, "a-wrong-token-that-must-not-show");

        // Act
        using var response = await client.GetAsync(ToolBridgeProtocol.ToolsEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var refusal = Assert.Single(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.DoesNotContain("a-wrong-token", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(BridgeLoopback.Token, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldAcceptTheToken_WhenItsVariableHoldsItBetweenBlanks()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync(
            [new FakeBridgeTool("firefox")], ["*"], environment: name => name == ToolBridgeProtocol.DefaultTokenEnvVar ? $"  {BridgeLoopback.Token}\n" : null);
        await using var _ = server;
        using var client = Client(address);

        // Act
        using var response = await client.GetAsync(ToolBridgeProtocol.ToolsEndpoint, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ShouldRefuseToStart_WhenTheTokenIsShorterThanTheMinimum()
    {
        // Arrange
        var options = new ToolBridgeServerOptions();
        options.Allow.Add("*");
        await using var server = new ToolBridgeServer(new ToolRegistry([]), options, _ => "short", null, NullLogger.Instance);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => server.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("shorter than 16", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("memory_store")]
    [InlineData("caller_tool")]
    public async Task ShouldRefuseToStart_WhenAllowNamesAToolThatStaysWithTheCaller(string name)
    {
        // Arrange
        var options = new ToolBridgeServerOptions();
        options.Allow.Add(name);
        await using var server = new ToolBridgeServer(
            new ToolRegistry([new FakeCallerBridgeTool(name)]), options, BridgeLoopback.Environment, null, NullLogger.Instance);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => server.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains($"'{name}'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldServeOnlyTheBridgeableTools_WhenAllowIsTheLoneStar()
    {
        // Arrange
        IBaseTool[] tools =
        [
            new FakeBridgeTool("firefox"), new FakeCallerBridgeTool("memory_store"), new FakeUnmarkedBridgeTool("plugin_tool"),
            StubMcpTransport.Tool("mcp_tool"), new FakeWorkspaceBridgeTool(),
        ];
        var (server, address) = await BridgeLoopback.StartServerAsync(tools, ["*"]);
        await using var _ = server;
        using var client = Client(address);

        // Act
        var list = await client.GetFromJsonAsync<BridgeToolListResponse>(ToolBridgeProtocol.ToolsEndpoint, ToolBridgeProtocol.JsonOptions, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["firefox"], list!.Tools.Select(t => t.Name));
    }
}
