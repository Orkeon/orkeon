using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// The three listings are read entry by entry, as <c>tools/list</c> is since #417: a prompt or a resource that
/// cannot be read is set aside alone, a list member of the wrong JSON type lists nothing, a name listed twice
/// is one tool. Each is warned about once, and none fails the listing.
/// </summary>
public sealed class McpClientListingToleranceTests
{
    private static async Task<(McpClient Client, MockMcpTransport Transport, RecordingLogger Log)> ConnectAsync(
        string method, params string[] pages)
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        var served = 0;
        transport.SetSendRequestFunc(req =>
        {
            if (req.Method == "initialize")
            {
                return new JsonRpcResponse
                {
                    Id = req.Id,
                    Result = JsonSerializer.SerializeToElement(new McpInitializeResult
                    {
                        ProtocolVersion = "2025-06-18",
                        Capabilities = new McpServerCapabilities { Tools = new McpCapabilityInfo() },
                        ServerInfo = new McpServerInfo { Name = "self-reported", Version = "0" }
                    })
                };
            }

            if (req.Method != method)
                return new JsonRpcResponse { Id = req.Id, Error = new JsonRpcError(JsonRpcErrorCodes.MethodNotFound, "nope") };

            // A listing starts again at the first page when no cursor is sent.
            var hasCursor = req.Params is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("cursor", out _);
            served = hasCursor ? served + 1 : 0;
            return new JsonRpcResponse { Id = req.Id, Result = JsonElement.Parse(pages[Math.Min(served, pages.Length - 1)]) };
        });
        // The factory holds nothing to release: it hands out its one recording logger.
        using var factory = new RecordingLoggerFactory();
        var log = factory.Logger;
        var client = new McpClient(transport, new Logger<McpClient>(factory)) { ServerLabel = "configured-id" };
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        return (client, transport, log);
    }

    private static List<string> Warnings(RecordingLogger log) =>
        log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();

    [Fact]
    public async Task ShouldKeepTheOtherPrompts_WhenOneCannotBeRead()
    {
        var (client, transport, log) = await ConnectAsync("prompts/list", """
            {"prompts":[{"name":"good","arguments":[{"name":"topic","required":true}]},{"name":"bad","description":5},{"name":""},null,{"name":"bare","arguments":null}]}
            """);
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 2; listing++)
        {
            var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["good", "bare"], prompts.Select(p => p.Name));
            Assert.Empty(prompts[1].Arguments); // "arguments": null is no argument, never a null list
        }

        var warnings = Warnings(log);
        Assert.Equal(3, warnings.Count); // bad, the nameless one, the null entry: once each over two listings
        Assert.Single(warnings, w => w.Contains("prompt 'bad'", StringComparison.Ordinal));
        Assert.All(warnings, w => Assert.Contains("'configured-id'", w, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldKeepTheOtherResources_WhenOneCannotBeRead()
    {
        var (client, transport, log) = await ConnectAsync("resources/list", """
            {"resources":[{"uri":"file:///a","name":"a"},{"uri":7,"name":"numbered"},{"name":"nowhere"},"text",{"uri":"file:///b","name":"b"}]}
            """);
        await using var _ = client;
        await using var __ = transport;

        var resources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], resources.Select(r => r.Name));
        var warnings = Warnings(log);
        Assert.Equal(3, warnings.Count);
        Assert.Single(warnings, w => w.Contains("no URI", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("prompts/list", """{"prompts":null}""")]
    [InlineData("prompts/list", "{}")]
    [InlineData("resources/list", """{"resources":null}""")]
    [InlineData("resources/list", "{}")]
    public async Task ShouldListNothing_WhenTheMemberIsNullOrAbsent(string method, string page)
    {
        var (client, transport, log) = await ConnectAsync(method, page);
        await using var _ = client;
        await using var __ = transport;

        var count = method == "prompts/list"
            ? (await client.ListPromptsAsync(TestContext.Current.CancellationToken)).Count
            : (await client.ListResourcesAsync(TestContext.Current.CancellationToken)).Count;

        Assert.Equal(0, count);
        Assert.Empty(Warnings(log));
    }

    [Theory]
    [InlineData("tools/list", """{"tools":"none"}""", "'tools' is no array (a string)")]
    [InlineData("tools/list", """{"tools":{"a":{"name":"a"}}}""", "'tools' is no array (an object)")]
    [InlineData("tools/list", "[]", "its result is no object (an array)")]
    [InlineData("prompts/list", """{"prompts":5}""", "'prompts' is no array (a number)")]
    [InlineData("resources/list", """{"resources":true}""", "'resources' is no array (a boolean)")]
    public async Task ShouldListNothingAndWarnOnce_WhenTheListingHasTheWrongShape(string method, string page, string reason)
    {
        var (client, transport, log) = await ConnectAsync(method, page);
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 2; listing++)
        {
            var count = method switch
            {
                "tools/list" => (await client.ListToolsAsync(TestContext.Current.CancellationToken)).Count,
                "prompts/list" => (await client.ListPromptsAsync(TestContext.Current.CancellationToken)).Count,
                _ => (await client.ListResourcesAsync(TestContext.Current.CancellationToken)).Count,
            };
            Assert.Equal(0, count);
        }

        var warning = Assert.Single(Warnings(log));
        Assert.Contains(method, warning, StringComparison.Ordinal);
        Assert.Contains("'configured-id'", warning, StringComparison.Ordinal);
        Assert.Contains(reason, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepThePageAndStop_WhenNextCursorIsNoString()
    {
        var (client, transport, log) = await ConnectAsync("tools/list", """{"tools":[{"name":"a"}],"nextCursor":2}""");
        await using var _ = client;
        await using var __ = transport;

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a"], tools.Select(t => t.Name));
        Assert.Equal(1, transport.AllSentRequests.Count(r => r.Method == "tools/list"));
        Assert.Contains("'nextCursor' is no string", Assert.Single(Warnings(log)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepTheFirst_WhenTheServerListsANameTwice_EvenAcrossPages()
    {
        var (client, transport, log) = await ConnectAsync(
            "tools/list",
            """{"tools":[{"name":"dup","description":"first"},{"name":"other"},{"name":"dup","description":"second"}],"nextCursor":"p2"}""",
            """{"tools":[{"name":"dup","description":"third"}]}""");
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 2; listing++)
        {
            var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["dup", "other"], tools.Select(t => t.Name));
            Assert.Equal("first", tools[0].Description);
        }

        var warning = Assert.Single(Warnings(log)); // once for the name, not once per copy or per listing
        Assert.Contains("lists the tool 'dup' twice", warning, StringComparison.Ordinal);
    }
}
