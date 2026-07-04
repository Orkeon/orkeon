using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsMemoryScopeTests
{
    [Fact]
    public async Task store_then_get_roundtrips_value()
    {
        var scope = new JsMemoryScope();

        await scope.store("k", "v");
        var value = await scope.get("k");

        Assert.Equal("v", value);
    }

    [Fact]
    public async Task get_missing_key_returns_null()
    {
        var scope = new JsMemoryScope();

        Assert.Null(await scope.get("absent"));
    }

    [Fact]
    public async Task delete_existing_key_returns_true_and_removes_it()
    {
        var scope = new JsMemoryScope();
        await scope.store("k", 42);

        var deleted = await scope.delete("k");

        Assert.True(deleted);
        Assert.Null(await scope.get("k"));
    }

    [Fact]
    public async Task delete_missing_key_returns_false()
    {
        var scope = new JsMemoryScope();

        Assert.False(await scope.delete("absent"));
    }

    [Fact]
    public async Task search_matches_by_key_substring()
    {
        var scope = new JsMemoryScope();
        await scope.store("user:alice", "data");
        await scope.store("user:bob", "data");
        await scope.store("config", "data");

        var hits = await scope.search("user", 10);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Contains("user", h.id));
    }

    [Fact]
    public async Task search_matches_by_value_substring()
    {
        var scope = new JsMemoryScope();
        await scope.store("k1", "the quick brown fox");
        await scope.store("k2", "lazy dog");

        var hits = await scope.search("quick", 10);

        var hit = Assert.Single(hits);
        Assert.Equal("k1", hit.id);
    }

    [Fact]
    public async Task search_empty_query_matches_everything()
    {
        var scope = new JsMemoryScope();
        await scope.store("a", 1);
        await scope.store("b", 2);

        var hits = await scope.search("", 10);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task search_negative_k_defaults_to_five()
    {
        var scope = new JsMemoryScope();
        for (var i = 0; i < 10; i++)
            await scope.store($"key{i}", "value");

        var hits = await scope.search("key", -1);

        Assert.Equal(5, hits.Count);
    }

    [Fact]
    public async Task search_assigns_decreasing_scores()
    {
        var scope = new JsMemoryScope();
        await scope.store("match-a", "x");
        await scope.store("match-b", "x");

        var hits = await scope.search("match", 10);

        Assert.Equal(1.0, hits[0].score, 5);
        Assert.True(hits[1].score < hits[0].score);
    }

    [Fact]
    public async Task search_hit_carries_stored_value()
    {
        var scope = new JsMemoryScope();
        await scope.store("only", "payload");

        var hit = Assert.Single(await scope.search("only", 5));

        Assert.Equal("payload", hit.value);
    }
}
