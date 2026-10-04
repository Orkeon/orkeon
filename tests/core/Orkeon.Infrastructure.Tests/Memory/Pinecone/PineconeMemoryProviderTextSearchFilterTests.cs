using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.Pinecone;

/// <summary>
/// GAP-20, GAP-30 — Pinecone stores vectors and searches by vector. A memory is sent with its
/// values, and one without is refused before any request, as the namespace path refuses it — a
/// crew's memory used to go out with <c>values: []</c>, which a real index rejects, failing every
/// task. A text search, which Pinecone does not have, is refused rather than faked with an empty
/// vector and an exact match on the content. The metadata filter travels with a similarity search,
/// applied by the index before <c>topK</c>; the default namespace keeps an item's custom properties,
/// as the named namespaces do, and gives them back with the key of each match.
/// </summary>
public class PineconeMemoryProviderTextSearchFilterTests
{
    private static readonly Dictionary<string, object> LegalWatch = new() { ["kind"] = "crew-memory", ["crew"] = "legal-watch" };

    private readonly PineconeMemoryProviderTestsFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> LastBodyAsync(FakeHttpMessageHandler handler)
    {
        var body = await handler.CapturedRequests[^1].Content!.ReadAsStringAsync(Ct);
        return JsonElement.Parse(body);
    }

    [Fact]
    public async Task A_text_search_is_refused_and_sends_nothing()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.SearchAsync("contract clause 4", 5, LegalWatch, Ct));

        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task A_similarity_search_sends_the_filter_one_equality_per_key()
    {
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { matches = Array.Empty<object>() });
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchSimilarAsync([0.1f, 0.2f], 5, 0f, LegalWatch, Ct);

        var filter = (await LastBodyAsync(handler)).GetProperty("filter");
        Assert.Equal("crew-memory", filter.GetProperty("kind").GetProperty("$eq").GetString());
        Assert.Equal("legal-watch", filter.GetProperty("crew").GetProperty("$eq").GetString());
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
    public async Task A_memory_with_a_vector_is_sent_with_values_of_its_dimension()
    {
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { upsertedCount = 1 });
        using var provider = _fixture.CreateProvider(handler);

        await provider.StoreAsync("k1", MemoryItem.Create("contract clause 4", embedding: [0.1f, 0.2f, 0.3f, 0.4f]), Ct);

        var values = (await LastBodyAsync(handler)).GetProperty("vectors")[0].GetProperty("values");
        Assert.Equal(4, values.GetArrayLength());
    }

    [Fact]
    public async Task A_stored_item_keeps_its_custom_properties_in_the_default_namespace()
    {
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { upsertedCount = 1 });
        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(
            "contract clause 4",
            embedding: [0.1f, 0.2f],
            source: "task_execution",
            customProperties: new Dictionary<string, string> { ["kind"] = "crew-memory", ["crew"] = "legal-watch", ["content"] = "spoofed" });

        await provider.StoreAsync("k1", item, Ct);

        var metadata = (await LastBodyAsync(handler)).GetProperty("vectors")[0].GetProperty("metadata");
        Assert.Equal("crew-memory", metadata.GetProperty("kind").GetString());
        Assert.Equal("legal-watch", metadata.GetProperty("crew").GetString());
        // A reserved key is the provider's: a custom property never overrides it.
        Assert.Equal("contract clause 4", metadata.GetProperty("content").GetString());
    }

    [Fact]
    public async Task A_similarity_search_gives_the_custom_properties_and_the_key_back()
    {
        var result = new
        {
            matches = new[]
            {
                new
                {
                    id = "k1",
                    score = 0.9f,
                    values = new[] { 0.1f, 0.2f },
                    metadata = new Dictionary<string, object>
                    {
                        ["content"] = "contract clause 4", ["importance"] = 0.8, ["source"] = "task_execution",
                        ["tags"] = "agent:a,crew:legal-watch", ["kind"] = "crew-memory", ["crew"] = "legal-watch",
                    },
                },
            },
        };
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, result);
        using var provider = _fixture.CreateProvider(handler);

        var match = Assert.Single(await provider.SearchSimilarAsync([0.1f, 0.2f], 5, 0f, LegalWatch, Ct));

        Assert.Equal("k1", match.Key);
        Assert.Equal("legal-watch", match.Item.Metadata.CustomProperties?.GetValueOrDefault("crew"));
        Assert.Equal("crew-memory", match.Item.Metadata.CustomProperties?.GetValueOrDefault("kind"));
        Assert.Contains("crew:legal-watch", match.Item.Tags);
    }
}
