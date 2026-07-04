using System.Text.Json;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using Fixture = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture;
using TestRow = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture.LanceDbTestRow;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// Verifies the search facade of <c>LanceDbMemoryProvider</c>: full-text and vector
/// queries are delegated to the server (request payload assertions) and the
/// server-provided rankings (<c>_distance</c>, <c>_score</c>) drive the results.
/// </summary>
public class LanceDbMemoryProviderSearchTests
{
    private const string QueryPath = "/v1/table/test_memories/query";

    private readonly Fixture _fixture = new();

    // --- Full-text search ---

    [Fact]
    public async Task ShouldPostFullTextQuery_WhenSearchAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp14 = Fixture.Ok();
        handler.EnqueueResponse(resp14);
        using var resp13 = Fixture.ArrowRows(
            new TestRow("k1", "Machine learning is great", Score: 3.2f),
            new TestRow("k2", "Deep learning for NLP", Score: 2.1f));
        handler.EnqueueResponse(resp13);
        using var provider = _fixture.CreateProvider(handler);

        var results = (await provider.SearchAsync("learning", limit: 5, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Machine learning is great", results[0].Content);

        var query = handler.CapturedRequests[1];
        Assert.Equal(QueryPath, query.RequestUri!.AbsolutePath);
        var body = await query.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"vector\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"k\":5", body, StringComparison.Ordinal);
        Assert.Contains("\"full_text_query\":{\"string_query\":{\"query\":\"learning\",\"columns\":[\"content\"]}}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldReturnEmptyWithoutRequest_WhenSearchAsyncWithEmptyQuery()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchAsync("", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(results);
        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSearchAsyncWithNullQuery()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SearchAsync(null!, limit: 10, cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- Vector search ---

    [Fact]
    public async Task ShouldPostVectorQueryAndConvertDistances_WhenSearchSimilarAsync()
    {
        // Arrange — the server ranks by distance; the provider converts to 1 - distance.
        using var handler = new FakeHttpMessageHandler();
        using var resp12 = Fixture.Ok();
        handler.EnqueueResponse(resp12);
        using var resp11 = Fixture.ArrowRows(
            new TestRow("s1", "Similar item 1", Distance: 0.05f),
            new TestRow("s2", "Similar item 2", Distance: 0.3f));
        handler.EnqueueResponse(resp11);
        using var provider = _fixture.CreateProvider(handler);

        var queryEmbedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };

        // Act
        var results = await provider.SearchSimilarAsync(queryEmbedding, topK: 2, minScore: 0.5f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — scores come from the server's _distance column
        Assert.Equal(2, results.Count);
        Assert.Equal("Similar item 1", results[0].Item.Content);
        Assert.Equal(0.95f, results[0].Score, precision: 5);
        Assert.Equal(0.7f, results[1].Score, precision: 5);
        Assert.True(results[0].Score > results[1].Score);

        var body = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"vector\":{\"single_vector\":[1,0,0,0]}", body, StringComparison.Ordinal);
        Assert.Contains("\"k\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"distance_type\":\"cosine\"", body, StringComparison.Ordinal);
        Assert.Contains("\"vector_column\":\"vector\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFilterByMinScore_WhenSearchSimilarAsync()
    {
        // Arrange — distance 0.6 → similarity 0.4, below the 0.9 threshold
        using var handler = new FakeHttpMessageHandler();
        using var resp10 = Fixture.Ok();
        handler.EnqueueResponse(resp10);
        using var resp9 = Fixture.ArrowRows(
            new TestRow("close", "Close match", Distance: 0.02f),
            new TestRow("far", "Far match", Distance: 0.6f));
        handler.EnqueueResponse(resp9);
        using var provider = _fixture.CreateProvider(handler);

        var results = await provider.SearchSimilarAsync([1.0f, 0.0f, 0.0f, 0.0f], topK: 10, minScore: 0.9f, cancellationToken: TestContext.Current.CancellationToken);

        var single = Assert.Single(results);
        Assert.Equal("Close match", single.Item.Content);
    }

    [Fact]
    public async Task ShouldTranslateMetadataFilterToSqlPredicate_WhenSearchSimilarAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp8 = Fixture.Ok();
        handler.EnqueueResponse(resp8);
        using var resp7 = Fixture.ArrowRows();
        handler.EnqueueResponse(resp7);
        using var provider = _fixture.CreateProvider(handler);

        await provider.SearchSimilarAsync(
            [1.0f, 0.0f, 0.0f, 0.0f],
            topK: 5,
            filter: new Dictionary<string, object> { ["source"] = "research" }, cancellationToken: TestContext.Current.CancellationToken);

        var body = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // System.Text.Json escapes apostrophes (') in the raw payload — assert the decoded values.
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("source = 'research'", doc.RootElement.GetProperty("filter").GetString());
        Assert.True(doc.RootElement.GetProperty("prefilter").GetBoolean());
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSearchSimilarAsyncWithNullEmbedding()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SearchSimilarAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    // --- Hybrid search ---

    [Fact]
    public async Task ShouldFuseServerRankedLists_WhenHybridSearchAsync()
    {
        // Arrange — vector list ranks A then B; full-text list ranks A then C.
        using var handler = new FakeHttpMessageHandler();
        using var resp6 = Fixture.Ok();
        handler.EnqueueResponse(resp6);
        using var resp5 = Fixture.ArrowRows(
            new TestRow("A", "Machine learning algorithms", Distance: 0.1f),
            new TestRow("B", "Vector only item", Distance: 0.5f));
        handler.EnqueueResponse(resp5);
        using var resp4 = Fixture.ArrowRows(
            new TestRow("A", "Machine learning algorithms", Score: 4.0f),
            new TestRow("C", "Text only learning item", Score: 2.0f));
        handler.EnqueueResponse(resp4);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var results = await provider.HybridSearchAsync([0.9f, 0.1f, 0.0f, 0.0f], "learning", topK: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — A appears in both server-ranked lists, so it must rank first.
        // Default weights 0.7/0.3: A = 0.7*0.9 + 0.3*1.0 = 0.93 ; B = 0.35 ; C = 0.3.
        Assert.Equal(3, results.Count);
        Assert.Equal("Machine learning algorithms", results[0].Item.Content);
        Assert.Equal(0.93f, results[0].Score, precision: 4);

        // Both candidate queries were executed server-side.
        var vectorBody = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"single_vector\"", vectorBody, StringComparison.Ordinal);
        var textBody = await handler.CapturedRequests[2].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"full_text_query\"", textBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldApplyMinScore_WhenHybridSearchAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp3 = Fixture.Ok();
        handler.EnqueueResponse(resp3);
        using var resp2 = Fixture.ArrowRows(
            new TestRow("A", "Strong match", Distance: 0.1f),
            new TestRow("B", "Weak match", Distance: 0.9f));
        handler.EnqueueResponse(resp2);
        using var resp1 = Fixture.ArrowRows();
        handler.EnqueueResponse(resp1);
        using var provider = _fixture.CreateProvider(handler);

        // Composite for B = 0.7 * 0.1 = 0.07 < 0.5 → filtered out.
        var results = await provider.HybridSearchAsync([1.0f, 0.0f, 0.0f, 0.0f], "match", topK: 10, minScore: 0.5f, cancellationToken: TestContext.Current.CancellationToken);

        var single = Assert.Single(results);
        Assert.Equal("Strong match", single.Item.Content);
    }
}
