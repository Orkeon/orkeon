using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// HTTP-level tests for the <see cref="ICollectionAwareMemory"/> implementation of
/// <see cref="LanceDbMemoryProvider"/> (RAG-03/C2): a logical collection maps to a
/// dedicated LanceDB table (lazy creation, no FTS index), every request targets
/// <c>/v1/table/{collection}/...</c> instead of the configured default table, and typed
/// filters compile to server-side SQL predicates.
/// </summary>
public class LanceDbMemoryProviderCollectionTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] UnitX4 = [1f, 0f, 0f, 0f];
    private static readonly float[] UnitY4 = [0f, 1f, 0f, 0f];
    private static readonly float[] TwoDims = [1f, 0f];

    private readonly LanceDbMemoryProviderTestsFixture _fixture = new();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    private static void EnqueueOk(FakeHttpMessageHandler handler)
        => handler.EnqueueResponse(LanceDbMemoryProviderTestsFixture.Ok());

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    private static void EnqueueStatus(FakeHttpMessageHandler handler, HttpStatusCode statusCode)
        => handler.EnqueueResponse(new HttpResponseMessage(statusCode));

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    private static void EnqueueArrowRows(
        FakeHttpMessageHandler handler,
        params LanceDbMemoryProviderTestsFixture.LanceDbTestRow[] rows)
        => handler.EnqueueResponse(LanceDbMemoryProviderTestsFixture.ArrowRows(rows));

    private static async Task<JsonDocument> ReadJsonBodyAsync(HttpRequestMessage request)
        => JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestCt));

    [Fact]
    public async Task UpsertBatchAsync_CreatesTheCollectionTableLazily_AndMergeInsertsIntoIt()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueStatus(handler, HttpStatusCode.NotFound); // exists → absent
        EnqueueOk(handler);                              // create
        EnqueueOk(handler);                              // merge_insert
        using var provider = _fixture.CreateProvider(handler);

        var item = MemoryItem.Create(
            "chunk content",
            source: "guide.md",
            customProperties: new Dictionary<string, string> { ["rag.kind"] = "chunk" });

        await provider.UpsertBatchAsync(
            "docs",
            [new MemoryUpsertEntry("abcd1234abcd1234:0", item, UnitX4)],
            TestCt);

        Assert.Equal(3, handler.CapturedRequests.Count);
        Assert.Equal("/v1/table/docs/exists", handler.CapturedRequests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/table/docs/create", handler.CapturedRequests[1].RequestUri!.AbsolutePath);

        var mergeInsert = handler.CapturedRequests[2];
        Assert.Equal("/v1/table/docs/merge_insert", mergeInsert.RequestUri!.AbsolutePath);
        Assert.Contains("on=id", mergeInsert.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("application/vnd.apache.arrow.stream", mergeInsert.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task UpsertBatchAsync_ExistingTable_SkipsCreation_AndCachesTheCheck()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler); // exists → present
        EnqueueOk(handler); // merge_insert
        EnqueueOk(handler); // merge_insert (2nd call)
        using var provider = _fixture.CreateProvider(handler);

        var item = MemoryItem.Create("content");
        await provider.UpsertBatchAsync(
            "docs", [new MemoryUpsertEntry("k1", item, UnitX4)], TestCt);
        await provider.UpsertBatchAsync(
            "docs", [new MemoryUpsertEntry("k2", item, UnitY4)], TestCt);

        // One existence probe only — the second batch reuses the cached table state.
        Assert.Equal(3, handler.CapturedRequests.Count);
        Assert.Equal("/v1/table/docs/exists", handler.CapturedRequests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/table/docs/merge_insert", handler.CapturedRequests[1].RequestUri!.AbsolutePath);
        Assert.Equal("/v1/table/docs/merge_insert", handler.CapturedRequests[2].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SearchSimilarWithScoresAsync_QueriesTheCollectionTable_WithSqlPrefilter()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler); // exists
        EnqueueArrowRows(handler, new LanceDbMemoryProviderTestsFixture.LanceDbTestRow(
            "abcd1234abcd1234:0", "chunk content",
            Embedding: UnitX4, Source: "guide.md", Distance: 0.2f));
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            "docs",
            UnitX4,
            topK: 5,
            minScore: float.MinValue,
            new MemoryFilter
            {
                Source = "guide.md",
                CustomProperties = new Dictionary<string, string> { ["rag.kind"] = "chunk" }
            },
            TestCt);

        var query = handler.CapturedRequests[1];
        Assert.Equal("/v1/table/docs/query", query.RequestUri!.AbsolutePath);
        using (var body = await ReadJsonBodyAsync(query))
        {
            Assert.Equal(5, body.RootElement.GetProperty("k").GetInt32());
            var predicate = body.RootElement.GetProperty("filter").GetString();
            Assert.Contains("source = 'guide.md'", predicate, StringComparison.Ordinal);
            Assert.Contains("metadata_json LIKE '%\"rag.kind\":\"chunk\"%'", predicate, StringComparison.Ordinal);
            Assert.True(body.RootElement.GetProperty("prefilter").GetBoolean());
        }

        var single = Assert.Single(results);
        Assert.Equal("abcd1234abcd1234:0", single.Key);
        Assert.Equal(0.8f, single.Score, 0.001f);
        Assert.Equal("chunk content", single.Item.Content);
    }

    [Fact]
    public async Task SearchSimilarWithScoresAsync_MinScoreIsAppliedAsGiven()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler);
        EnqueueArrowRows(
            handler,
            new LanceDbMemoryProviderTestsFixture.LanceDbTestRow(
                "near", "near", Embedding: UnitX4, Distance: 0.1f),
            new LanceDbMemoryProviderTestsFixture.LanceDbTestRow(
                "far", "far", Embedding: UnitY4, Distance: 0.9f));
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            "docs", UnitX4, topK: 5, minScore: 0.5f, filter: null, TestCt);

        var single = Assert.Single(results);
        Assert.Equal("near", single.Key);
    }

    [Fact]
    public async Task DeleteByFilterAsync_PostsTheSqlPredicate_ToTheCollectionTable()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler); // exists
        EnqueueOk(handler); // delete
        using var provider = _fixture.CreateProvider(handler);

        await provider.DeleteByFilterAsync("docs", new MemoryFilter { Source = "drop.md" }, TestCt);

        var delete = handler.CapturedRequests[1];
        Assert.Equal("/v1/table/docs/delete", delete.RequestUri!.AbsolutePath);
        using var body = await ReadJsonBodyAsync(delete);
        Assert.Equal("source = 'drop.md'", body.RootElement.GetProperty("predicate").GetString());
    }

    [Fact]
    public async Task DeleteByFilterAsync_EmptyFilter_Throws()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.DeleteByFilterAsync("docs", new MemoryFilter(), TestCt));
    }

    [Fact]
    public async Task DropCollectionAsync_DropsTheCollectionTable()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DropCollectionAsync("docs", TestCt);

        var drop = Assert.Single(handler.CapturedRequests);
        Assert.Equal("/v1/table/docs/drop", drop.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DropCollectionAsync_AbsentTable_IsANoOp()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueStatus(handler, HttpStatusCode.NotFound);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DropCollectionAsync("docs", TestCt);

        Assert.Single(handler.CapturedRequests);
    }

    [Fact]
    public async Task UpsertBatchAsync_DimensionMismatch_Throws_NothingSent()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler); // exists
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.UpsertBatchAsync(
                "docs",
                [new MemoryUpsertEntry("k", MemoryItem.Create("x"), TwoDims)],
                TestCt));

        // Only the existence probe went out — no merge_insert was attempted.
        Assert.Single(handler.CapturedRequests);
    }
}
