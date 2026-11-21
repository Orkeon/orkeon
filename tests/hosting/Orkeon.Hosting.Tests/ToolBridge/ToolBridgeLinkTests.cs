using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Network;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>
/// #445 - the caller's side, against a real server in the same process: what is bridged, the refusals of the start, a
/// lost bridge and the cancellation of a call.
/// </summary>
public sealed class ToolBridgeLinkTests
{
    private static ToolCallRequest Read(string tool) => new(tool, new Dictionary<string, object?> { ["action"] = "read" });

    [Fact]
    public async Task ShouldRunTheCallOnTheBridge_WhenTheToolIsBridged()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox") { Response = new ToolCallResponse(true, "from the bridge", null) };
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"]);
        await using var _ = server;
        var local = new FakeBridgeTool("firefox") { Response = new ToolCallResponse(true, "local", null) };
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);

        // Act
        var response = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(("from the bridge", 0), (response.Result, local.Calls.Count));
    }

    [Fact]
    public async Task ShouldBridgeWhatTheBridgeServes_WhenAllowIsTheLoneStar()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox"), new FakeBridgeTool("teams_chat")]);

        // Act
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "*"));

        // Assert
        Assert.Equal(["firefox"], link.BridgedTools);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenTheBridgeCannotBeReached()
    {
        // Arrange
        using var refusing = LoopbackPorts.Refusing();
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        var options = BridgeLoopback.ClientOptions(LoopbackPorts.BaseAddress(refusing.Port), "firefox");

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(() => BridgeLoopback.ConnectAsync(registry, options));

        // Assert
        Assert.Contains("cannot be reached", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenTheBridgeRefusesTheToken()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"), token: "wrong"));

        // Assert
        Assert.Contains("refused the token", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenANamedToolIsNotServed()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox"), new FakeBridgeTool("teams_chat")]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "teams_chat")));

        // Assert
        Assert.Contains("'teams_chat'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheStart_WhenAContractHashDiffers()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox") { WriteAccess = ToolAccess.Execute }]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox")));

        // Assert
        Assert.Contains("contract of 'firefox' differs", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepTheLocalAccessClasses_WhenAToolIsBridged()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var local = new FakeBridgeTool("firefox");
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));

        // Act
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(local, Assert.IsType<ToolBridgeProxy>(bridged).Inner);
        Assert.Equal(ToolAccess.Edit, bridged!.AccessFor(new Dictionary<string, object?> { ["action"] = "write" }));
    }

    [Fact]
    public async Task ShouldFailTheCallWithTheReason_WhenTheBridgeIsLost()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        var local = new FakeBridgeTool("firefox");
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);
        await server.DisposeAsync();

        // Act
        var response = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(nameof(ToolBridgeFailure.Unreachable), response.Metadata![ToolBridgeProxy.BridgeFailureKey]);
        Assert.Empty(local.Calls);
    }

    [Fact]
    public async Task ShouldReachTheBridgeAgain_WhenItComesBackAfterAFailedCall()
    {
        // Arrange
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);
        await server.DisposeAsync();
        _ = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);
        var (again, _) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"], port: address.Port);
        await using var __ = again;

        // Act
        var response = await bridged.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.Success, response.Error);
    }

    [Fact]
    public async Task ShouldCancelTheCallOnTheBridge_WhenTheCallerCancelsIt()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox") { WaitsForCancellation = true };
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var call = bridged!.CallAsync(Read("firefox"), cancellation.Token);
        await remote.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await cancellation.CancelAsync();

        // Assert
        var response = await call.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(true, response.Metadata!["cancelled"]);
        await remote.Cancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    private static string ListOf(string tools, string? window = null) =>
        "{\"tools\":" + tools + (window is null ? "" : ",\"heartbeat_window_ms\":" + window) + "}";

    private static string Served(IBaseTool tool) =>
        "{\"name\":\"" + tool.Name + "\",\"contract_hash\":\"" + ToolBridgeContract.HashOf(tool) + "\"}";

    public static TheoryData<string, string> HostileLists() => new()
    {
        { "twice", "more than once" },
        { "nameless", "names no tool" },
        { "window:1", "heartbeat window of 1 ms" },
        { "window:9223372036854775807", "heartbeat window of 9223372036854775807 ms" },
        { "window:0", "heartbeat window of 0 ms" },
    };

    [Theory]
    [MemberData(nameof(HostileLists))]
    public async Task ShouldRefuseTheStartWithAReason_WhenTheBridgeSendsAListItCannotTakeFor(string kind, string expected)
    {
        // Arrange
        var local = new FakeBridgeTool("firefox");
        await using var bridge = new StubBridgeServer();
        bridge.Bodies[ToolBridgeProtocol.ToolsEndpoint] = kind switch
        {
            "twice" => ListOf("[" + Served(local) + "," + Served(new FakeBridgeTool("FIREFOX")) + "]"),
            "nameless" => ListOf("[{\"name\":null,\"contract_hash\":\"x\"}]"),
            _ => ListOf("[" + Served(local) + "]", kind["window:".Length..]),
        };
        var registry = new ToolRegistry([local]);

        // Act
        var refusal = await Assert.ThrowsAsync<RunnerSettingsException>(
            () => BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(bridge.Address, "firefox")));

        // Assert
        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldTruncateTheBridgedResultLocally_WhenTheBridgeSaysItIsAlreadyBounded()
    {
        // Arrange
        var local = new FakeBridgeTool("firefox");
        await using var bridge = new StubBridgeServer();
        bridge.Bodies[ToolBridgeProtocol.ToolsEndpoint] = ListOf("[" + Served(local) + "]");
        bridge.Bodies[ToolBridgeProtocol.CallEndpoint] = """{"success":true,"result":"long","already_bounded":true}""";
        var registry = new ToolRegistry([local]);
        using var link = await BridgeLoopback.ConnectAsync(registry, BridgeLoopback.ClientOptions(bridge.Address, "firefox"));
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);

        // Act
        var response = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(("long", false), (response.Result, response.AlreadyBounded));
    }

    [Fact]
    public async Task ShouldFailTheCallWithTheReason_WhenTheAnswerExceedsTheCallersResponseLimit()
    {
        // Arrange
        var remote = new FakeBridgeTool("firefox") { Response = new ToolCallResponse(true, new string('r', 50_000), null) };
        var (server, address) = await BridgeLoopback.StartServerAsync([remote], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        var options = BridgeLoopback.ClientOptions(address, "firefox");
        options.MaxResponseBytes = 4096;
        using var link = await BridgeLoopback.ConnectAsync(registry, options);
        var bridged = await registry.GetToolByNameAsync("firefox", TestContext.Current.CancellationToken);

        // Act
        var response = await bridged!.CallAsync(Read("firefox"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Orkeon:Tools:Bridge:MaxResponseBytes", response.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldBeAnsweredOnlyWithTheHostHeaderOfThePrefix_WhenTheAddressNamesTheBridgeUnderAnotherName(bool withHeader)
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The test listener's prefix names localhost under Windows.");
        var (server, address) = await BridgeLoopback.StartServerAsync([new FakeBridgeTool("firefox")], ["*"]);
        await using var _ = server;
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        var options = BridgeLoopback.ClientOptions(new Uri($"http://localhost:{address.Port}/"), "firefox");
        options.HostHeader = withHeader ? $"127.0.0.1:{address.Port}" : null;

        // Act
        var connected = await Record.ExceptionAsync(async () => (await BridgeLoopback.ConnectAsync(registry, options)).Dispose());

        // Assert
        Assert.Equal(withHeader, connected is null);
    }

    [Fact]
    public async Task ShouldWarnThatTheTokenTravelsInClear_WhenTheAddressNamesDockerDesktopsHostOverHttp()
    {
        // Arrange
        var logger = new RecordingLogger();
        var registry = new ToolRegistry([new FakeBridgeTool("firefox")]);
        using var refusing = LoopbackPorts.Refusing();
        var options = BridgeLoopback.ClientOptions(new Uri($"http://host.docker.internal:{refusing.Port}/"), "firefox");
        options.ConnectTimeout = TimeSpan.FromSeconds(2);

        // Act
        await Record.ExceptionAsync(() => BridgeLoopback.ConnectAsync(registry, options, operatorLogger: logger));

        // Assert
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("in clear", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldNameTheLocalSection_WhenTheSettingsConfigureABridgedTool()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:Tools:Firefox:ExecutablePath"] = "/usr/bin/firefox" })
            .Build();

        // Act
        var section = ToolBridgeLink.LocalSettingsOf(configuration, "firefox");

        // Assert
        Assert.Equal("Orkeon:Tools:Firefox", section);
    }
}
