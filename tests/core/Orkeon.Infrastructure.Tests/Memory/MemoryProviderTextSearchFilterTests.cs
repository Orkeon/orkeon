using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// GAP-20 — <see cref="IMemoryProvider.SearchAsync"/> takes the metadata filter of
/// <see cref="IMemoryProvider.SearchSimilarAsync"/> (<c>source</c> equality, <c>tag</c>/<c>tags</c>
/// membership, any other key an equality on the custom property of that name) and applies it
/// <b>before</b> the limit: a filtered search returns every match the store holds, up to the limit,
/// never fewer because non-matching entries filled the page. In-Memory and SQLite, the providers
/// that run offline; the server-backed ones translate the filter to their query (their own tests).
/// </summary>
public sealed class MemoryProviderTextSearchFilterTests : IDisposable
{
    private readonly InMemoryProvider _inMemory = new();
    private readonly SqliteMemoryProvider _sqlite = new(
        Options.Create(new SqliteMemoryOptions { ConnectionString = "Data Source=:memory:" }),
        new FakeFileSystemService());

    public void Dispose() => _sqlite.Dispose();

    public static TheoryData<string> Providers => new() { "inmemory", "sqlite" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IMemoryProvider Provider(string type) => type == "sqlite" ? _sqlite : _inMemory;

    private static MemoryItem Item(string content, string crew, string source = "task_execution", string[]? tags = null) =>
        MemoryItem.Create(
            content,
            source: source,
            tags: tags,
            customProperties: new Dictionary<string, string> { ["kind"] = "crew-memory", ["crew"] = crew });

    private static Dictionary<string, object> CrewFilter(string crew) => new() { ["kind"] = "crew-memory", ["crew"] = crew };

    private static async Task<List<string>> ContentsAsync(Task<IEnumerable<MemoryItem>> search) =>
        [.. (await search).Select(i => i.Content).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task The_filter_applies_before_the_limit(string type)
    {
        var provider = Provider(type);
        // Five entries of another crew are stored after these two (SQLite pages the newest
        // first): a filter applied to a page of two would find nothing.
        await provider.StoreAsync("mine-1", Item("contract clause 4", "legal-watch"), Ct);
        await provider.StoreAsync("mine-2", Item("contract clause 9", "legal-watch"), Ct);
        for (var i = 0; i < 5; i++)
            await provider.StoreAsync($"other-{i}", Item($"contract note {i}", "customer-follow-up"), Ct);

        var found = await ContentsAsync(provider.SearchAsync("contract", 2, CrewFilter("legal-watch"), Ct));

        Assert.Equal(["contract clause 4", "contract clause 9"], found);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_custom_property_matches_by_equality_not_by_substring(string type)
    {
        var provider = Provider(type);
        await provider.StoreAsync("ops", Item("contract archive", "legal ops"), Ct);
        await provider.StoreAsync("watch", Item("contract clause 4", "legal-watch"), Ct);

        Assert.Empty(await ContentsAsync(provider.SearchAsync("contract", 10, CrewFilter("legal"), Ct)));
        Assert.Equal(["contract archive"], await ContentsAsync(provider.SearchAsync("contract", 10, CrewFilter("legal ops"), Ct)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_entry_without_the_property_does_not_match(string type)
    {
        var provider = Provider(type);
        await provider.StoreAsync("chunk", MemoryItem.Create("The contract may be terminated.", source: "/kb/contracts.md"), Ct);

        Assert.Empty(await ContentsAsync(provider.SearchAsync("contract", 10, CrewFilter("legal-watch"), Ct)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Source_and_tags_filter_as_in_a_similarity_search(string type)
    {
        var provider = Provider(type);
        await provider.StoreAsync("a", Item("contract from docs", "legal-watch", source: "docs", tags: ["howto"]), Ct);
        await provider.StoreAsync("b", Item("contract from wiki", "legal-watch", source: "wiki", tags: ["reference"]), Ct);

        Assert.Equal(["contract from wiki"], await ContentsAsync(provider.SearchAsync("contract", 10, new() { ["source"] = "wiki" }, Ct)));
        Assert.Equal(["contract from docs"], await ContentsAsync(provider.SearchAsync("contract", 10, new() { ["tags"] = "howto" }, Ct)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_empty_query_returns_what_the_filter_selects(string type)
    {
        var provider = Provider(type);
        await provider.StoreAsync("other", Item("a note", "customer-follow-up"), Ct);
        await provider.StoreAsync("mine", Item("a clause", "legal-watch"), Ct);

        Assert.Equal(["a clause"], await ContentsAsync(provider.SearchAsync(string.Empty, 10, CrewFilter("legal-watch"), Ct)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Without_a_filter_the_search_is_unchanged(string type)
    {
        var provider = Provider(type);
        await provider.StoreAsync("other", Item("contract note", "customer-follow-up"), Ct);
        await provider.StoreAsync("mine", Item("contract clause", "legal-watch"), Ct);

        Assert.Equal(["contract clause", "contract note"], await ContentsAsync(provider.SearchAsync("contract", 10, cancellationToken: Ct)));
    }

    [Fact]
    public async Task Sqlite_matches_a_custom_property_of_a_similarity_search_by_equality_too()
    {
        // The two entry points share one filter: the record-level probe used to look for the
        // value anywhere in the custom-properties JSON.
        await _sqlite.StoreAsync("ops", MemoryItem.Create("archive", embedding: [1f, 0f],
            customProperties: new Dictionary<string, string> { ["crew"] = "legal ops" }), Ct);

        var found = await _sqlite.SearchSimilarAsync([1f, 0f], topK: 10, minScore: 0f,
            filter: new Dictionary<string, object> { ["crew"] = "legal" }, cancellationToken: Ct);

        Assert.Empty(found);
    }
}
