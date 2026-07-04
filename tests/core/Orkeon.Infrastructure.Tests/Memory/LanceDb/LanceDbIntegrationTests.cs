using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using Fixture = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture;
using TestRow = Orkeon.Infrastructure.Tests.Memory.LanceDb.LanceDbMemoryProviderTestsFixture.LanceDbTestRow;

namespace Orkeon.Infrastructure.Tests.Memory.LanceDb;

/// <summary>
/// End-to-end flows of the LanceDB provider against a scripted REST conversation
/// (no server is started), plus the DI registration of <c>AddOrkeonLanceDb</c>.
/// </summary>
public class LanceDbIntegrationTests
{
    [Fact]
    public async Task EndToEnd_StoreGetCountDelete_DrivesTheRestProtocol()
    {
        // Arrange — one provider instance, scripted server conversation:
        // exists(404) → create → create_index → merge_insert (Store)
        // → query (Get) → count_rows (Count) → query + delete (Delete)
        using var handler = new FakeHttpMessageHandler();
        using var resp8 = new HttpResponseMessage(HttpStatusCode.NotFound);
        handler.EnqueueResponse(resp8);
        using var resp7 = Fixture.Ok();
        handler.EnqueueResponse(resp7);
        using var resp6 = Fixture.Ok();
        handler.EnqueueResponse(resp6);
        using var resp5 = Fixture.Ok();
        handler.EnqueueResponse(resp5);
        using var resp4 = Fixture.ArrowRows(
            new TestRow("e2e-1", "AI agent collaboration patterns", Embedding: [1f, 0f, 0f, 0f], Importance: 0.9f, Source: "research"));
        handler.EnqueueResponse(resp4);
        using var resp3 = Fixture.PlainText("1");
        handler.EnqueueResponse(resp3);
        using var resp2 = Fixture.ArrowRows(new TestRow("e2e-1", "AI agent collaboration patterns"));
        handler.EnqueueResponse(resp2);
        using var resp1 = Fixture.Json(HttpStatusCode.OK, "{\"version\": 3}");
        handler.EnqueueResponse(resp1);

        var fixture = new Fixture();
        using var provider = fixture.CreateProvider(handler);
        await provider.InitializeAsync(MemoryProviderConfig.LanceDB(), TestContext.Current.CancellationToken);

        // Act + Assert — Store
        var embedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        await provider.StoreAsync("e2e-1", MemoryItem.Create("AI agent collaboration patterns", embedding, 0.9f, "research"), TestContext.Current.CancellationToken);

        // Act + Assert — Get
        var retrieved = await provider.GetAsync("e2e-1", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.Equal("AI agent collaboration patterns", retrieved.Content);
        Assert.Equal(0.9f, retrieved.Importance);

        // Act + Assert — Count
        Assert.Equal(1, await provider.CountAsync(TestContext.Current.CancellationToken));

        // Act + Assert — Delete
        Assert.True(await provider.DeleteAsync("e2e-1", TestContext.Current.CancellationToken));

        // Assert — the full conversation hit the expected endpoints, in order
        var paths = handler.CapturedRequests.Select(r => r.RequestUri!.AbsolutePath).ToList();
        Assert.Equal(
        [
            "/v1/table/test_memories/exists",
            "/v1/table/test_memories/create",
            "/v1/table/test_memories/create_index",
            "/v1/table/test_memories/merge_insert",
            "/v1/table/test_memories/query",
            "/v1/table/test_memories/count_rows",
            "/v1/table/test_memories/query",
            "/v1/table/test_memories/delete"
        ], paths);
    }

    [Fact]
    public void DI_Registration_ShouldResolveProvider()
    {
        // Arrange — endpoint/api key bound from the Orkeon:LanceDb section
        var configValues = new Dictionary<string, string?>
        {
            ["Orkeon:LanceDb:Endpoint"] = Fixture.Endpoint,
            ["Orkeon:LanceDb:ApiKey"] = "di-test-key",
            ["Orkeon:LanceDb:TableName"] = "di_test",
            ["Orkeon:LanceDb:DefaultTopK"] = "5"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonMemoryMigration();
        services.AddOrkeonLanceDb(configuration);

        // Act
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetService<LanceDbMemoryProvider>();
        var migrationService = serviceProvider.GetService<LanceDbMigrationService>();

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("LanceDB", provider.Name);
        Assert.NotNull(migrationService);
    }

    [Fact]
    public void DI_Registration_ShouldFailFast_WhenEndpointMissing()
    {
        // No Orkeon:LanceDb:Endpoint — resolving the singleton must surface
        // a clear configuration error instead of a silent local fallback.
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonLanceDb(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(serviceProvider.GetRequiredService<LanceDbMemoryProvider>);
    }
}
