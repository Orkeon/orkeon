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

        var all = await store.ListAsync(null, false, CancellationToken.None);
        Assert.Equal(2, all.Count);

        var projectOnly = await store.ListAsync("project", false, CancellationToken.None);
        Assert.Single(projectOnly);
        Assert.Equal("uses pnpm", projectOnly[0].Content);

        var got = await store.GetAsync(id1, CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal("project", got!.Category);

        Assert.True(await store.DeleteAsync(id1, CancellationToken.None));
        Assert.False(await store.DeleteAsync(id1, CancellationToken.None));
        Assert.Single(await store.ListAsync(null, false, CancellationToken.None));
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
    public async Task CategoryStore_lists_newest_first_by_created_at_then_id()
    {
        var store = new InMemoryCategoryMemoryStore();
        var id1 = await store.AddAsync("project", "first", CancellationToken.None);
        var id2 = await store.AddAsync("project", "second", CancellationToken.None);
        var id3 = await store.AddAsync("user", "third", CancellationToken.None);

        var insertion = await store.ListAsync(null, false, CancellationToken.None);
        Assert.Equal([id1, id2, id3], insertion.Select(e => e.Id));

        // Replacing the first entry makes it the newest: it leads, the others keep their relative order.
        Assert.True(await store.ReplaceAsync(id1, "first, revised", CancellationToken.None));
        var newest = await store.ListAsync(null, true, CancellationToken.None);
        Assert.Equal(id1, newest[0].Id);
        Assert.Equal([id3, id2], newest.Skip(1).Select(e => e.Id));
    }

    [Fact]
    public async Task CategoryStore_replace_keeps_id_and_category_and_refreshes_created_at()
    {
        var store = new InMemoryCategoryMemoryStore();
        var id = await store.AddAsync("feedback", "run the linter", CancellationToken.None);
        var before = (await store.GetAsync(id, CancellationToken.None))!;

        Assert.True(await store.ReplaceAsync(id, "  run the linter and the tests  ", CancellationToken.None));
        var after = (await store.GetAsync(id, CancellationToken.None))!;
        Assert.Equal(id, after.Id);
        Assert.Equal("feedback", after.Category);
        Assert.Equal("run the linter and the tests", after.Content);
        Assert.True(string.CompareOrdinal(after.CreatedAt, before.CreatedAt) >= 0);

        Assert.False(await store.ReplaceAsync(id + 1, "nobody", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReplaceAsync(id, "  ", CancellationToken.None));
        Assert.Single(await store.ListAsync(null, false, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryStoreTool_replace_and_newest_first()
    {
        var store = new InMemoryCategoryMemoryStore();
        using var tool = new MemoryStoreTool(store);
        var id1 = await store.AddAsync("project", "uses npm", CancellationToken.None);
        var id2 = await store.AddAsync("project", "targets net10", CancellationToken.None);

        var replace = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "replace",
            ["id"] = id1,
            ["content"] = "uses pnpm",
        }), TestContext.Current.CancellationToken);
        Assert.Equal(id1, Convert.ToInt32(ResultDict(replace)["id"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("uses pnpm", (await store.GetAsync(id1, CancellationToken.None))!.Content);

        var list = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "list",
            ["newest_first"] = true,
        }), TestContext.Current.CancellationToken);
        var entries = Assert.IsType<System.Collections.IEnumerable>(ResultDict(list)["entries"], exactMatch: false).Cast<object>().ToList();
        Assert.Equal(2, entries.Count);
        var first = Assert.IsType<Dictionary<string, object?>>(entries[0]);
        Assert.Equal(id1, Convert.ToInt32(first["id"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(first["created_at"]);
        Assert.Equal(id2, Convert.ToInt32(Assert.IsType<Dictionary<string, object?>>(entries[1])["id"], System.Globalization.CultureInfo.InvariantCulture));

        var missing = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "replace",
            ["id"] = 99,
            ["content"] = "nobody",
        }), TestContext.Current.CancellationToken);
        Assert.False(missing.Success);

        var blank = await tool.CallAsync(new ToolCallRequest("memory_store", new Dictionary<string, object?>
        {
            ["operation"] = "replace",
            ["id"] = id1,
        }), TestContext.Current.CancellationToken);
        Assert.False(blank.Success);
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
