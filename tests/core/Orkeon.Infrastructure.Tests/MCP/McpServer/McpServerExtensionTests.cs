using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-24 - the tools and prompts a host adds to the server: listed with the registry's, routed to the
/// extension, progress sent as notifications only when the client gave a token. Every wait is a hang guard.
/// </summary>
public sealed class McpServerExtensionTests
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);

    private static McpServer NewServer(FakeMcpServerExtension? extension, params MockGateTool[] tools)
    {
        var registry = new MockToolRegistry();
        foreach (var tool in tools)
            registry.AddTool(tool.Name, tool);
        return new McpServer(registry, ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), new McpServerOptions(), null, extension);
    }

    private static JsonRpcRequest Request(object id, string method, object? @params = null) =>
        JsonSerializer.Deserialize<JsonRpcRequest>(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params }))!;

    private static JsonElement Result(JsonRpcResponse? response)
    {
        Assert.NotNull(response);
        Assert.Null(response.Error);
        return response.Result!.Value;
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldListTheExtensionToolsWithTheRegistryOnes_InOneOrder()
    {
        var server = NewServer(new FakeMcpServerExtension(), new MockGateTool("zeta"), new MockGateTool("alpha"));

        var result = Result(await server.ProcessRequestAsync(Request(1, "tools/list"), TestContext.Current.CancellationToken));

        var names = result.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["alpha", "run_crew_demo", "zeta"], names);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldRouteTheCallToTheExtension_WhenItOwnsTheTool()
    {
        var extension = new FakeMcpServerExtension();
        var server = NewServer(extension);

        var result = Result(await server.ProcessRequestAsync(
            Request(1, "tools/call", new { name = "run_crew_demo", arguments = new { topic = "bees" } }), TestContext.Current.CancellationToken));

        Assert.Equal("final output", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("bees", extension.LastArguments!.Value.GetProperty("topic").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldAdvertiseThePromptsCapabilityAndListThem_WhenTheExtensionHasPrompts()
    {
        var server = NewServer(new FakeMcpServerExtension());

        var init = Result(await server.ProcessRequestAsync(Request(1, "initialize", new { protocolVersion = "2025-11-25" }), TestContext.Current.CancellationToken));
        var list = Result(await server.ProcessRequestAsync(Request(2, "prompts/list"), TestContext.Current.CancellationToken));
        var got = Result(await server.ProcessRequestAsync(Request(3, "prompts/get", new { name = "greet" }), TestContext.Current.CancellationToken));

        Assert.True(init.GetProperty("capabilities").TryGetProperty("prompts", out _));
        Assert.Equal("greet", list.GetProperty("prompts")[0].GetProperty("name").GetString());
        Assert.Equal("hello", got.GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldAnswerMethodNotFound_WhenThereAreNoPrompts()
    {
        var server = NewServer(null);

        var init = Result(await server.ProcessRequestAsync(Request(1, "initialize", new { protocolVersion = "2025-11-25" }), TestContext.Current.CancellationToken));
        var list = await server.ProcessRequestAsync(Request(2, "prompts/list"), TestContext.Current.CancellationToken);

        Assert.False(init.GetProperty("capabilities").TryGetProperty("prompts", out _));
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, list!.Error!.Code);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldAnswerInvalidParams_WhenTheExtensionRefusesTheRequest()
    {
        var server = NewServer(new FakeMcpServerExtension());

        var response = await server.ProcessRequestAsync(Request(1, "prompts/get", new { name = "nope" }), TestContext.Current.CancellationToken);

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, response!.Error!.Code);
        Assert.Contains("nope", response.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldSendProgressNotificationsBeforeTheAnswer_WhenTheClientGaveAToken()
    {
        var extension = new FakeMcpServerExtension { ProgressSteps = 3 };
        var server = NewServer(extension);
        var reader = new FakeLineReader();
        var writer = new FakeLineWriter();
        var run = server.RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);
        try
        {

            reader.Send(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 7, method = "tools/call",
                @params = new { name = "run_crew_demo", arguments = new { }, _meta = new { progressToken = "tok-1" } },
            }));
            await Polling.WaitUntilAsync(() => writer.Lines.Count >= 4, HangGuard);
            reader.Complete();
            await run.WaitAsync(HangGuard, TestContext.Current.CancellationToken);

            var lines = writer.Lines.Select(l => JsonDocument.Parse(l).RootElement).ToList();
            Assert.Equal(4, lines.Count);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal("notifications/progress", lines[i].GetProperty("method").GetString());
                var p = lines[i].GetProperty("params");
                Assert.Equal("tok-1", p.GetProperty("progressToken").GetString());
                Assert.Equal(i + 1, p.GetProperty("progress").GetDouble());
                Assert.Equal(3, p.GetProperty("total").GetDouble());
                Assert.Equal($"step {i + 1}", p.GetProperty("message").GetString());
            }

            Assert.Equal(7, lines[3].GetProperty("id").GetInt32());
        }
        finally
        {
            reader.Complete();
            await run.WaitAsync(HangGuard, TestContext.Current.CancellationToken);
            reader.Dispose();
            await writer.DisposeAsync();
        }
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldSendNoProgress_WhenTheClientGaveNoToken()
    {
        var server = NewServer(new FakeMcpServerExtension { ProgressSteps = 2 });
        var reader = new FakeLineReader();
        var writer = new FakeLineWriter();
        var run = server.RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);
        try
        {

            reader.Send(McpServerStdioHarness.Call(7, "run_crew_demo"));
            await Polling.WaitUntilAsync(() => writer.Lines.Count >= 1, HangGuard);
            reader.Complete();
            await run.WaitAsync(HangGuard, TestContext.Current.CancellationToken);

            var line = Assert.Single(writer.Lines);
            Assert.Equal(7, JsonDocument.Parse(line).RootElement.GetProperty("id").GetInt32());
        }
        finally
        {
            reader.Complete();
            await run.WaitAsync(HangGuard, TestContext.Current.CancellationToken);
            reader.Dispose();
            await writer.DisposeAsync();
        }
    }

    [Fact]
    [Trait("BR", "FWK-24")]
    public async Task ShouldCancelTheExtensionCall_WhenTheClientCancels()
    {
        var extension = new FakeMcpServerExtension { WaitForFinish = true };
        var server = NewServer(extension);
        var reader = new FakeLineReader();
        var writer = new FakeLineWriter();
        var run = server.RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);
        try
        {

            reader.Send(McpServerStdioHarness.Call(9, "run_crew_demo"));
            // The call is in flight once it has recorded its arguments.
            await Polling.WaitUntilAsync(() => extension.LastArguments is not null, HangGuard);
            reader.Send(McpServerStdioHarness.Cancel(9));
            await Polling.WaitUntilAsync(() => extension.SawCancellation, HangGuard);
            reader.Complete();
            await run.WaitAsync(HangGuard, TestContext.Current.CancellationToken);

            Assert.Empty(writer.Lines);
        }
        finally
        {
            reader.Complete();
            await run.WaitAsync(HangGuard, TestContext.Current.CancellationToken);
            reader.Dispose();
            await writer.DisposeAsync();
        }
    }
}
