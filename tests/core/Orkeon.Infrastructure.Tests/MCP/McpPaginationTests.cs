using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-10: tools/list, resources/list and prompts/list follow nextCursor, 100 pages at most.</summary>
public sealed class McpPaginationTests
{
    private static string? CursorOf(JsonRpcRequest request) =>
        request.Params is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("cursor", out var c) ? c.GetString() : null;

    private static JsonRpcResponse Ok(JsonRpcRequest request, object result) =>
        new() { Id = request.Id, Result = JsonSerializer.SerializeToElement(result, result.GetType()) };

    /// <summary>A legacy server whose <paramref name="list"/> answers the paged listings it cares about.</summary>
    [SuppressMessage("Reliability", "CA2000", Justification = "The client owns the transport and the test disposes the client.")]
    private static async Task<(McpClient Client, MockMcpTransport Transport)> ConnectAsync(
        Func<JsonRpcRequest, JsonRpcResponse?> list)
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(request => list(request) ?? ToolServer.Respond(request, []));
        var client = new McpClient(transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        return (client, transport);
    }

    [Fact]
    public async Task ShouldConcatenateThreePages_OfToolsList()
    {
        var (client, transport) = await ConnectAsync(request =>
        {
            if (request.Method != "tools/list")
                return null;
            return CursorOf(request) switch
            {
                null => Ok(request, new { tools = new[] { new { name = "a" } }, nextCursor = "p2" }),
                "p2" => Ok(request, new { tools = new[] { new { name = "b" } }, nextCursor = "p3" }),
                _ => Ok(request, new { tools = new[] { new { name = "c" } } }),
            };
        });
        await using var _ = client;

        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "c"], tools.Select(t => t.Name));
        Assert.Equal(3, transport.AllSentRequests.Count(r => r.Method == "tools/list"));
    }

    [Fact]
    public async Task ShouldConcatenatePages_OfResourcesAndPrompts()
    {
        var (client, _) = await ConnectAsync(request => request.Method switch
        {
            "resources/list" => CursorOf(request) is null
                ? Ok(request, new { resources = new[] { new { uri = "file:///a", name = "a" } }, nextCursor = "r2" })
                : Ok(request, new { resources = new[] { new { uri = "file:///b", name = "b" } } }),
            "prompts/list" => CursorOf(request) is null
                ? Ok(request, new { prompts = new[] { new { name = "p1" } }, nextCursor = "q2" })
                : Ok(request, new { prompts = new[] { new { name = "p2" } } }),
            _ => null,
        });
        await using var _ = client;

        var resources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);
        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], resources.Select(r => r.Name));
        Assert.Equal(["p1", "p2"], prompts.Select(p => p.Name));
    }

    [Fact]
    public async Task ShouldFail_WhenThePagesHoldMoreItemsThanTheBound()
    {
        var page = Enumerable.Range(0, 600).Select(i => new { name = $"t{i}" }).ToArray();
        var (client, transport) = await ConnectAsync(request =>
            request.Method == "tools/list" ? Ok(request, new { tools = page, nextCursor = "more" }) : null);
        await using var _ = client;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ListToolsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("items", ex.Message, StringComparison.Ordinal);
        // The item bound is reached long before the page bound.
        Assert.InRange(transport.AllSentRequests.Count(r => r.Method == "tools/list"), 2, 99);
    }

    [Fact]
    public async Task ShouldFail_WhenTheServerNeverStopsPaging()
    {
        var (client, transport) = await ConnectAsync(request =>
            request.Method == "tools/list"
                ? Ok(request, new { tools = new[] { new { name = "t" } }, nextCursor = "again" })
                : null);
        await using var _ = client;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ListToolsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("100", ex.Message, StringComparison.Ordinal);
        Assert.Equal(100, transport.AllSentRequests.Count(r => r.Method == "tools/list"));
    }
}
