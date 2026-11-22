using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentCli.Bridge;
using Orkeon.Interop.AgentCli.Tests.Doubles;

namespace Orkeon.Interop.AgentCli.Tests.Bridge;

public sealed class AgentCliBridgeTests
{
    private static readonly ToolSchema Read = new("read", "Reads a file", new Dictionary<string, ParameterSchema>
    {
        ["path"] = new("string", "The file", Required: true),
    });

    private static readonly ToolSchema Write = new("write", "Writes a file", []);

    [Fact]
    public async Task ShouldAnswerInitializeAndList_WhenCliStarts()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);
        await bridge.SetToolsAsync([Read], TestContext.Current.CancellationToken);

        await connection.SendAsync(Request(0, "server/discover"));
        await connection.SendAsync(Request(1, "initialize", new JsonObject { ["protocolVersion"] = "2025-11-25" }));
        await connection.SendAsync(Request(2, "tools/list"));

        var discover = await connection.NextAsync();
        var initialize = await connection.NextAsync();
        var list = await connection.NextAsync();

        Assert.Equal(-32601, discover["error"]!["code"]!.GetValue<int>());
        Assert.Equal("2025-11-25", initialize["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.True(initialize["result"]!["capabilities"]!["tools"]!["listChanged"]!.GetValue<bool>());
        var tool = Assert.Single(list["result"]!["tools"]!.AsArray())!;
        Assert.Equal("read", tool["name"]!.GetValue<string>());
        Assert.Equal("string", tool["inputSchema"]!["properties"]!["path"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ShouldAnswerCall_WhenResultComesAfterIt()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);

        await connection.CallAsync(7, "read", "toolu_1");
        await connection.SendAsync(Request(8, "ping"));
        await connection.NextAsync(); // the ping's answer: the call was handled before it
        Assert.Contains("toolu_1", bridge.WaitingCalls);

        await bridge.ResolveAsync("toolu_1", "contents", isError: false, TestContext.Current.CancellationToken);
        var answer = await connection.NextAsync();

        Assert.Equal(7, answer["id"]!.GetValue<int>());
        Assert.Equal("contents", answer["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.False(answer["result"]!["isError"]!.GetValue<bool>());
        Assert.Empty(bridge.WaitingCalls);
    }

    [Fact]
    public async Task ShouldAnswerCall_WhenResultCameBeforeIt()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);

        await bridge.ResolveAsync("toolu_1", "DENIED: not allowed", isError: true, TestContext.Current.CancellationToken);
        await connection.CallAsync(3, "write", "toolu_1");
        var answer = await connection.NextAsync();

        Assert.Equal(3, answer["id"]!.GetValue<int>());
        Assert.True(answer["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ShouldMatchByName_WhenCallHasNoToolUseId()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);
        bridge.Expect([("toolu_a", "read"), ("toolu_b", "write")]);

        await connection.CallAsync(1, "write", toolUseId: null);
        await connection.SendAsync(Request(2, "ping"));
        await connection.NextAsync();
        await bridge.ResolveAsync("toolu_b", "written", isError: false, TestContext.Current.CancellationToken);
        var answer = await connection.NextAsync();

        Assert.Equal(1, answer["id"]!.GetValue<int>());
        Assert.Equal("written", answer["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ShouldAnswerCall_WhenItCameBeforeItsToolUseBlockWasExpected()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);

        // The call reaches the bridge before the session has read the message that asks for it.
        await connection.CallAsync(1, "write", toolUseId: null);
        await connection.SendAsync(Request(2, "ping"));
        await connection.NextAsync();
        bridge.Expect([("toolu_b", "write")]);
        await bridge.ResolveAsync("toolu_b", "written", isError: false, TestContext.Current.CancellationToken);
        var answer = await connection.NextAsync();

        Assert.Equal(1, answer["id"]!.GetValue<int>());
        Assert.Equal("written", answer["result"]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ShouldKeepServing_WhenMetaIsNoObject()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);
        bridge.Expect([("toolu_a", "read")]);

        await connection.SendAsync(Request(1, "tools/call", new JsonObject { ["name"] = "read", ["_meta"] = "none" }));
        await connection.SendAsync(Request(2, "ping"));
        var pong = await connection.NextAsync();
        await bridge.ResolveAsync("toolu_a", "content", isError: false, TestContext.Current.CancellationToken);
        var answer = await connection.NextAsync();

        Assert.Equal(2, pong["id"]!.GetValue<int>());
        Assert.Equal(1, answer["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task ShouldCutResult_WhenLongerThanTheLimit()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 1_000, NullLogger.Instance);

        await bridge.ResolveAsync("toolu_1", new string('x', 5_000), isError: false, TestContext.Current.CancellationToken);
        await connection.CallAsync(1, "read", "toolu_1");
        var text = (await connection.NextAsync())["result"]!["content"]![0]!["text"]!.GetValue<string>();

        Assert.StartsWith(new string('x', 1_000), text, StringComparison.Ordinal);
        Assert.Contains("1000 of its 5000 characters", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNotifyListChanged_WhenToolsChangeAfterListing()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);
        await bridge.SetToolsAsync([Read], TestContext.Current.CancellationToken);
        await connection.SendAsync(Request(1, "tools/list"));
        await connection.NextAsync();

        await bridge.SetToolsAsync([Read], TestContext.Current.CancellationToken);
        await bridge.SetToolsAsync([Read, Write], TestContext.Current.CancellationToken);
        var notification = await connection.NextAsync();

        Assert.Equal("notifications/tools/list_changed", notification["method"]!.GetValue<string>());
        Assert.False(connection.Written.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ShouldDropCall_WhenCliCancelsIt()
    {
        var connection = new FakeBridgeConnection();
        await using var bridge = new AgentCliBridge(connection, 100_000, NullLogger.Instance);

        await connection.CallAsync(5, "read", "toolu_1");
        await connection.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/cancelled",
            ["params"] = new JsonObject { ["requestId"] = 5 },
        });
        await connection.SendAsync(Request(6, "ping"));
        await connection.NextAsync();

        Assert.Empty(bridge.WaitingCalls);
    }

    private static JsonObject Request(int id, string method, JsonObject? parameters = null) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters ?? [],
    };
}
