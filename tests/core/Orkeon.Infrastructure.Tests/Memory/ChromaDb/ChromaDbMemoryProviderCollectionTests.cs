using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.ChromaDb;

/// <summary>
/// HTTP-level tests for the <see cref="ICollectionAwareMemory"/> implementation of
/// <see cref="ChromaDbMemoryProvider"/> (RAG-03/C2): a logical collection maps to a
/// dedicated ChromaDB collection (lazy <c>get_or_create</c>), upserts/queries/deletes
/// target that collection's routes, and metadata round-trips custom properties.
/// </summary>
public class ChromaDbMemoryProviderCollectionTests
{
    private const string CollectionsRoute = "/api/v2/tenants/default_tenant/databases/default_database/collections";

    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] QueryVector = [1f, 0f];
    private static readonly float[] UnitVector = [1f];
    private static readonly string[] ImportantTag = ["important"];

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

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    private static void EnqueueStatus(FakeHttpMessageHandler handler, HttpStatusCode statusCode)
        => handler.EnqueueResponse(new HttpResponseMessage(statusCode));

    /// <summary>Enqueues the lazy get_or_create response of the "docs" RAG collection.</summary>
    private static void EnqueueRagCollection(FakeHttpMessageHandler handler)
        => EnqueueJson(handler, new { id = "rag-col-id", name = "docs" });

    private static async Task<JsonDocument> ReadBodyAsync(HttpRequestMessage request)
        => JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestCt));

    [Fact]
    public async Task UpsertBatchAsync_CreatesTheCollectionLazily_AndTargetsItsUpsertRoute()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueRagCollection(handler);
        EnqueueStatus(handler, HttpStatusCode.OK);
        using var provider = _fixture.CreateProvider(handler);

        var item = MemoryItem.Create(
            "chunk content",
            source: "guide.md",
            customProperties: new Dictionary<string, string> { ["rag.kind"] = "chunk", ["meta.lang"] = "fr" });

        await provider.UpsertBatchAsync(
            "docs",
            [new MemoryUpsertEntry("abcd1234abcd1234:0", item, QueryVector)],
            TestCt);

        Assert.Equal(2, handler.CapturedRequests.Count);

        // 1. Lazy get_or_create of the RAG collection (not the configured default one).
        var create = handler.CapturedRequests[0];
        Assert.Equal(CollectionsRoute, create.RequestUri!.AbsolutePath);
        using (var createBody = await ReadBodyAsync(create))
        {
            Assert.Equal("docs", createBody.RootElement.GetProperty("name").GetString());
            Assert.True(createBody.RootElement.GetProperty("get_or_create").GetBoolean());
        }

        // 2. Upsert on the resolved collection id, carrying the unprefixed key and the
        //    custom properties flattened into the metadata document.
        var upsert = handler.CapturedRequests[1];
        Assert.Equal($"{CollectionsRoute}/rag-col-id/upsert", upsert.RequestUri!.AbsolutePath);
        using var upsertBody = await ReadBodyAsync(upsert);
        Assert.Equal("abcd1234abcd1234:0", upsertBody.RootElement.GetProperty("ids")[0].GetString());
        Assert.Equal("chunk content", upsertBody.RootElement.GetProperty("documents")[0].GetString());
        var metadata = upsertBody.RootElement.GetProperty("metadatas")[0];
        Assert.Equal("chunk", metadata.GetProperty("rag.kind").GetString());
        Assert.Equal("fr", metadata.GetProperty("meta.lang").GetString());
        Assert.Equal("guide.md", metadata.GetProperty("source").GetString());
        Assert.Equal(2, upsertBody.RootElement.GetProperty("embeddings")[0].GetArrayLength());
    }

    [Fact]
    public async Task SearchSimilarWithScoresAsync_QueriesTheCollection_AndRestoresCustomProperties()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueRagCollection(handler);
        var responseBody = new
        {
            ids = new[] { new[] { "abcd1234abcd1234:0" } },
            documents = new[] { new[] { "chunk content" } },
            metadatas = new[] { new object[]
            {
                new Dictionary<string, object>
                {
                    ["importance"] = 0.5f,
                    ["source"] = "guide.md",
                    ["timestamp"] = "2026-01-01T00:00:00Z",
                    ["rag.kind"] = "chunk",
                    ["meta.lang"] = "fr",
                }
            } },
            distances = new[] { new[] { 0.25f } },
        };
        EnqueueJson(handler, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            "docs",
            QueryVector,
            topK: 5,
            minScore: float.MinValue,
            new MemoryFilter
            {
                CustomProperties = new Dictionary<string, string> { ["rag.kind"] = "chunk" }
            },
            TestCt);

        var query = handler.CapturedRequests[1];
        Assert.Equal($"{CollectionsRoute}/rag-col-id/query", query.RequestUri!.AbsolutePath);
        using (var queryBody = await ReadBodyAsync(query))
        {
            Assert.Equal(5, queryBody.RootElement.GetProperty("n_results").GetInt32());
            var where = queryBody.RootElement.GetProperty("where");
            Assert.Equal("chunk", where.GetProperty("rag.kind").GetProperty("$eq").GetString());
        }

        var single = Assert.Single(results);
        Assert.Equal("abcd1234abcd1234:0", single.Key);
        Assert.Equal(0.75f, single.Score, 0.001f);
        Assert.Equal("chunk content", single.Item.Content);
        Assert.Equal("guide.md", single.Item.Source);
        Assert.Equal("chunk", single.Item.Metadata.CustomProperties!["rag.kind"]);
        Assert.Equal("fr", single.Item.Metadata.CustomProperties["meta.lang"]);
    }

    [Fact]
    public async Task SearchSimilarWithScoresAsync_MultipleCriteria_CompileToAnd()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueRagCollection(handler);
        var emptyResponse = new { ids = Array.Empty<string[]>() };
        EnqueueJson(handler, emptyResponse);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchSimilarWithScoresAsync(
            "docs", QueryVector, topK: 3, minScore: 0f,
            new MemoryFilter
            {
                Source = "guide.md",
                CustomProperties = new Dictionary<string, string> { ["rag.kind"] = "chunk" }
            },
            TestCt);

        using var body = await ReadBodyAsync(handler.CapturedRequests[1]);
        var and = body.RootElement.GetProperty("where").GetProperty("$and");
        Assert.Equal(2, and.GetArrayLength());
    }

    [Fact]
    public async Task DeleteByFilterAsync_PostsTheWhereClause_ToTheCollectionDeleteRoute()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueRagCollection(handler);
        EnqueueStatus(handler, HttpStatusCode.OK);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DeleteByFilterAsync("docs", new MemoryFilter { Source = "drop.md" }, TestCt);

        var delete = handler.CapturedRequests[1];
        Assert.Equal($"{CollectionsRoute}/rag-col-id/delete", delete.RequestUri!.AbsolutePath);
        using var body = await ReadBodyAsync(delete);
        Assert.Equal("drop.md",
            body.RootElement.GetProperty("where").GetProperty("source").GetProperty("$eq").GetString());
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
    public async Task DropCollectionAsync_DeletesTheCollectionByName()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueStatus(handler, HttpStatusCode.OK);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DropCollectionAsync("docs", TestCt);

        var drop = Assert.Single(handler.CapturedRequests);
        Assert.Equal(HttpMethod.Delete, drop.Method);
        Assert.Equal($"{CollectionsRoute}/docs", drop.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DropCollectionAsync_AbsentCollection_IsANoOp()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueStatus(handler, HttpStatusCode.NotFound);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DropCollectionAsync("docs", TestCt);

        Assert.Single(handler.CapturedRequests);
    }

    [Fact]
    public async Task ScopedOperations_TagFilter_IsRejectedNotIgnored()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueRagCollection(handler);
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.SearchSimilarWithScoresAsync(
                "docs", QueryVector, topK: 3, minScore: 0f,
                new MemoryFilter { Tags = ImportantTag }, TestCt));
    }

    [Fact]
    public async Task UpsertBatchAsync_EntryWithoutEmbedding_Throws_NothingSent()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.UpsertBatchAsync(
                "docs", [new MemoryUpsertEntry("k", MemoryItem.Create("no embedding"))], TestCt));

        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task ScopedCollections_AreCachedPerName()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueJson(handler, new { id = "alpha-id", name = "alpha" });
        EnqueueStatus(handler, HttpStatusCode.OK);
        EnqueueJson(handler, new { id = "beta-id", name = "beta" });
        EnqueueStatus(handler, HttpStatusCode.OK);
        EnqueueStatus(handler, HttpStatusCode.OK);
        using var provider = _fixture.CreateProvider(handler);

        var item = MemoryItem.Create("content");
        await provider.StoreWithEmbeddingAsync("alpha", "k1", item, UnitVector, TestCt);
        await provider.StoreWithEmbeddingAsync("beta", "k1", item, UnitVector, TestCt);
        await provider.StoreWithEmbeddingAsync("alpha", "k2", item, UnitVector, TestCt);

        // Two get_or_create calls only — the third write reuses the cached "alpha" id.
        var createCalls = handler.CapturedRequests
            .Where(r => r.RequestUri!.AbsolutePath == CollectionsRoute)
            .ToList();
        Assert.Equal(2, createCalls.Count);
        Assert.Equal($"{CollectionsRoute}/alpha-id/upsert", handler.CapturedRequests[1].RequestUri!.AbsolutePath);
        Assert.Equal($"{CollectionsRoute}/beta-id/upsert", handler.CapturedRequests[3].RequestUri!.AbsolutePath);
        Assert.Equal($"{CollectionsRoute}/alpha-id/upsert", handler.CapturedRequests[4].RequestUri!.AbsolutePath);
    }
}
