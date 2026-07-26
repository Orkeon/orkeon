using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using Fixture = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture;
using TestRow = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture.LanceDbTestRow;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// HTTP-level tests for the Domain capabilities of <c>LanceDbMemoryProvider</c>
/// (RAG-04/C2): <see cref="IScoredVectorSearch"/> on the default table (native scores,
/// keys, typed filter, minScore as given) and <see cref="IHybridSearchCapable"/> — the
/// formerly out-of-interface hybrid search exposed through the contract, both
/// default-table and collection-scoped (per-collection tables + lazy FTS index).
/// </summary>
public class LanceDbMemoryProviderCapabilityTests
{
    private const string DefaultQueryPath = "/v1/table/test_memories/query";

    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] UnitX4 = [1f, 0f, 0f, 0f];

    private readonly Fixture _fixture = new();

    // --- Discovery ---

    [Fact]
    public void Capabilities_AreDiscoverable_ThroughTryGetCapability()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        Assert.True(((IMemoryProvider)provider).TryGetCapability<IScoredVectorSearch>(out _));
        Assert.True(((IMemoryProvider)provider).TryGetCapability<IHybridSearchCapable>(out _));
        Assert.True(((IMemoryProvider)provider).TryGetCapability<ICollectionAwareMemory>(out _));
    }

    // --- IScoredVectorSearch (default table) ---

    [Fact]
    public async Task SearchSimilarWithScores_QueriesTheDefaultTable_WithScoresAndKeys()
    {
        using var handler = new FakeHttpMessageHandler();
        using var exists = Fixture.Ok();
        handler.EnqueueResponse(exists);
        using var rows = Fixture.ArrowRows(
            new TestRow("s1", "Similar item 1", Distance: 0.05f),
            new TestRow("s2", "Similar item 2", Distance: 0.3f));
        handler.EnqueueResponse(rows);
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            UnitX4, topK: 5, minScore: float.MinValue,
            new MemoryFilter { Source = "research" }, TestCt);

        Assert.Equal(DefaultQueryPath, handler.CapturedRequests[1].RequestUri!.AbsolutePath);
        var body = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestCt);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("source = 'research'", doc.RootElement.GetProperty("filter").GetString());
        Assert.True(doc.RootElement.GetProperty("prefilter").GetBoolean());
        Assert.Equal(5, doc.RootElement.GetProperty("k").GetInt32());

        Assert.Equal(2, results.Count);
        Assert.Equal("s1", results[0].Key);
        Assert.Equal(0.95f, results[0].Score, precision: 5);
        Assert.Equal("s2", results[1].Key);
        Assert.Equal(0.7f, results[1].Score, precision: 5);
    }

    [Fact]
    public async Task SearchSimilarWithScores_AppliesMinScoreAsGiven_NoProviderDefault()
    {
        // Options carry MinSimilarityScore = 0 but the caller's 0.8 must win as given;
        // conversely a negative caller threshold must not be replaced by any default.
        using var handler = new FakeHttpMessageHandler();
        using var exists = Fixture.Ok();
        handler.EnqueueResponse(exists);
        using var rows = Fixture.ArrowRows(
            new TestRow("close", "Close", Distance: 0.05f),
            new TestRow("far", "Far", Distance: 0.9f));
        handler.EnqueueResponse(rows);
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            UnitX4, topK: 10, minScore: 0.8f, cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("close", single.Key);
    }

    // --- IHybridSearchCapable (default table) ---

    [Fact]
    public async Task HybridSearch_InterfaceLevel_FusesBothServerRankedLists_WithKeys()
    {
        // Vector list ranks A then B; full-text list ranks A then C (same shape as the
        // legacy out-of-interface entry point — weights 0.7/0.3 → A=0.93, B=0.35, C=0.3).
        using var handler = new FakeHttpMessageHandler();
        using var exists = Fixture.Ok();
        handler.EnqueueResponse(exists);
        using var vectorRows = Fixture.ArrowRows(
            new TestRow("A", "Machine learning algorithms", Distance: 0.1f),
            new TestRow("B", "Vector only item", Distance: 0.5f));
        handler.EnqueueResponse(vectorRows);
        using var textRows = Fixture.ArrowRows(
            new TestRow("A", "Machine learning algorithms", Score: 4.0f),
            new TestRow("C", "Text only learning item", Score: 2.0f));
        handler.EnqueueResponse(textRows);
        using var provider = _fixture.CreateProvider(handler);

        IHybridSearchCapable hybrid = provider;
        var results = await hybrid.HybridSearchAsync(
            "learning", UnitX4.AsMemory(), topK: 10, cancellationToken: TestCt);

        Assert.Equal(3, results.Count);
        Assert.Equal("A", results[0].Key);
        Assert.Equal(0.93f, results[0].Score, precision: 4);

        Assert.Equal(DefaultQueryPath, handler.CapturedRequests[1].RequestUri!.AbsolutePath);
        Assert.Equal(DefaultQueryPath, handler.CapturedRequests[2].RequestUri!.AbsolutePath);
        var textBody = await handler.CapturedRequests[2].Content!.ReadAsStringAsync(TestCt);
        Assert.Contains("\"full_text_query\"", textBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridSearch_InterfaceLevel_CarriesTheTypedFilter_OnBothQueries()
    {
        using var handler = new FakeHttpMessageHandler();
        using var exists = Fixture.Ok();
        handler.EnqueueResponse(exists);
        using var vectorRows = Fixture.ArrowRows();
        handler.EnqueueResponse(vectorRows);
        using var textRows = Fixture.ArrowRows();
        handler.EnqueueResponse(textRows);
        using var provider = _fixture.CreateProvider(handler);

        IHybridSearchCapable hybrid = provider;
        await hybrid.HybridSearchAsync(
            "query", UnitX4.AsMemory(), topK: 4,
            new MemoryFilter { Source = "research" }, TestCt);

        foreach (var index in new[] { 1, 2 })
        {
            var body = await handler.CapturedRequests[index].Content!.ReadAsStringAsync(TestCt);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("source = 'research'", doc.RootElement.GetProperty("filter").GetString());
        }
    }

    // --- IHybridSearchCapable (collection-scoped) ---

    [Fact]
    public async Task HybridSearch_CollectionScoped_TargetsTheCollectionTable_AndEnsuresFtsIndex()
    {
        using var handler = new FakeHttpMessageHandler();
        using var tableExists = Fixture.Ok();     // exists → 200 (table already there)
        handler.EnqueueResponse(tableExists);
        using var indexCreated = Fixture.Ok();    // lazy best-effort FTS index creation
        handler.EnqueueResponse(indexCreated);
        using var vectorRows = Fixture.ArrowRows(
            new TestRow("A", "Alpha chunk", Distance: 0.2f));
        handler.EnqueueResponse(vectorRows);
        using var textRows = Fixture.ArrowRows(
            new TestRow("A", "Alpha chunk", Score: 3.0f),
            new TestRow("B", "Beta chunk", Score: 1.0f));
        handler.EnqueueResponse(textRows);
        using var provider = _fixture.CreateProvider(handler);

        IHybridSearchCapable hybrid = provider;
        var results = await hybrid.HybridSearchAsync(
            "docs", "chunk", UnitX4.AsMemory(), topK: 10, cancellationToken: TestCt);

        Assert.Equal("/v1/table/docs/exists", handler.CapturedRequests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/table/docs/create_index", handler.CapturedRequests[1].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/table/docs/query", handler.CapturedRequests[2].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/table/docs/query", handler.CapturedRequests[3].RequestUri!.AbsolutePath);

        var indexBody = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestCt);
        Assert.Contains("\"index_type\":\"FTS\"", indexBody, StringComparison.Ordinal);
        Assert.Contains("\"column\":\"content\"", indexBody, StringComparison.Ordinal);

        // A appears in both lists (0.7*0.8 + 0.3*1.0 = 0.86); B is text-only (0.3*1/3 = 0.1).
        Assert.Equal(2, results.Count);
        Assert.Equal("A", results[0].Key);
        Assert.Equal(0.86f, results[0].Score, precision: 4);
        Assert.Equal("B", results[1].Key);
    }

    [Fact]
    public async Task HybridSearch_CollectionScoped_FtsIndexFailure_IsBestEffort()
    {
        using var handler = new FakeHttpMessageHandler();
        using var tableExists = Fixture.Ok();
        handler.EnqueueResponse(tableExists);
        using var indexFailed = Fixture.Json(System.Net.HttpStatusCode.BadRequest, "{\"error\":\"index exists\"}");
        handler.EnqueueResponse(indexFailed);
        using var vectorRows = Fixture.ArrowRows(new TestRow("A", "Alpha", Distance: 0.2f));
        handler.EnqueueResponse(vectorRows);
        using var textRows = Fixture.ArrowRows();
        handler.EnqueueResponse(textRows);
        using var provider = _fixture.CreateProvider(handler);

        IHybridSearchCapable hybrid = provider;
        var results = await hybrid.HybridSearchAsync(
            "docs", "alpha", UnitX4.AsMemory(), topK: 5, cancellationToken: TestCt);

        // The failed create_index never aborts the hybrid query.
        var single = Assert.Single(results);
        Assert.Equal("A", single.Key);
    }
}
