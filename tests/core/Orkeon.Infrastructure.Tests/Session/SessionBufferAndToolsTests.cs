using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Session;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Tests.Session;

/// <summary>
/// exp 07 Phase 2: the session buffer primitive and the three session tools
/// (<c>session_store</c>, <c>session_snip</c>, <c>token_budget</c>).
/// </summary>
public sealed class SessionBufferAndToolsTests
{
    private static SessionMessage Msg(string role, string content) => new() { Role = role, Content = content };

    private static Dictionary<string, object?> ResultDict(ToolCallResponse resp)
    {
        Assert.True(resp.Success, resp.Error);
        return Assert.IsType<Dictionary<string, object?>>(resp.Result);
    }

    // ── Buffer service ──────────────────────────────────────────────────────

    [Fact]
    public void Replace_then_get_round_trips()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(new[] { Msg("user", "hi"), Msg("assistant", "hello") });

        var messages = buffer.GetMessages();
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("hello", messages[1].Content);
        Assert.Equal(2, buffer.MessageCount);
    }

    [Fact]
    public void Truncate_keeps_head_plus_last_n()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(new[]
        {
            Msg("system", "S"), Msg("user", "1"), Msg("assistant", "2"),
            Msg("user", "3"), Msg("assistant", "4"),
        });

        var removed = buffer.Truncate(retainCount: 2);

        var kept = buffer.GetMessages();
        Assert.Equal(3, kept.Count);              // head + last 2
        Assert.Equal("system", kept[0].Role);     // head preserved
        Assert.Equal("3", kept[1].Content);
        Assert.Equal("4", kept[2].Content);
        Assert.Equal(2, removed);
    }

    [Fact]
    public void Reset_clears_and_reports_removed()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(new[] { Msg("user", "a"), Msg("user", "b") });

        var removed = buffer.Reset();

        Assert.Equal(2, removed);
        Assert.Empty(buffer.GetMessages());
    }

    [Fact]
    public void AppendNote_and_SetTitle_reflected_in_metadata()
    {
        var buffer = new InMemorySessionBufferService();
        Assert.True(buffer.AppendNote("remember this"));
        Assert.True(buffer.SetTitle("My Session"));

        var meta = buffer.GetMetadata();
        Assert.Equal("My Session", meta.Title);
        Assert.Equal(1, meta.MessageCount);
        Assert.False(string.IsNullOrEmpty(meta.SessionId));
    }

    [Fact]
    public void EstimateTokenCount_grows_with_content()
    {
        var buffer = new InMemorySessionBufferService();
        var empty = buffer.EstimateTokenCount();
        buffer.ReplaceMessages(new[] { Msg("user", new string('x', 400)) });
        var full = buffer.EstimateTokenCount();

        Assert.Equal(0, empty);
        Assert.True(full >= 100, $"expected ≥100 tokens for 400 chars, got {full}");
    }

    [Fact]
    public void AppendNote_rejects_blank()
    {
        var buffer = new InMemorySessionBufferService();
        Assert.False(buffer.AppendNote("  "));
        Assert.Empty(buffer.GetMessages());
    }

    [Fact]
    public void State_bag_round_trips_removes_and_survives_message_rewrites()
    {
        var buffer = new InMemorySessionBufferService();

        Assert.Null(buffer.GetState("autocompact_failures"));

        buffer.SetState("autocompact_failures", "2");
        Assert.Equal("2", buffer.GetState("autocompact_failures"));

        // Message rewrites (compaction) must not touch the state bag.
        buffer.ReplaceMessages(new[] { Msg("user", "a") });
        buffer.Truncate(retainCount: 0);
        Assert.Equal("2", buffer.GetState("autocompact_failures"));

        buffer.SetState("autocompact_failures", null);
        Assert.Null(buffer.GetState("autocompact_failures"));
    }

    [Fact]
    public void Reset_clears_the_state_bag()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.SetState("k", "v");

        buffer.Reset();

        Assert.Null(buffer.GetState("k"));
    }

    // ── SessionStoreTool ────────────────────────────────────────────────────

    [Fact]
    public async Task SessionStore_write_then_read_round_trips()
    {
        var buffer = new InMemorySessionBufferService();
        using var tool = new SessionStoreTool(buffer);

        var write = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "write_messages",
            ["messages"] = new List<object>
            {
                new Dictionary<string, object?> { ["role"] = "user", ["content"] = "ping" },
                new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = "pong" },
            },
        }), TestContext.Current.CancellationToken);
        ResultDict(write);
        Assert.Equal(2, buffer.MessageCount);

        var read = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "read_messages",
        }), TestContext.Current.CancellationToken);
        var dict = ResultDict(read);
        var messages = Assert.IsType<System.Collections.IEnumerable>(dict["messages"], exactMatch: false);
        Assert.Equal(2, messages.Cast<object>().Count());
    }

    [Fact]
    public async Task SessionStore_truncate_reports_removed_count()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(Enumerable.Range(0, 6).Select(i => Msg("user", i.ToString())).ToArray());
        using var tool = new SessionStoreTool(buffer);

        var resp = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "truncate",
            ["retain_count"] = 2,
        }), TestContext.Current.CancellationToken);

        var dict = ResultDict(resp);
        Assert.Equal(3, Convert.ToInt32(dict["removed_count"]));   // 6 → head + 2
    }

    [Fact]
    public async Task SessionStore_get_metadata_returns_metadata()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.SetTitle("T");
        using var tool = new SessionStoreTool(buffer);

        var resp = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "get_metadata",
        }), TestContext.Current.CancellationToken);

        var dict = ResultDict(resp);
        Assert.NotNull(dict["metadata"]);
    }

    [Fact]
    public async Task SessionStore_set_state_then_get_state_round_trips()
    {
        var buffer = new InMemorySessionBufferService();
        using var tool = new SessionStoreTool(buffer);

        var set = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "set_state",
            ["key"] = "autocompact_failures",
            ["value"] = "3",
        }), TestContext.Current.CancellationToken);
        ResultDict(set);

        var get = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "get_state",
            ["key"] = "autocompact_failures",
        }), TestContext.Current.CancellationToken);

        var dict = ResultDict(get);
        Assert.Equal("3", dict["value"]);
    }

    [Fact]
    public async Task SessionStore_get_state_of_unset_key_returns_null_value()
    {
        var buffer = new InMemorySessionBufferService();
        using var tool = new SessionStoreTool(buffer);

        var resp = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
        {
            ["operation"] = "get_state",
            ["key"] = "never_set",
        }), TestContext.Current.CancellationToken);

        var dict = ResultDict(resp);
        Assert.True(!dict.TryGetValue("value", out var v) || v is null);
    }

    [Fact]
    public async Task SessionStore_state_ops_without_key_fail()
    {
        var buffer = new InMemorySessionBufferService();
        using var tool = new SessionStoreTool(buffer);

        foreach (var op in new[] { "get_state", "set_state" })
        {
            var resp = await tool.CallAsync(new ToolCallRequest("session_store", new Dictionary<string, object?>
            {
                ["operation"] = op,
            }), TestContext.Current.CancellationToken);

            // The typed pipeline surfaces the operation's success=false at the protocol level.
            Assert.False(resp.Success);
        }
    }

    // ── SessionSnipTool ─────────────────────────────────────────────────────

    [Fact]
    public async Task SessionSnip_truncates_to_window()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(Enumerable.Range(0, 5).Select(i => Msg("user", i.ToString())).ToArray());
        using var tool = new SessionSnipTool(buffer);

        var resp = await tool.CallAsync(new ToolCallRequest("session_snip", new Dictionary<string, object?>
        {
            ["retain_count"] = 1,
        }), TestContext.Current.CancellationToken);

        var dict = ResultDict(resp);
        Assert.Equal(2, buffer.MessageCount);                            // head + 1
        Assert.Equal(3, Convert.ToInt32(dict["removed_count"]));
        Assert.Equal(2, Convert.ToInt32(dict["retained_count"]));
    }

    // ── TokenBudgetTool ─────────────────────────────────────────────────────

    [Fact]
    public async Task TokenBudget_reports_window_used_and_available()
    {
        var buffer = new InMemorySessionBufferService();
        buffer.ReplaceMessages(new[] { Msg("user", new string('x', 4000)) });
        using var tool = new TokenBudgetTool(buffer, configuration: null);

        var resp = await tool.CallAsync(new ToolCallRequest("token_budget", new Dictionary<string, object?>()), TestContext.Current.CancellationToken);

        var dict = ResultDict(resp);
        var window = Convert.ToInt32(dict["context_window_tokens"]);
        var used = Convert.ToInt32(dict["used_tokens"]);
        var available = Convert.ToInt32(dict["available_tokens"]);
        Assert.Equal(200_000, window);
        Assert.True(used >= 1000);
        Assert.Equal(window - used, available);
    }
}
