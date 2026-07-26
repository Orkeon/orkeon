using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.ChromaDb;

/// <summary>
/// HTTP-level tests for the non-scoped <see cref="IScoredVectorSearch"/> capability of
/// <see cref="ChromaDbMemoryProvider"/> (RAG-04/C2, reliquat RAG-02): the query targets
/// the configured <b>default</b> collection, native scores (<c>1 - distance</c>) and
/// storage keys survive end to end, the typed filter compiles to a <c>where</c> clause,
/// and <c>minScore</c> is applied as given.
/// </summary>
public class ChromaDbMemoryProviderScoredSearchTests
{
    private const string CollectionsRoute = "/api/v2/tenants/default_tenant/databases/default_database/collections";

    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] QueryVector = [1f, 0f];

    private readonly ChromaDbMemoryProviderTestsFixture _fixture = new();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    private static void EnqueueJson(FakeHttpMessageHandler handler, object body)
    {
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        });
    }

    /// <summary>Enqueues the lazy get_or_create response of the configured default collection.</summary>
    private static void EnqueueDefaultCollection(FakeHttpMessageHandler handler)
        => EnqueueJson(handler, new { id = "test-collection-id", name = "test_collection" });

    private static object TwoScoredRows() => new
    {
        ids = new[] { new[] { "k-close", "k-far" } },
        documents = new[] { new[] { "close content", "far content" } },
        metadatas = new[] { new object[]
        {
            new Dictionary<string, object> { ["source"] = "guide.md", ["rag.kind"] = "chunk" },
            new Dictionary<string, object> { ["source"] = "other.md" },
        } },
        distances = new[] { new[] { 0.1f, 0.7f } },
    };

    [Fact]
    public void Capability_IsDiscoverable_ThroughTryGetCapability()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        Assert.True(((IMemoryProvider)provider).TryGetCapability<IScoredVectorSearch>(out _));
    }

    [Fact]
    public async Task SearchSimilarWithScores_TargetsTheDefaultCollection_WithScoresAndKeys()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueDefaultCollection(handler);
        EnqueueJson(handler, TwoScoredRows());
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            QueryVector, topK: 5, minScore: float.MinValue, cancellationToken: TestCt);

        // Request 0 = lazy get_or_create of the DEFAULT collection, request 1 = its query route.
        Assert.Equal(2, handler.CapturedRequests.Count);
        Assert.Equal(CollectionsRoute, handler.CapturedRequests[0].RequestUri!.AbsolutePath);
        Assert.Equal($"{CollectionsRoute}/test-collection-id/query",
            handler.CapturedRequests[1].RequestUri!.AbsolutePath);

        Assert.Equal(2, results.Count);
        Assert.Equal("k-close", results[0].Key);
        Assert.Equal(0.9f, results[0].Score, 0.001f);
        Assert.Equal("close content", results[0].Item.Content);
        Assert.Equal("guide.md", results[0].Item.Source);
        Assert.Equal("chunk", results[0].Item.Metadata.CustomProperties!["rag.kind"]);
        Assert.Equal("k-far", results[1].Key);
        Assert.Equal(0.3f, results[1].Score, 0.001f);
    }

    [Fact]
    public async Task SearchSimilarWithScores_CompilesTheTypedFilter_AndPassesTopK()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueDefaultCollection(handler);
        EnqueueJson(handler, new { ids = Array.Empty<string[]>() });
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchSimilarWithScoresAsync(
            QueryVector,
            topK: 7,
            minScore: 0f,
            new MemoryFilter
            {
                CustomProperties = new Dictionary<string, string> { ["rag.kind"] = "chunk" }
            },
            TestCt);

        var body = JsonDocument.Parse(
            await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestCt));
        using (body)
        {
            Assert.Equal(7, body.RootElement.GetProperty("n_results").GetInt32());
            Assert.Equal("chunk",
                body.RootElement.GetProperty("where").GetProperty("rag.kind").GetProperty("$eq").GetString());
        }
    }

    [Fact]
    public async Task SearchSimilarWithScores_AppliesMinScoreAsGiven()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueDefaultCollection(handler);
        EnqueueJson(handler, TwoScoredRows());
        using var provider = _fixture.CreateProvider(handler);

        // distance 0.7 → similarity 0.3, below the caller's 0.5 — no provider default substituted.
        var results = await provider.SearchSimilarWithScoresAsync(
            QueryVector, topK: 5, minScore: 0.5f, cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("k-close", single.Key);
    }

    [Fact]
    public async Task SearchSimilarWithScores_NonPositiveTopK_Throws()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            provider.SearchSimilarWithScoresAsync(QueryVector, topK: 0, minScore: 0f, cancellationToken: TestCt));
        Assert.Empty(handler.CapturedRequests);
    }
}
