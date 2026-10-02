using System.Text.Json;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using Fixture = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// GAP-20 — LanceDB filters on the server: a full-text search sends the metadata filter as its SQL
/// predicate, applied before <c>k</c> (<c>prefilter</c>), with the semantics of a similarity search.
/// </summary>
public class LanceDbMemoryProviderTextSearchFilterTests
{
    private readonly Fixture _fixture = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> QueryOfAsync(Func<Orkeon.Infrastructure.Memory.LanceDb.LanceDbMemoryProvider, Task> search)
    {
        using var handler = new FakeHttpMessageHandler();
        using var table = Fixture.Ok();
        handler.EnqueueResponse(table);
        using var rows = Fixture.ArrowRows();
        handler.EnqueueResponse(rows);
        using var provider = _fixture.CreateProvider(handler);

        await search(provider);

        var body = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(Ct);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task A_full_text_search_sends_the_filter_as_a_prefiltering_predicate()
    {
        var query = await QueryOfAsync(provider => provider.SearchAsync(
            "contract", 5, new Dictionary<string, object> { ["kind"] = "crew-memory", ["crew"] = "legal-watch" }, Ct));

        Assert.Equal(
            "metadata_json LIKE '%\"kind\":\"crew-memory\"%' AND metadata_json LIKE '%\"crew\":\"legal-watch\"%'",
            query.GetProperty("filter").GetString());
        Assert.True(query.GetProperty("prefilter").GetBoolean());
        Assert.Equal("contract", query.GetProperty("full_text_query").GetProperty("string_query").GetProperty("query").GetString());
    }

    [Fact]
    public async Task A_full_text_search_without_a_filter_sends_no_predicate()
    {
        var query = await QueryOfAsync(provider => provider.SearchAsync("contract", 5, cancellationToken: Ct));

        Assert.True(!query.TryGetProperty("filter", out var filter) || filter.ValueKind == JsonValueKind.Null);
    }
}
