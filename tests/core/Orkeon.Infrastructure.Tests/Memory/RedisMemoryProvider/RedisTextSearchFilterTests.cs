using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// GAP-20 — the Redis provider scans its key space and keeps, for a text search, every item whose
/// content holds the query and that passes the metadata filter, until the limit. The scan needs a
/// server; the decision for one candidate does not.
/// </summary>
public class RedisTextSearchFilterTests
{
    private static MemoryItem Item(string content, string crew, string source = "task_execution") =>
        MemoryItem.Create(
            content,
            source: source,
            tags: ["agent:alpha"],
            customProperties: new Dictionary<string, string> { ["kind"] = "crew-memory", ["crew"] = crew });

    private static readonly Dictionary<string, object> LegalWatch = new() { ["kind"] = "crew-memory", ["crew"] = "legal-watch" };

    [Fact]
    public void An_item_of_the_filtered_crew_whose_content_holds_the_query_is_a_hit()
    {
        Assert.True(RedisMemoryProvider.IsTextSearchHit(
            Item("contract clause 4", "legal-watch"), "CONTRACT", LegalWatch));
    }

    [Fact]
    public void An_item_of_another_crew_is_not_a_hit_whatever_its_content()
    {
        Assert.False(RedisMemoryProvider.IsTextSearchHit(
            Item("contract clause 4", "legal ops"), "contract", LegalWatch));
    }

    [Fact]
    public void An_item_without_the_filtered_properties_is_not_a_hit()
    {
        Assert.False(RedisMemoryProvider.IsTextSearchHit(
            MemoryItem.Create("The contract may be terminated.", source: "/kb/contracts.md"), "contract", LegalWatch));
    }

    [Fact]
    public void Source_and_tags_filter_as_in_a_similarity_search()
    {
        var item = Item("contract clause 4", "legal-watch", source: "docs");

        Assert.True(RedisMemoryProvider.IsTextSearchHit(item, "contract", new() { ["source"] = "docs", ["tag"] = "agent:alpha" }));
        Assert.False(RedisMemoryProvider.IsTextSearchHit(item, "contract", new() { ["source"] = "wiki" }));
    }

    [Fact]
    public void Without_a_filter_the_content_decides()
    {
        Assert.True(RedisMemoryProvider.IsTextSearchHit(Item("contract clause 4", "x"), "clause", null));
        Assert.False(RedisMemoryProvider.IsTextSearchHit(Item("contract clause 4", "x"), "invoice", null));
        Assert.False(RedisMemoryProvider.IsTextSearchHit(null, "contract", null));
    }
}
