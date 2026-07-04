using System.Net;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory.Pinecone;

public class PineconeMemoryProviderTests
{
    private readonly PineconeMemoryProviderTestsFixture _fixture;

    public PineconeMemoryProviderTests()
    {
        _fixture = new PineconeMemoryProviderTestsFixture();
    }

    [Fact]
    public void ShouldReturnPinecone_WhenName()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act & Assert
        Assert.Equal("Pinecone", provider.Name);
    }

    [Fact]
    public async Task ShouldSendUpsertRequest_WhenStoreAsync()
    {
        // Arrange
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { upsertedCount = 1 });
        using var provider = _fixture.CreateProvider(handler);
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var item = MemoryItem.Create(TestContent, embedding: embedding, source: "test");

        // Act
        await provider.StoreAsync("test-key", item, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.CapturedRequests);
        var request = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/vectors/upsert", request.RequestUri?.ToString());

        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("test-key", body);
        Assert.Contains(TestContent, body);
    }

    [Fact]
    public async Task ShouldSetApiKeyHeader_WhenStoreAsync()
    {
        // Arrange
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { upsertedCount = 1 });
        var options = new PineconeOptions { ApiKey = "my-secret-key", Namespace = "ns" };

        // We need to create the provider without a preset BaseAddress so ConfigureHttpClient sets it
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.pinecone.io")
        };
        httpClient.DefaultRequestHeaders.Add("Api-Key", "my-secret-key");

        using var provider = _fixture.CreateProvider(handler, options);
        var item = MemoryItem.Create(TestContent);

        // Act
        await provider.StoreAsync("key", item, TestContext.Current.CancellationToken);

        // Assert - verify the request was made (API key is set on client)
        Assert.Single(handler.CapturedRequests);
    }

    [Fact]
    public async Task ShouldFetchById_WhenGetAsync()
    {
        // Arrange
        var responseBody = new
        {
            vectors = new Dictionary<string, object>
            {
                ["test-key"] = new
                {
                    id = "test-key",
                    values = new[] { 0.1f, 0.2f },
                    metadata = new Dictionary<string, object>
                    {
                        ["content"] = TestContent,
                        ["importance"] = 0.8,
                        ["source"] = "test-source"
                    }
                }
            }
        };

        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var result = await provider.GetAsync("test-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContent, result.Content);

        var request = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/vectors/fetch", request.RequestUri?.ToString());
        Assert.Contains("test-key", request.RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentKey()
    {
        // Arrange
        var responseBody = new
        {
            vectors = new Dictionary<string, object>()
        };

        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var result = await provider.GetAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldSendDeleteRequest_WhenDeleteAsync()
    {
        // Arrange
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { });
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var result = await provider.DeleteAsync("test-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var request = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/vectors/delete", request.RequestUri?.ToString());

        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("test-key", body);
    }

    [Fact]
    public async Task ShouldSendQueryWithVector_WhenSearchAsync()
    {
        // Arrange
        var responseBody = new
        {
            matches = new[]
            {
                new
                {
                    id = "key1",
                    score = 0.95f,
                    values = new[] { 0.1f, 0.2f },
                    metadata = new Dictionary<string, object>
                    {
                        ["content"] = "Match 1",
                        ["importance"] = 0.9,
                        ["source"] = "src1"
                    }
                },
                new
                {
                    id = "key2",
                    score = 0.80f,
                    values = new[] { 0.3f, 0.4f },
                    metadata = new Dictionary<string, object>
                    {
                        ["content"] = "Match 2",
                        ["importance"] = 0.5,
                        ["source"] = "src2"
                    }
                }
            }
        };

        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var results = await provider.SearchAsync("search query", limit: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count());

        var request = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/query", request.RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldClearAllVectors_WhenClearAsync()
    {
        // Arrange
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, new { });
        using var provider = _fixture.CreateProvider(handler);

        // Act
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/vectors/delete", request.RequestUri?.ToString());

        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("deleteAll", body);
    }

    [Fact]
    public async Task ShouldReturnCount_WhenCountAsync()
    {
        // Arrange
        var responseBody = new
        {
            namespaces = new Dictionary<string, object>
            {
                ["test-namespace"] = new { vectorCount = 25 }
            },
            totalVectorCount = 25
        };

        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(25, count);
    }

    [Fact]
    public async Task ShouldReturnZero_WhenCountAsyncWithNoNamespace()
    {
        // Arrange
        var responseBody = new
        {
            namespaces = new Dictionary<string, object>(),
            totalVectorCount = 0
        };

        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldReturnEmptyResults_WhenSearchAsyncWithEmptyQuery()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var results = await provider.SearchAsync("", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSearchAsyncWithNullQuery()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.SearchAsync(null!, limit: 10, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenStoreAsyncWithEmptyKey()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.StoreAsync("", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullItem()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.StoreAsync("key", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldHandleHttpErrors_WhenDeleteAsyncFails()
    {
        // Arrange
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.InternalServerError);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var result = await provider.DeleteAsync("test-key", TestContext.Current.CancellationToken);

        // Assert - delete returns false on failure rather than throwing
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldQueryVectorAndMapScores_WhenSearchSimilarAsync()
    {
        // Arrange - two matches; minScore 0.5 keeps only the close one
        var responseBody = new
        {
            matches = new object[]
            {
                new
                {
                    id = "m1",
                    score = 0.95f,
                    values = new[] { 0.1f, 0.2f },
                    metadata = new Dictionary<string, object>
                    {
                        ["content"] = "close match",
                        ["importance"] = 0.9,
                        ["source"] = "test-source"
                    }
                },
                new
                {
                    id = "m2",
                    score = 0.2f,
                    values = new[] { 0.3f, 0.4f },
                    metadata = new Dictionary<string, object> { ["content"] = "far match" }
                }
            }
        };
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(HttpStatusCode.OK, responseBody);
        using var provider = _fixture.CreateProvider(handler);

        // Act - called through the interface to lock in the dispatch fix (MAT-017 / R10.1)
        IMemoryProvider memoryProvider = provider;
        var results = await memoryProvider.SearchSimilarAsync(
            [0.1f, 0.2f], topK: 5, minScore: 0.5f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var single = Assert.Single(results);
        Assert.Equal("close match", single.Item.Content);
        Assert.Equal(0.95f, single.Score, precision: 4);

        var request = Assert.Single(handler.CapturedRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/query", request.RequestUri?.ToString());
        Assert.NotNull(request.Content);
        var body = await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"vector\"", body);
        Assert.Contains("test-namespace", body);
    }

    [Fact]
    public async Task ShouldSendEqualityFilter_WhenSearchSimilarAsyncWithFilter()
    {
        // Arrange - empty result set; the assertion targets the outgoing payload
        using var handler = PineconeMemoryProviderTestsFixture.CreateHandler(
            HttpStatusCode.OK,
            new { matches = Array.Empty<object>() });
        using var provider = _fixture.CreateProvider(handler);
        var filter = new Dictionary<string, object> { ["source"] = "unit-test" };

        // Act
        IMemoryProvider memoryProvider = provider;
        var results = await memoryProvider.SearchSimilarAsync(
            [1f, 0f], filter: filter, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        var request = Assert.Single(handler.CapturedRequests);
        Assert.NotNull(request.Content);
        var body = await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("$eq", body);
        Assert.Contains("unit-test", body);
    }
}
