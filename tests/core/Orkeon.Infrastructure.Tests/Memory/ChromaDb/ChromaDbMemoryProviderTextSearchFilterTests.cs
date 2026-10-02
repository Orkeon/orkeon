using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Memory.ChromaDb;

/// <summary>
/// GAP-20 — ChromaDB filters on the server. A text search sends the metadata filter as the query's
/// <c>where</c> clause, so the server applies it before <c>n_results</c>; several conditions travel
/// under one <c>$and</c>, the only shape ChromaDB accepts. A filter on a custom property needs the
/// property to be stored: the default collection keeps an item's custom properties, as the named
/// collections do, and gives them back.
/// </summary>
public class ChromaDbMemoryProviderTextSearchFilterTests
{
    private static readonly Dictionary<string, object> LegalWatch = new() { ["kind"] = "crew-memory", ["crew"] = "legal-watch" };

    private readonly ChromaDbMemoryProviderTestsFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object EmptyQueryResult => new { ids = new[] { Array.Empty<string>() } };

    private static async Task<JsonElement> LastBodyAsync(FakeHttpMessageHandler handler)
    {
        var body = await handler.CapturedRequests[^1].Content!.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static void AssertEqualityConditions(JsonElement where)
    {
        var conditions = where.GetProperty("$and").EnumerateArray().ToList();
        Assert.Equal(2, conditions.Count);
        Assert.Contains(conditions, c => c.TryGetProperty("kind", out var kind) && kind.GetProperty("$eq").GetString() == "crew-memory");
        Assert.Contains(conditions, c => c.TryGetProperty("crew", out var crew) && crew.GetProperty("$eq").GetString() == "legal-watch");
    }

    [Fact]
    public async Task A_text_search_sends_the_filter_as_its_where_clause()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, EmptyQueryResult);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync("contract", 5, LegalWatch, Ct);

        var body = await LastBodyAsync(handler);
        Assert.Equal("contract", body.GetProperty("query_texts")[0].GetString());
        Assert.Equal(5, body.GetProperty("n_results").GetInt32());
        AssertEqualityConditions(body.GetProperty("where"));
    }

    [Fact]
    public async Task A_text_search_without_a_filter_sends_no_where_clause()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, EmptyQueryResult);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync("contract", 5, cancellationToken: Ct);

        Assert.False((await LastBodyAsync(handler)).TryGetProperty("where", out _));
    }

    [Fact]
    public async Task A_similarity_search_on_two_keys_sends_them_under_one_and()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, EmptyQueryResult);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchSimilarAsync([1f, 0f], filter: LegalWatch, cancellationToken: Ct);

        AssertEqualityConditions((await LastBodyAsync(handler)).GetProperty("where"));
    }

    [Fact]
    public async Task A_stored_item_keeps_its_custom_properties_in_the_default_collection()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, new { });
        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(
            "contract clause 4",
            source: "task_execution",
            customProperties: new Dictionary<string, string> { ["kind"] = "crew-memory", ["crew"] = "legal-watch", ["source"] = "spoofed" });

        await provider.StoreAsync("k1", item, Ct);

        var metadata = (await LastBodyAsync(handler)).GetProperty("metadatas")[0];
        Assert.Equal("crew-memory", metadata.GetProperty("kind").GetString());
        Assert.Equal("legal-watch", metadata.GetProperty("crew").GetString());
        // A reserved key is the provider's: a custom property never overrides it.
        Assert.Equal("task_execution", metadata.GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_text_search_gives_the_custom_properties_back()
    {
        var result = new
        {
            ids = new[] { new[] { "k1" } },
            documents = new[] { new[] { "contract clause 4" } },
            metadatas = new[]
            {
                new[]
                {
                    new Dictionary<string, object>
                    {
                        ["importance"] = 0.8, ["source"] = "task_execution", ["tags"] = "agent:a,crew:legal-watch",
                        ["kind"] = "crew-memory", ["crew"] = "legal-watch",
                    },
                },
            },
            distances = new[] { new[] { 0.1f } },
        };
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, result);
        using var provider = _fixture.CreateProvider(handler);

        var item = Assert.Single(await provider.SearchAsync("contract", 5, LegalWatch, Ct));

        Assert.Equal("legal-watch", item.Metadata.CustomProperties?.GetValueOrDefault("crew"));
        Assert.Equal("crew-memory", item.Metadata.CustomProperties?.GetValueOrDefault("kind"));
        Assert.Contains("crew:legal-watch", item.Tags);
    }
}
