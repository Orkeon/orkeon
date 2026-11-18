using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// The three listings are read entry by entry, as <c>tools/list</c> is since #417: a prompt or a resource that
/// cannot be read is set aside alone and a name listed twice is one tool, each warned about once. A prompt or
/// resource listing of the wrong shape lists nothing; a tool listing of the wrong shape fails, naming the server.
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
            {"prompts":[{"name":"good","arguments":[{"name":"topic","required":true},null]},{"name":"bad","description":5},{"name":""},null,{"name":"bare","arguments":null}]}
            """);
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 2; listing++)
        {
            var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["good", "bare"], prompts.Select(p => p.Name));
            Assert.Equal(["topic"], prompts[0].Arguments.Select(a => a.Name)); // a null argument is none
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
            {"resources":[{"uri":"file:///a","name":"a"},{"uri":7,"name":"numbered"},{"name":"nowhere"},"text",{"uri":"  ","name":"blank"},{"uri":"file:///b","name":"b"}]}
            """);
        await using var _ = client;
        await using var __ = transport;

        var resources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], resources.Select(r => r.Name));
        var warnings = Warnings(log);
        Assert.Equal(4, warnings.Count);
        Assert.Equal(2, warnings.Count(w => w.Contains("no URI", StringComparison.Ordinal)));
        // A resource that names no URI is named by its name.
        Assert.Single(warnings, w => w.Contains("resource 'nowhere'", StringComparison.Ordinal));
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
    [InlineData("prompts/list", """{"prompts":5}""", "'prompts' is no array (a number)")]
    [InlineData("prompts/list", "[]", "its result is no object (an array)")]
    [InlineData("resources/list", """{"resources":true}""", "'resources' is no array (a boolean)")]
    [InlineData("resources/list", """{"resources":[],"nextCursor":2}""", "'nextCursor' is no string (a number)")]
    public async Task ShouldListNothingAndWarnOnce_WhenAPromptOrResourceListingHasTheWrongShape(string method, string page, string reason)
    {
        var (client, transport, log) = await ConnectAsync(method, page);
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 2; listing++)
        {
            var count = method == "prompts/list"
                ? (await client.ListPromptsAsync(TestContext.Current.CancellationToken)).Count
                : (await client.ListResourcesAsync(TestContext.Current.CancellationToken)).Count;
            Assert.Equal(0, count);
        }

        var warning = Assert.Single(Warnings(log));
        Assert.Contains(method, warning, StringComparison.Ordinal);
        Assert.Contains("'configured-id'", warning, StringComparison.Ordinal);
        Assert.Contains(reason, warning, StringComparison.Ordinal);
    }

    /// <summary>
    /// The registry follows <c>tools/list</c>: a listing that cannot be told from an empty one fails, naming the
    /// server and the member, where listing nothing would take the server's tools away.
    /// </summary>
    [Theory]
    [InlineData("""{"tools":"none"}""", "'tools' is no array (a string)")]
    [InlineData("""{"tools":{"a":{"name":"a"}}}""", "'tools' is no array (an object)")]
    [InlineData("[]", "its result is no object (an array)")]
    [InlineData("""{"tools":[{"name":"a"}],"nextCursor":2}""", "'nextCursor' is no string (a number)")]
    public async Task ShouldFailNamingTheServer_WhenTheToolListingHasTheWrongShape(string page, string reason)
    {
        var (client, transport, _) = await ConnectAsync("tools/list", page);
        await using var __ = client;
        await using var ___ = transport;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ListToolsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("tools/list answer of MCP server 'configured-id'", ex.Message, StringComparison.Ordinal);
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldKeepTheFirst_WhenTheServerListsANameTwice_EvenAcrossPages()
    {
        var (client, transport, log) = await ConnectAsync(
            "tools/list",
            """{"tools":[{"name":"dup","description":"first"},{"name":"other","description":null},{"name":"dup","description":"second"}],"nextCursor":"p2"}""",
            """{"tools":[{"name":"DUP","description":"third"}]}""");
        await using var _ = client;
        await using var __ = transport;

        for (var listing = 0; listing < 2; listing++)
        {
            var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["dup", "other"], tools.Select(t => t.Name));
            Assert.Equal("first", tools[0].Description);
            Assert.Equal("", tools[1].Description); // "description": null is no description
        }

        // Once per name as it is written, not once per copy or per listing: the registry holds `dup` and `DUP` as one name.
        var warnings = Warnings(log);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("lists the tool 'dup' twice", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("lists the tool 'DUP' twice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldKeepTheFirstPromptAndResource_WhenTheServerListsOneTwice()
    {
        var (prompts, promptTransport, promptLog) = await ConnectAsync(
            "prompts/list",
            """{"prompts":[{"name":"p","description":"first"},{"name":"P"},{"name":"p","description":"second"}],"nextCursor":"2"}""",
            """{"prompts":[{"name":"p","description":"third"}]}""");
        await using var _ = prompts;
        await using var __ = promptTransport;
        var (resources, resourceTransport, resourceLog) = await ConnectAsync(
            "resources/list",
            """{"resources":[{"uri":"file:///a","name":"first"},{"uri":"file:///a","name":"second"},{"uri":"file:///b","name":"b"}]}""");
        await using var ___ = resources;
        await using var ____ = resourceTransport;

        for (var listing = 0; listing < 2; listing++)
        {
            var listed = await prompts.ListPromptsAsync(TestContext.Current.CancellationToken);
            // prompts/get names a prompt with its case: `P` is another prompt.
            Assert.Equal(["p", "P"], listed.Select(p => p.Name));
            Assert.Equal("first", listed[0].Description);
            var read = await resources.ListResourcesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["first", "b"], read.Select(r => r.Name));
        }

        var promptWarning = Assert.Single(promptLog.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("mcp.entry.duplicate", promptWarning.EventId.Name);
        Assert.Contains("lists the prompt 'p' twice", promptWarning.Message, StringComparison.Ordinal);
        var resourceWarning = Assert.Single(resourceLog.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("mcp.entry.duplicate", resourceWarning.EventId.Name);
        Assert.Contains("lists the resource 'file:///a' twice", resourceWarning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameEachWarningForALogFilter()
    {
        var (prompts, promptTransport, promptLog) = await ConnectAsync("prompts/list", """{"prompts":[{"name":5}]}""");
        await using var _ = prompts;
        await using var __ = promptTransport;
        var (resources, resourceTransport, resourceLog) = await ConnectAsync("resources/list", """{"resources":"none"}""");
        await using var ___ = resources;
        await using var ____ = resourceTransport;
        var (tools, toolTransport, toolLog) = await ConnectAsync("tools/list", """{"tools":[null,{"name":"ok"}]}""");
        await using var _____ = tools;
        await using var ______ = toolTransport;

        await prompts.ListPromptsAsync(TestContext.Current.CancellationToken);
        await resources.ListResourcesAsync(TestContext.Current.CancellationToken);
        await tools.ListToolsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("mcp.entry.unreadable", Assert.Single(promptLog.Entries, e => e.Level == LogLevel.Warning).EventId.Name);
        Assert.Equal("mcp.listing.unreadable", Assert.Single(resourceLog.Entries, e => e.Level == LogLevel.Warning).EventId.Name);
        Assert.Equal("mcp.tool.unreadable", Assert.Single(toolLog.Entries, e => e.Level == LogLevel.Warning).EventId.Name);
    }
}
