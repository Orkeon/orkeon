using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;

namespace Orkeon.Infrastructure.Tests.Memory.Pinecone;

/// <summary>
/// HTTP-level tests for the non-scoped <see cref="IScoredVectorSearch"/> capability of
/// <see cref="PineconeMemoryProvider"/> (RAG-04/C2, reliquat RAG-02): the query targets
/// the configured <b>default</b> namespace, native match scores and storage keys survive
/// end to end, the typed filter compiles to a metadata filter, and <c>minScore</c> is
/// applied as given.
/// </summary>
public class PineconeMemoryProviderScoredSearchTests
{
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly float[] QueryVector = [1f, 0f];

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

    private static object TwoMatches() => new
    {
        matches = new[]
        {
            new
            {
                id = "k-strong",
                score = 0.92f,
                values = new[] { 1f, 0f },
                metadata = new Dictionary<string, object>
                {
                    ["content"] = "strong content",
                    ["source"] = "guide.md",
                    ["rag.kind"] = "chunk",
                }
            },
            new
            {
                id = "k-weak",
                score = 0.41f,
                values = new[] { 0f, 1f },
                metadata = new Dictionary<string, object> { ["content"] = "weak content" }
            },
        }
    };

    [Fact]
    public void Capability_IsDiscoverable_ThroughTryGetCapability()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        Assert.True(((IMemoryProvider)provider).TryGetCapability<IScoredVectorSearch>(out _));
    }

    [Fact]
    public async Task SearchSimilarWithScores_TargetsTheDefaultNamespace_WithScoresAndKeys()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueJson(handler, TwoMatches());
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            QueryVector, topK: 5, minScore: float.MinValue, cancellationToken: TestCt);

        var query = Assert.Single(handler.CapturedRequests);
        Assert.Equal("/query", query.RequestUri!.AbsolutePath);
        var body = JsonDocument.Parse(await query.Content!.ReadAsStringAsync(TestCt));
        using (body)
        {
            // Namespace = the configured default, never a collection.
            Assert.Equal("test-namespace", body.RootElement.GetProperty("namespace").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("topK").GetInt32());
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("k-strong", results[0].Key);
        Assert.Equal(0.92f, results[0].Score, 0.001f);
        Assert.Equal("strong content", results[0].Item.Content);
        Assert.Equal("guide.md", results[0].Item.Source);
        Assert.Equal("chunk", results[0].Item.Metadata.CustomProperties!["rag.kind"]);
        Assert.Equal("k-weak", results[1].Key);
    }

    [Fact]
    public async Task SearchSimilarWithScores_CompilesTheTypedFilter()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueJson(handler, new { matches = Array.Empty<object>() });
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchSimilarWithScoresAsync(
            QueryVector,
            topK: 3,
            minScore: 0f,
            new MemoryFilter { Source = "guide.md" },
            TestCt);

        var body = JsonDocument.Parse(
            await handler.CapturedRequests[0].Content!.ReadAsStringAsync(TestCt));
        using (body)
        {
            Assert.Equal("guide.md",
                body.RootElement.GetProperty("filter").GetProperty("source").GetProperty("$eq").GetString());
        }
    }

    [Fact]
    public async Task SearchSimilarWithScores_AppliesMinScoreAsGiven()
    {
        using var handler = new FakeHttpMessageHandler();
        EnqueueJson(handler, TwoMatches());
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarWithScoresAsync(
            QueryVector, topK: 5, minScore: 0.5f, cancellationToken: TestCt);

        var single = Assert.Single(results);
        Assert.Equal("k-strong", single.Key);
    }

    [Fact]
    public async Task SearchSimilarWithScores_NonPositiveTopK_Throws()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            provider.SearchSimilarWithScoresAsync(QueryVector, topK: -1, minScore: 0f, cancellationToken: TestCt));
        Assert.Empty(handler.CapturedRequests);
    }
}
