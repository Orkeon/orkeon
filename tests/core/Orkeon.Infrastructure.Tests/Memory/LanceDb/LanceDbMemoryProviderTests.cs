using System.Net;
using System.Text.Json;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using Fixture = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture;
using TestRow = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture.LanceDbTestRow;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// Verifies that <see cref="LanceDbMemoryProvider"/> drives the remote LanceDB
/// Cloud/Enterprise REST protocol: request paths, query strings, headers and
/// payloads (JSON + Arrow IPC) — no local store, no local scoring.
/// </summary>
public class LanceDbMemoryProviderTests
{
    private const string TablePath = "/v1/table/test_memories";

    private readonly Fixture _fixture = new();

    [Fact]
    public void ShouldReturnLanceDB_WhenName()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        Assert.Equal("LanceDB", provider.Name);
    }

    // --- Store ---

    [Fact]
    public async Task ShouldPostArrowMergeInsert_WhenStoreAsync()
    {
        // Arrange — table exists, merge_insert succeeds
        using var handler = new FakeHttpMessageHandler();
        using var resp45 = Fixture.Ok();
        handler.EnqueueResponse(resp45);
        using var resp44 = Fixture.Ok();
        handler.EnqueueResponse(resp44);
        using var provider = _fixture.CreateProvider(handler);

        var embedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var item = MemoryItem.Create("Test content about AI agents", embedding, 0.8f, "test");

        // Act
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        // Assert — requests
        Assert.Equal(2, handler.CapturedRequests.Count);

        var exists = handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, exists.Method);
        Assert.Equal($"{TablePath}/exists", exists.RequestUri!.AbsolutePath);

        var merge = handler.CapturedRequests[1];
        Assert.Equal(HttpMethod.Post, merge.Method);
        Assert.Equal($"{TablePath}/merge_insert", merge.RequestUri!.AbsolutePath);
        Assert.Contains("on=id", merge.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("when_matched_update_all=true", merge.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("when_not_matched_insert_all=true", merge.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal("application/vnd.apache.arrow.stream", merge.Content!.Headers.ContentType!.MediaType);

        // Assert — the Arrow payload carries the row
        var payload = await merge.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var rows = LanceDbArrowCodec.DecodeQueryResponse(payload);
        var row = Assert.Single(rows);
        Assert.Equal("key1", row.Record.Id);
        Assert.Equal("Test content about AI agents", row.Record.Content);
        Assert.Equal(embedding, row.Record.Embedding);
        Assert.Equal(0.8f, row.Record.Importance);
        Assert.Equal("test", row.Record.Source);
    }

    [Fact]
    public async Task ShouldSendApiKeyHeader_WhenStoreAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp43 = Fixture.Ok();
        handler.EnqueueResponse(resp43);
        using var resp42 = Fixture.Ok();
        handler.EnqueueResponse(resp42);
        using var provider = _fixture.CreateProvider(handler);

        await provider.StoreAsync("key1", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        Assert.All(handler.CapturedRequests, request =>
        {
            Assert.True(request.Headers.TryGetValues("x-api-key", out var values));
            Assert.Equal(TestApiKey, Assert.Single(values!));
        });
    }

    [Fact]
    public async Task ShouldSendDatabaseHeader_WhenDatabaseConfigured()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp41 = Fixture.Ok();
        handler.EnqueueResponse(resp41);
        using var resp40 = Fixture.Ok();
        handler.EnqueueResponse(resp40);
        var options = Fixture.DefaultOptions();
        options.Database = "orkeon-db";
        using var provider = _fixture.CreateProvider(handler, options);

        await provider.StoreAsync("key1", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        Assert.All(handler.CapturedRequests, request =>
        {
            Assert.True(request.Headers.TryGetValues("x-lancedb-database", out var values));
            Assert.Equal("orkeon-db", Assert.Single(values!));
        });
    }

    [Fact]
    public async Task ShouldCreateTableAndFtsIndex_WhenTableMissing()
    {
        // Arrange — exists 404, create OK, create_index OK, merge_insert OK
        using var handler = new FakeHttpMessageHandler();
        using var resp39 = new HttpResponseMessage(HttpStatusCode.NotFound);
        handler.EnqueueResponse(resp39);
        using var resp38 = Fixture.Ok();
        handler.EnqueueResponse(resp38);
        using var resp37 = Fixture.Ok();
        handler.EnqueueResponse(resp37);
        using var resp36 = Fixture.Ok();
        handler.EnqueueResponse(resp36);
        using var provider = _fixture.CreateProvider(handler);

        // Act
        await provider.StoreAsync("key1", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, handler.CapturedRequests.Count);

        var create = handler.CapturedRequests[1];
        Assert.Equal($"{TablePath}/create", create.RequestUri!.AbsolutePath);
        Assert.Equal("application/vnd.apache.arrow.stream", create.Content!.Headers.ContentType!.MediaType);

        var createIndex = handler.CapturedRequests[2];
        Assert.Equal($"{TablePath}/create_index", createIndex.RequestUri!.AbsolutePath);
        var indexBody = await createIndex.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"column\":\"content\"", indexBody, StringComparison.Ordinal);
        Assert.Contains("\"index_type\":\"FTS\"", indexBody, StringComparison.Ordinal);

        Assert.Equal($"{TablePath}/merge_insert", handler.CapturedRequests[3].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ShouldSkipFtsIndex_WhenCreateFullTextIndexOnInitDisabled()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp35 = new HttpResponseMessage(HttpStatusCode.NotFound);
        handler.EnqueueResponse(resp35);
        using var resp34 = Fixture.Ok();
        handler.EnqueueResponse(resp34);
        using var resp33 = Fixture.Ok();
        handler.EnqueueResponse(resp33);
        var options = Fixture.DefaultOptions();
        options.CreateFullTextIndexOnInit = false;
        using var provider = _fixture.CreateProvider(handler, options);

        await provider.StoreAsync("key1", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.CapturedRequests.Count);
        Assert.DoesNotContain(handler.CapturedRequests, r => r.RequestUri!.AbsolutePath.EndsWith("/create_index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldStillStore_WhenFtsIndexCreationFails()
    {
        // Arrange — exists 404, create OK, create_index 500, merge_insert OK
        using var handler = new FakeHttpMessageHandler();
        using var resp32 = new HttpResponseMessage(HttpStatusCode.NotFound);
        handler.EnqueueResponse(resp32);
        using var resp31 = Fixture.Ok();
        handler.EnqueueResponse(resp31);
        using var resp30 = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        handler.EnqueueResponse(resp30);
        using var resp29 = Fixture.Ok();
        handler.EnqueueResponse(resp29);
        using var provider = _fixture.CreateProvider(handler);

        // Act — index failure is logged, not fatal
        await provider.StoreAsync("key1", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{TablePath}/merge_insert", handler.CapturedRequests[^1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenEmbeddingDimensionMismatch()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp28 = Fixture.Ok();
        handler.EnqueueResponse(resp28);
        using var provider = _fixture.CreateProvider(handler);

        // Table dimension is 4; this embedding has 3 values.
        var item = MemoryItem.Create(TestContent, [1.0f, 0.0f, 0.0f]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StoreAsync("key1", item, TestContext.Current.CancellationToken));

        // No merge_insert was attempted.
        Assert.DoesNotContain(handler.CapturedRequests, r => r.RequestUri!.AbsolutePath.EndsWith("/merge_insert", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenStoreAsyncWithEmptyKey()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.StoreAsync("", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullItem()
    {
        using var handler = new FakeHttpMessageHandler();
        using var provider = _fixture.CreateProvider(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.StoreAsync("key", null!, TestContext.Current.CancellationToken));
    }

    // --- Get ---

    [Fact]
    public async Task ShouldQueryByKeyFilter_WhenGetAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp27 = Fixture.Ok();
        handler.EnqueueResponse(resp27);
        using var resp26 = Fixture.ArrowRows(
            new TestRow("key1", "Stored content", Importance: 0.8f, Source: "research"));
        handler.EnqueueResponse(resp26);
        using var provider = _fixture.CreateProvider(handler);

        var item = await provider.GetAsync("key1", TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.Equal("Stored content", item.Content);
        Assert.Equal(0.8f, item.Importance);
        Assert.Equal("research", item.Source);

        var query = handler.CapturedRequests[1];
        Assert.Equal($"{TablePath}/query", query.RequestUri!.AbsolutePath);
        var body = await query.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"vector\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"k\":1", body, StringComparison.Ordinal);
        // System.Text.Json escapes apostrophes (') in the raw payload — assert the decoded value.
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("id = 'key1'", doc.RootElement.GetProperty("filter").GetString());
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentKey()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp25 = Fixture.Ok();
        handler.EnqueueResponse(resp25);
        using var resp24 = Fixture.ArrowRows();
        handler.EnqueueResponse(resp24);
        using var provider = _fixture.CreateProvider(handler);

        var item = await provider.GetAsync("missing", TestContext.Current.CancellationToken);

        Assert.Null(item);
    }

    // --- Update ---

    [Fact]
    public async Task ShouldReturnFalse_WhenUpdateAsyncWithNonExistentKey()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp23 = Fixture.Ok();
        handler.EnqueueResponse(resp23);
        using var resp22 = Fixture.ArrowRows();
        handler.EnqueueResponse(resp22);
        using var provider = _fixture.CreateProvider(handler);

        var updated = await provider.UpdateAsync("missing", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        Assert.False(updated);
        Assert.Equal(2, handler.CapturedRequests.Count); // exists + existence query, no merge_insert
    }

    [Fact]
    public async Task ShouldMergeInsert_WhenUpdateAsyncWithExistingKey()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp21 = Fixture.Ok();
        handler.EnqueueResponse(resp21);
        using var resp20 = Fixture.ArrowRows(new TestRow("key1", "old"));
        handler.EnqueueResponse(resp20);
        using var resp19 = Fixture.Ok();
        handler.EnqueueResponse(resp19);
        using var provider = _fixture.CreateProvider(handler);

        var updated = await provider.UpdateAsync("key1", MemoryItem.Create("Updated content"), TestContext.Current.CancellationToken);

        Assert.True(updated);
        Assert.Equal($"{TablePath}/merge_insert", handler.CapturedRequests[2].RequestUri!.AbsolutePath);
    }

    // --- Delete ---

    [Fact]
    public async Task ShouldPostDeletePredicate_WhenDeleteAsyncWithExistingKey()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp18 = Fixture.Ok();
        handler.EnqueueResponse(resp18);
        using var resp17 = Fixture.ArrowRows(new TestRow("del-key", "x"));
        handler.EnqueueResponse(resp17);
        using var resp16 = Fixture.Json(HttpStatusCode.OK, "{\"version\": 12}");
        handler.EnqueueResponse(resp16);
        using var provider = _fixture.CreateProvider(handler);

        var deleted = await provider.DeleteAsync("del-key", TestContext.Current.CancellationToken);

        Assert.True(deleted);
        var delete = handler.CapturedRequests[2];
        Assert.Equal($"{TablePath}/delete", delete.RequestUri!.AbsolutePath);
        var body = await delete.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // System.Text.Json escapes apostrophes (') in the raw payload — assert the decoded value.
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("id = 'del-key'", doc.RootElement.GetProperty("predicate").GetString());
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenDeleteAsyncWithNonExistentKey()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp15 = Fixture.Ok();
        handler.EnqueueResponse(resp15);
        using var resp14 = Fixture.ArrowRows();
        handler.EnqueueResponse(resp14);
        using var provider = _fixture.CreateProvider(handler);

        var deleted = await provider.DeleteAsync("no-such-key", TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.DoesNotContain(handler.CapturedRequests, r => r.RequestUri!.AbsolutePath.EndsWith("/delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldEscapeSingleQuotes_WhenDeleteAsyncKeyContainsQuote()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp13 = Fixture.Ok();
        handler.EnqueueResponse(resp13);
        using var resp12 = Fixture.ArrowRows(new TestRow("o'brien", "x"));
        handler.EnqueueResponse(resp12);
        using var resp11 = Fixture.Ok();
        handler.EnqueueResponse(resp11);
        using var provider = _fixture.CreateProvider(handler);

        await provider.DeleteAsync("o'brien", TestContext.Current.CancellationToken);

        var body = await handler.CapturedRequests[2].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        // System.Text.Json escapes apostrophes (') in the raw payload — assert the decoded value.
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("id = 'o''brien'", doc.RootElement.GetProperty("predicate").GetString());
    }

    // --- Clear / Count / ListKeys ---

    [Fact]
    public async Task ShouldDeleteAllRows_WhenClearAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp10 = Fixture.Ok();
        handler.EnqueueResponse(resp10);
        using var resp9 = Fixture.PlainText("2");
        handler.EnqueueResponse(resp9);
        using var resp8 = Fixture.Json(HttpStatusCode.OK, "{}");
        handler.EnqueueResponse(resp8);
        using var provider = _fixture.CreateProvider(handler);

        await provider.ClearAsync(TestContext.Current.CancellationToken);

        var delete = handler.CapturedRequests[2];
        Assert.Equal($"{TablePath}/delete", delete.RequestUri!.AbsolutePath);
        var body = await delete.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"predicate\":\"true\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldParsePlainInteger_WhenCountAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp7 = Fixture.Ok();
        handler.EnqueueResponse(resp7);
        using var resp6 = Fixture.PlainText("3");
        handler.EnqueueResponse(resp6);
        using var provider = _fixture.CreateProvider(handler);

        var count = await provider.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, count);
        Assert.Equal($"{TablePath}/count_rows", handler.CapturedRequests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ShouldProjectIdsWithPagination_WhenListKeysAsync()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp5 = Fixture.Ok();
        handler.EnqueueResponse(resp5);
        using var resp4 = Fixture.ArrowRows(new TestRow("lk-1", ""), new TestRow("lk-2", ""));
        handler.EnqueueResponse(resp4);
        using var provider = _fixture.CreateProvider(handler);

        var keys = await provider.ListKeysAsync(skip: 1, take: 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["lk-1", "lk-2"], keys);
        var body = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"k\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"offset\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"column_names\":[\"id\"]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenListKeysAsyncWithNonPositiveTake()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp3 = Fixture.Ok();
        handler.EnqueueResponse(resp3);
        using var provider = _fixture.CreateProvider(handler);

        var keys = await provider.ListKeysAsync(skip: 0, take: 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(keys);
        Assert.Single(handler.CapturedRequests); // only the exists probe
    }

    // --- Error propagation ---

    [Fact]
    public async Task ShouldSurfaceServerError_WhenQueryFails()
    {
        using var handler = new FakeHttpMessageHandler();
        using var resp2 = Fixture.Ok();
        handler.EnqueueResponse(resp2);
        using var resp1 = Fixture.Json(
            HttpStatusCode.BadRequest,
            "{\"error\": \"Table 'test_memories' not found\", \"code\": 4}");
        handler.EnqueueResponse(resp1);
        using var provider = _fixture.CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAsync("key1", TestContext.Current.CancellationToken));

        Assert.Contains("LanceDB Query failed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Table 'test_memories' not found", exception.Message, StringComparison.Ordinal);
        Assert.Contains("code 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldThrowInvalidOperation_WhenNoEndpointConfigured()
    {
        using var handler = new FakeHttpMessageHandler();
        var options = Fixture.DefaultOptions();
        options.Endpoint = "";

        Assert.Throws<InvalidOperationException>(() => _fixture.CreateProvider(handler, options));
    }
}
