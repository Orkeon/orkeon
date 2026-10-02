using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Memory.ChromaDb;

/// <summary>
/// GAP-20, GAP-30 — ChromaDB filters on the server, and its HTTP API takes vectors. A text search is
/// a <c>/get</c> of the documents that contain the query (<c>where_document</c> <c>$contains</c>) with
/// the metadata filter as its <c>where</c> clause — never <c>query_texts</c>, which only the clients
/// know, embedding it themselves; an empty query sends the filter alone. Several conditions travel
/// under one <c>$and</c>, the only shape ChromaDB accepts. A memory is stored with its vector, and one
/// without is refused before any request, as the collection path refuses it. A filter on a custom
/// property needs the property to be stored: the default collection keeps an item's custom
/// properties, as the named collections do, and gives them back.
/// </summary>
public class ChromaDbMemoryProviderTextSearchFilterTests
{
    private static readonly Dictionary<string, object> LegalWatch = new() { ["kind"] = "crew-memory", ["crew"] = "legal-watch" };

    private readonly ChromaDbMemoryProviderTestsFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object EmptyGetResult => new { ids = Array.Empty<string>(), documents = Array.Empty<string>() };

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
    public async Task A_text_search_gets_the_documents_containing_the_query_within_the_filter()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, EmptyGetResult);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync("contract", 5, LegalWatch, Ct);

        Assert.EndsWith("/test-collection-id/get", handler.CapturedRequests[^1].RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var body = await LastBodyAsync(handler);
        Assert.Equal("contract", body.GetProperty("where_document").GetProperty("$contains").GetString());
        Assert.Equal(5, body.GetProperty("limit").GetInt32());
        Assert.False(body.TryGetProperty("query_texts", out _));
        AssertEqualityConditions(body.GetProperty("where"));
    }

    [Fact]
    public async Task A_text_search_without_a_filter_sends_no_where_clause()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, EmptyGetResult);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync("contract", 5, cancellationToken: Ct);

        var body = await LastBodyAsync(handler);
        Assert.False(body.TryGetProperty("where", out _));
        Assert.Equal("contract", body.GetProperty("where_document").GetProperty("$contains").GetString());
    }

    [Fact]
    public async Task An_empty_text_search_sends_the_filter_alone()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, EmptyGetResult);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync(string.Empty, 5, LegalWatch, Ct);

        var body = await LastBodyAsync(handler);
        Assert.False(body.TryGetProperty("where_document", out _));
        AssertEqualityConditions(body.GetProperty("where"));
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
    public async Task A_similarity_search_returns_the_storage_key_of_each_match()
    {
        var result = new
        {
            ids = new[] { new[] { "k1" } },
            documents = new[] { new[] { "contract clause 4" } },
            metadatas = new[] { new[] { new Dictionary<string, object> { ["importance"] = 0.8 } } },
            distances = new[] { new[] { 0.1f } },
        };
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, result);
        using var provider = _fixture.CreateProvider(handler);

        var match = Assert.Single(await provider.SearchSimilarAsync([1f, 0f], cancellationToken: Ct));

        Assert.Equal("k1", match.Key);
    }

    [Fact]
    public async Task A_memory_without_a_vector_is_refused_before_any_request()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.StoreAsync("k1", MemoryItem.Create("contract clause 4"), Ct));

        Assert.Contains("embedding", error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task A_stored_item_keeps_its_custom_properties_and_its_vector_in_the_default_collection()
    {
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, new { });
        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(
            "contract clause 4",
            embedding: [0.1f, 0.2f, 0.3f],
            source: "task_execution",
            customProperties: new Dictionary<string, string> { ["kind"] = "crew-memory", ["crew"] = "legal-watch", ["source"] = "spoofed" });

        await provider.StoreAsync("k1", item, Ct);

        var body = await LastBodyAsync(handler);
        var metadata = body.GetProperty("metadatas")[0];
        Assert.Equal("crew-memory", metadata.GetProperty("kind").GetString());
        Assert.Equal("legal-watch", metadata.GetProperty("crew").GetString());
        // A reserved key is the provider's: a custom property never overrides it.
        Assert.Equal("task_execution", metadata.GetProperty("source").GetString());
        Assert.Equal(3, body.GetProperty("embeddings")[0].GetArrayLength());
    }

    [Fact]
    public async Task A_text_search_gives_the_custom_properties_back()
    {
        var result = new
        {
            ids = new[] { "k1" },
            documents = new[] { "contract clause 4" },
            metadatas = new[]
            {
                new Dictionary<string, object>
                {
                    ["importance"] = 0.8, ["source"] = "task_execution", ["tags"] = "agent:a,crew:legal-watch",
                    ["kind"] = "crew-memory", ["crew"] = "legal-watch",
                },
            },
        };
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(HttpStatusCode.OK, result);
        using var provider = _fixture.CreateProvider(handler);

        var item = Assert.Single(await provider.SearchAsync("contract", 5, LegalWatch, Ct));

        Assert.Equal("legal-watch", item.Metadata.CustomProperties?.GetValueOrDefault("crew"));
        Assert.Equal("crew-memory", item.Metadata.CustomProperties?.GetValueOrDefault("kind"));
        Assert.Contains("crew:legal-watch", item.Tags);
    }
}
