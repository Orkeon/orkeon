using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.Pinecone;

/// <summary>
/// GAP-20 — Pinecone filters on the server. A text search sends the metadata filter with its own
/// condition on the content, under one <c>$and</c>, so the index applies both before <c>topK</c>.
/// A filter on a custom property needs the property to be stored: the default namespace keeps an
/// item's custom properties, as the named namespaces do, and gives them back.
/// </summary>
public class PineconeMemoryProviderTextSearchFilterTests
{
    private static readonly Dictionary<string, object> LegalWatch = new() { ["kind"] = "crew-memory", ["crew"] = "legal-watch" };

    private readonly PineconeMemoryProviderTestsFixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> LastBodyAsync(FakeHttpMessageHandler handler)
    {
        var body = await handler.CapturedRequests[^1].Content!.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string? EqualityOn(List<JsonElement> conditions, string key) =>
        conditions.FirstOrDefault(c => c.TryGetProperty(key, out _)) is { ValueKind: JsonValueKind.Object } condition
            ? condition.GetProperty(key).GetProperty("$eq").GetString()
            : null;

    [Fact]
    public async Task A_text_search_sends_the_filter_with_its_content_condition()
    {
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { matches = Array.Empty<object>() });
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync("contract clause 4", 5, LegalWatch, Ct);

        var conditions = (await LastBodyAsync(handler)).GetProperty("filter").GetProperty("$and").EnumerateArray().ToList();
        Assert.Equal(3, conditions.Count);
        Assert.Equal("contract clause 4", EqualityOn(conditions, "content"));
        Assert.Equal("crew-memory", EqualityOn(conditions, "kind"));
        Assert.Equal("legal-watch", EqualityOn(conditions, "crew"));
    }

    [Fact]
    public async Task A_text_search_without_a_filter_sends_its_content_condition_alone()
    {
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { matches = Array.Empty<object>() });
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchAsync("contract clause 4", 5, cancellationToken: Ct);

        var filter = (await LastBodyAsync(handler)).GetProperty("filter");
        Assert.Equal("contract clause 4", filter.GetProperty("content").GetProperty("$eq").GetString());
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
    public async Task A_text_search_gives_the_custom_properties_back()
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

        var item = Assert.Single(await provider.SearchAsync("contract clause 4", 5, LegalWatch, Ct));

        Assert.Equal("legal-watch", item.Metadata.CustomProperties?.GetValueOrDefault("crew"));
        Assert.Equal("crew-memory", item.Metadata.CustomProperties?.GetValueOrDefault("kind"));
        Assert.Contains("crew:legal-watch", item.Tags);
    }
}
