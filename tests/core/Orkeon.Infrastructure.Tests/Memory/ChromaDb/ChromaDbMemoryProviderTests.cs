using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory.ChromaDb;

public class ChromaDbMemoryProviderTests
{
    private const string V2CollectionsRoute =
        "/api/v2/tenants/default_tenant/databases/default_database/collections";

    private static readonly string[] QueryResultKeys = ["k1", "k2"];
    private static readonly string[] QueryResultDocuments = ["first doc", "second doc"];
    private static readonly float[] QueryResultDistances = [0.1f, 0.4f];

    private readonly ChromaDbMemoryProviderTestsFixture _fixture;

    public ChromaDbMemoryProviderTests()
    {
        _fixture = new ChromaDbMemoryProviderTestsFixture();
    }

    [Fact]
    public void ShouldReturnChromaDB_WhenName()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act & Assert
        Assert.Equal("ChromaDB", provider.Name);
    }

    [Fact]
    public async Task ShouldSendCorrectHttpRequest_WhenStoreAsync()
    {
        // Arrange
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new { });

        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(TestContent, source: "test");

        // Act
        await provider.StoreAsync("test-key", item, TestContext.Current.CancellationToken);

        // Assert - first request is collection creation, second is add
        Assert.Equal(2, handler.CapturedRequests.Count);
        var addRequest = handler.CapturedRequests[1];
        Assert.Equal(HttpMethod.Post, addRequest.Method);
        Assert.Contains($"{V2CollectionsRoute}/test-collection-id/add", addRequest.RequestUri?.ToString());

        var body = await addRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("test-key", body);
        Assert.Contains(TestContent, body);
    }

    [Fact]
    public async Task ShouldReturnItem_WhenGetAsyncWithExistingKey()
    {
        // Arrange
        var responseBody = new
        {
            ids = new[] { "test-key" },
            documents = new[] { TestContent },
            metadatas = new[]
            {
                new Dictionary<string, object>
                {
                    ["importance"] = 0.8,
                    ["source"] = "test-source"
                }
            },
            embeddings = (float[][]?)null
        };

        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            responseBody);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var result = await provider.GetAsync("test-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(TestContent, result.Content);
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentKey()
    {
        // Arrange
        var responseBody = new
        {
            ids = Array.Empty<string>(),
            documents = Array.Empty<string>(),
            metadatas = Array.Empty<Dictionary<string, object>>()
        };

        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            responseBody);

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
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new { });

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var result = await provider.DeleteAsync("test-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var deleteRequest = handler.CapturedRequests[1];
        Assert.Equal(HttpMethod.Post, deleteRequest.Method);
        Assert.Contains($"{V2CollectionsRoute}/test-collection-id/delete", deleteRequest.RequestUri?.ToString());

        var body = await deleteRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("test-key", body);
    }

    [Fact]
    public async Task ShouldSendQueryRequest_WhenSearchAsync()
    {
        // Arrange
        var responseBody = new
        {
            ids = new[] { new[] { "key1", "key2" } },
            documents = new[] { new[] { "Content 1", "Content 2" } },
            metadatas = new[]
            {
                new[]
                {
                    new Dictionary<string, object> { ["importance"] = 0.5, ["source"] = "s1" },
                    new Dictionary<string, object> { ["importance"] = 0.7, ["source"] = "s2" }
                }
            },
            distances = new[] { new[] { 0.1f, 0.3f } }
        };

        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            responseBody);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var results = await provider.SearchAsync("test query", limit: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count());

        var queryRequest = handler.CapturedRequests[1];
        Assert.Equal(HttpMethod.Post, queryRequest.Method);
        Assert.Contains($"{V2CollectionsRoute}/test-collection-id/query", queryRequest.RequestUri?.ToString());

        var body = await queryRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("test query", body);
    }

    [Fact]
    public async Task ShouldHandleHttpErrors_WhenStoreAsyncFails()
    {
        // Arrange
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.InternalServerError);

        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.StoreAsync("test-key", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnCount_WhenCountAsync()
    {
        // Arrange
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            42);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(42, count);
    }

    [Fact]
    public async Task ShouldReturnEmptyResults_WhenSearchAsyncWithEmptyQuery()
    {
        // Arrange - no handler responses needed since empty query returns early
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var results = await provider.SearchAsync("", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        // No HTTP requests should be made for empty query
        Assert.Empty(handler.CapturedRequests);
    }

    [Fact]
    public Task ShouldThrowArgumentNullException_WhenSearchAsyncWithNullQuery()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act & Assert
        return Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.SearchAsync(null!, limit: 10, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public Task ShouldThrowArgumentException_WhenStoreAsyncWithEmptyKey()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        return Assert.ThrowsAsync<ArgumentException>(
            () => provider.StoreAsync("", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullItem()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        // Act & Assert
        return Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.StoreAsync("key", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldListKeys_WhenListKeysAsync()
    {
        // Arrange
        var responseBody = new
        {
            ids = new[] { "key1", "key2", "key3" },
            documents = new[] { "doc1", "doc2", "doc3" }
        };

        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            responseBody);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var keys = await provider.ListKeysAsync(0, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public async Task ShouldClearCollection_WhenClearAsync()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();

        // 1. First, we need to trigger EnsureCollectionExistsAsync via some operation (e.g. CountAsync)
        // Collection creation response
        using var resp6 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { id = "test-collection-id", name = "test_collection" }),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        handler.EnqueueResponse(resp6);

        // CountAsync response
        using var resp5 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("5", System.Text.Encoding.UTF8, "application/json")
        };
        handler.EnqueueResponse(resp5);

        // 2. Now ClearAsync will delete + recreate
        // Delete collection response
        using var resp4 = new HttpResponseMessage(HttpStatusCode.OK);
        handler.EnqueueResponse(resp4);

        // Recreate collection response
        using var resp3 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { id = "new-collection-id", name = "test_collection" }),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        handler.EnqueueResponse(resp3);

        using var provider = _fixture.CreateProvider(handler);

        // Initialize the collection by calling CountAsync
        await provider.CountAsync(TestContext.Current.CancellationToken);

        // Act
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert - 4 requests: create collection, count, delete collection, recreate collection
        Assert.Equal(4, handler.CapturedRequests.Count);

        // The delete request should be a DELETE to the collection (v2 tenant/database route)
        var deleteRequest = handler.CapturedRequests[2];
        Assert.Equal(HttpMethod.Delete, deleteRequest.Method);
        Assert.Contains($"{V2CollectionsRoute}/test_collection", deleteRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldCreateCollectionOnV2TenantDatabaseRoute_WhenFirstOperation()
    {
        // Arrange
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            42);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        await provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert - the collection bootstrap request targets the v2 tenants/databases route
        var createRequest = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, createRequest.Method);
        Assert.Equal(
            $"http://localhost:8000{V2CollectionsRoute}",
            createRequest.RequestUri?.ToString());

        // And the count request hangs off the same v2 route
        var countRequest = handler.CapturedRequests[1];
        Assert.Equal(HttpMethod.Get, countRequest.Method);
        Assert.Contains($"{V2CollectionsRoute}/test-collection-id/count", countRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldUseConfiguredTenantAndDatabase_WhenStoreAsync()
    {
        // Arrange
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new { });

        var options = new ChromaDbOptions
        {
            BaseUrl = new Uri("http://localhost:8000"),
            CollectionName = "test_collection",
            Tenant = "acme_tenant",
            Database = "acme_db"
        };

        using var provider = _fixture.CreateProvider(handler, options);
        var item = MemoryItem.Create(TestContent, source: "test");

        // Act
        await provider.StoreAsync("test-key", item, TestContext.Current.CancellationToken);

        // Assert - both requests target the configured tenant/database
        const string expectedRoute = "/api/v2/tenants/acme_tenant/databases/acme_db/collections";
        Assert.Contains(expectedRoute, handler.CapturedRequests[0].RequestUri?.ToString());
        Assert.Contains($"{expectedRoute}/test-collection-id/add", handler.CapturedRequests[1].RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldNotTargetLegacyV1Routes_WhenPerformingOperations()
    {
        // Arrange
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new { });

        using var provider = _fixture.CreateProvider(handler);
        var item = MemoryItem.Create(TestContent, source: "test");

        // Act
        await provider.StoreAsync("test-key", item, TestContext.Current.CancellationToken);

        // Assert - no request targets the removed /api/v1 surface
        Assert.All(
            handler.CapturedRequests,
            request => Assert.DoesNotContain("/api/v1/", request.RequestUri?.ToString()));
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenHeartbeatAsyncSucceeds()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var resp2 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"nanosecond heartbeat\": 1234567890}",
                System.Text.Encoding.UTF8,
                "application/json")
        };
        handler.EnqueueResponse(resp2);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var healthy = await provider.HeartbeatAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(healthy);
        var heartbeatRequest = Assert.Single(handler.CapturedRequests);
        Assert.Equal(HttpMethod.Get, heartbeatRequest.Method);
        Assert.Equal("http://localhost:8000/api/v2/heartbeat", heartbeatRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenHeartbeatAsyncGetsServerError()
    {
        // Arrange
        using var handler = new FakeHttpMessageHandler();
        using var resp1 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        handler.EnqueueResponse(resp1);

        using var provider = _fixture.CreateProvider(handler);

        // Act
        var healthy = await provider.HeartbeatAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(healthy);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenHeartbeatAsyncServerIsUnreachable()
    {
        // Arrange
        using var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Connection refused"));
        using var provider = _fixture.CreateProvider(handler);

        // Act
        var healthy = await provider.HeartbeatAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(healthy);
    }

    [Fact]
    public async Task ShouldQueryWithEmbeddingsAndScoreFromDistances_WhenSearchSimilarAsync()
    {
        // Arrange - distances 0.1 / 0.4 are converted to scores 0.9 / 0.6 (1 - distance)
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new
            {
                ids = new[] { QueryResultKeys },
                documents = new[] { QueryResultDocuments },
                metadatas = new[] { new object?[] { new { importance = 0.8f, source = "test" }, null } },
                distances = new[] { QueryResultDistances }
            });
        using var provider = _fixture.CreateProvider(handler);

        // Act - called through the interface to lock in the dispatch fix (MAT-017 / R10.1)
        IMemoryProvider memoryProvider = provider;
        var results = await memoryProvider.SearchSimilarAsync(
            [0.1f, 0.2f, 0.3f], topK: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - scored and ordered best first
        Assert.Equal(2, results.Count);
        Assert.Equal("first doc", results[0].Item.Content);
        Assert.Equal(0.9f, results[0].Score, precision: 4);
        Assert.Equal("second doc", results[1].Item.Content);
        Assert.Equal(0.6f, results[1].Score, precision: 4);

        // The vector query is executed server-side with query_embeddings
        var queryRequest = handler.CapturedRequests[^1];
        Assert.Equal(HttpMethod.Post, queryRequest.Method);
        Assert.Contains("/query", queryRequest.RequestUri?.ToString());
        Assert.NotNull(queryRequest.Content);
        var body = await queryRequest.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("query_embeddings", body);
        Assert.Contains("distances", body);
    }

    [Fact]
    public async Task ShouldFilterByMinScore_WhenSearchSimilarAsync()
    {
        // Arrange - scores 0.9 / 0.6; minScore 0.7 keeps only the first item
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new
            {
                ids = new[] { QueryResultKeys },
                documents = new[] { QueryResultDocuments },
                metadatas = new[] { new object?[] { null, null } },
                distances = new[] { QueryResultDistances }
            });
        using var provider = _fixture.CreateProvider(handler);

        // Act
        IMemoryProvider memoryProvider = provider;
        var results = await memoryProvider.SearchSimilarAsync(
            [0.1f, 0.2f, 0.3f], topK: 5, minScore: 0.7f, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var single = Assert.Single(results);
        Assert.Equal("first doc", single.Item.Content);
        Assert.Equal(0.9f, single.Score, precision: 4);
    }

    [Fact]
    public async Task ShouldSendWhereClause_WhenSearchSimilarAsyncWithFilter()
    {
        // Arrange - empty result set; the assertion targets the outgoing payload
        using var handler = ChromaDbMemoryProviderTestsFixture.CreateHandlerWithCollection(
            HttpStatusCode.OK,
            new { ids = new[] { Array.Empty<string>() } });
        using var provider = _fixture.CreateProvider(handler);
        var filter = new Dictionary<string, object> { ["source"] = "unit-test" };

        // Act
        IMemoryProvider memoryProvider = provider;
        var results = await memoryProvider.SearchSimilarAsync(
            [1f, 0f], filter: filter, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        var queryRequest = handler.CapturedRequests[^1];
        Assert.NotNull(queryRequest.Content);
        var body = await queryRequest.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("where", body);
        Assert.Contains("$eq", body);
        Assert.Contains("unit-test", body);
    }
}
