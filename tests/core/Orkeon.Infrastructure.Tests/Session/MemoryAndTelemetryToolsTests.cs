using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Session;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Tests.Session;

/// <summary>
/// The typed memory store and the cost/stats telemetry tools.
/// </summary>
public sealed class MemoryAndTelemetryToolsTests
{
    private static Dictionary<string, object?> ResultDict(ToolCallResponse resp)
    {
        Assert.True(resp.Success, resp.Error);
        return Assert.IsType<Dictionary<string, object?>>(resp.Result);
    }

    // ── Category memory store ───────────────────────────────────────────────

    [Fact]
    public async Task CategoryStore_add_list_get_delete()
    {
        var store = new InMemoryCategoryMemoryStore();
        var id1 = await store.AddAsync("project", "uses pnpm", CancellationToken.None);
        var id2 = await store.AddAsync("user", "prefers TS strict", CancellationToken.None);
        Assert.NotEqual(id1, id2);

        var all = await store.ListAsync(null, CancellationToken.None);
        Assert.Equal(2, all.Count);

        var projectOnly = await store.ListAsync("project", CancellationToken.None);
        Assert.Single(projectOnly);
        Assert.Equal("uses pnpm", projectOnly[0].Content);

        var got = await store.GetAsync(id1, CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal("project", got!.Category);

        Assert.True(await store.DeleteAsync(id1, CancellationToken.None));
        Assert.False(await store.DeleteAsync(id1, CancellationToken.None));
        Assert.Single(await store.ListAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryStoreTool_add_then_list()
    {
        var store = new InMemoryCategoryMemoryStore();
        using var tool = new MemoryStoreTool(store);

        var add = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "add",
            ["category"] = "feedback",
            ["content"] = "always run the linter",
        }), TestContext.Current.CancellationToken);
        var addDict = ResultDict(add);
        Assert.NotNull(addDict["id"]);

        var list = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "list",
            ["category"] = "feedback",
        }), TestContext.Current.CancellationToken);
        var listDict = ResultDict(list);
        var entries = Assert.IsType<System.Collections.IEnumerable>(listDict["entries"], exactMatch: false);
        Assert.Single(entries.Cast<object>());
    }

    [Fact]
    public async Task MemoryStoreTool_add_without_content_fails()
    {
        using var tool = new MemoryStoreTool(new InMemoryCategoryMemoryStore());

        var resp = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "add",
            ["category"] = "user",
        }), TestContext.Current.CancellationToken);

        Assert.False(resp.Success);
    }

    // ── Telemetry tools (null cost manager → zeros; stats reads buffer) ──────

    [Fact]
    public async Task SessionCost_without_manager_returns_zeros()
    {
        using var tool = new SessionCostTool(costManager: null);

        var dict = ResultDict(await tool.CallAsync(
            new ToolCallRequest("session_cost", new Dictionary<string, object?>()), TestContext.Current.CancellationToken));

        Assert.Equal(0m, Convert.ToDecimal(dict["total_cost_usd"]));
        Assert.Equal(0, Convert.ToInt32(dict["total_tokens"]));
    }

    [Fact]
    public async Task SessionStats_reports_buffer_size()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(new[]
        {
            new Orkeon.Application.Interfaces.Ports.SessionMessage { Role = "user", Content = "abcd" },
        });
        using var tool = new SessionStatsTool(buffer, costManager: null);

        var dict = ResultDict(await tool.CallAsync(
            new ToolCallRequest("session_stats", new Dictionary<string, object?>()), TestContext.Current.CancellationToken));

        Assert.Equal(1, Convert.ToInt32(dict["message_count"]));
        Assert.True(Convert.ToInt32(dict["estimated_tokens"]) >= 1);
    }
}
