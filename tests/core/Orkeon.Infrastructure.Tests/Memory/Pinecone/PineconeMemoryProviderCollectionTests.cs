using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.Pinecone;

/// <summary>
/// HTTP-level tests for the <see cref="ICollectionAwareMemory"/> implementation of
/// <see cref="PineconeMemoryProvider"/> (RAG-03/C2): a logical collection maps to a
/// Pinecone namespace, every request carries that namespace (never the configured
/// default), and metadata round-trips custom properties.
/// </summary>
public class PineconeMemoryProviderCollectionTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] QueryVector = [1f, 0f];
    private static readonly string[] ImportantTag = ["important"];

    private readonly PineconeMemoryProviderTestsFixture _fixture = new();

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
    private static void EnqueueOk(FakeHttpMessageHandler handler)
        => handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));

    private static async Task<JsonDocument> ReadBodyAsync(HttpRequestMessage request)
        => JsonDocument.Parse(await request.Content!.ReadAsStringAsync(TestCt));

    [Fact]
    public async Task UpsertBatchAsync_TargetsTheCollectionNamespace_WithCustomProperties()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler);
        using var provider = _fixture.CreateProvider(handler);

        var item = MemoryItem.Create(
            "chunk content",
            source: "guide.md",
            customProperties: new Dictionary<string, string> { ["rag.kind"] = "chunk", ["meta.lang"] = "fr" });

        await provider.UpsertBatchAsync(
            "docs",
            [new MemoryUpsertEntry("abcd1234abcd1234:0", item, QueryVector)],
            TestCt);

        var upsert = Assert.Single(handler.CapturedRequests);
        Assert.Equal("/vectors/upsert", upsert.RequestUri!.AbsolutePath);
        using var body = await ReadBodyAsync(upsert);

        // Namespace = the RAG collection, not the configured "test-namespace".
        Assert.Equal("docs", body.RootElement.GetProperty("namespace").GetString());

        var vector = body.RootElement.GetProperty("vectors")[0];
        Assert.Equal("abcd1234abcd1234:0", vector.GetProperty("id").GetString());
        Assert.Equal(2, vector.GetProperty("values").GetArrayLength());
        var metadata = vector.GetProperty("metadata");
        Assert.Equal("chunk content", metadata.GetProperty("content").GetString());
        Assert.Equal("guide.md", metadata.GetProperty("source").GetString());
        Assert.Equal("chunk", metadata.GetProperty("rag.kind").GetString());
        Assert.Equal("fr", metadata.GetProperty("meta.lang").GetString());
    }

    [Fact]
    public async Task SearchSimilarWithScoresAsync_QueriesTheNamespace_AndRestoresCustomProperties()
    {
        using var handler = new FakeHttpMessageHandler();
        var responseBody = new
        {
            matches = new[]
            {
                new
                {
                    id = "abcd1234abcd1234:0",
                    score = 0.92f,
                    values = new[] { 1f, 0f },
                    metadata = new Dictionary<string, object>
                    {
                        ["content"] = "chunk content",
                        ["importance"] = 0.5f,
                        ["source"] = "guide.md",
                        ["timestamp"] = "2026-01-01T00:00:00Z",
                        ["rag.kind"] = "chunk",
                        ["meta.lang"] = "fr",
                    }
                }
            }
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

        var query = Assert.Single(handler.CapturedRequests);
        Assert.Equal("/query", query.RequestUri!.AbsolutePath);
        using (var body = await ReadBodyAsync(query))
        {
            Assert.Equal("docs", body.RootElement.GetProperty("namespace").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("topK").GetInt32());
            Assert.Equal("chunk",
                body.RootElement.GetProperty("filter").GetProperty("rag.kind").GetProperty("$eq").GetString());
        }

        var single = Assert.Single(results);
        Assert.Equal("abcd1234abcd1234:0", single.Key);
        Assert.Equal(0.92f, single.Score, 0.001f);
        Assert.Equal("chunk content", single.Item.Content);
        Assert.Equal("chunk", single.Item.Metadata.CustomProperties!["rag.kind"]);
        Assert.Equal("fr", single.Item.Metadata.CustomProperties["meta.lang"]);
    }

    [Fact]
    public async Task DeleteByFilterAsync_PostsTheFilter_ToTheNamespace()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DeleteByFilterAsync("docs", new MemoryFilter { Source = "drop.md" }, TestCt);

        var delete = Assert.Single(handler.CapturedRequests);
        Assert.Equal("/vectors/delete", delete.RequestUri!.AbsolutePath);
        using var body = await ReadBodyAsync(delete);
        Assert.Equal("docs", body.RootElement.GetProperty("namespace").GetString());
        Assert.Equal("drop.md",
            body.RootElement.GetProperty("filter").GetProperty("source").GetProperty("$eq").GetString());
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
    public async Task DropCollectionAsync_DeletesAllVectorsOfTheNamespace()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueOk(handler);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DropCollectionAsync("docs", TestCt);

        var drop = Assert.Single(handler.CapturedRequests);
        Assert.Equal("/vectors/delete", drop.RequestUri!.AbsolutePath);
        using var body = await ReadBodyAsync(drop);
        Assert.True(body.RootElement.GetProperty("deleteAll").GetBoolean());
        Assert.Equal("docs", body.RootElement.GetProperty("namespace").GetString());
    }

    [Fact]
    public async Task ScopedOperations_TagFilter_IsRejectedNotIgnored()
    {
        using var handler = new FakeHttpMessageHandler();
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
}
