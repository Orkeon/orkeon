using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

/// <summary>
/// FWK-01: a session is found again by the stable key of its crew, in the stores that can be built
/// without a server (memory, JSON, SQLite). The PostgreSQL store runs the same statements through its
/// own integration tests.
/// </summary>
public sealed class StateStoreCrewKeyTests : IDisposable
{
    private readonly SqliteStateStore _sqlite = new("Data Source=:memory:", new FakeFileSystemService());

    public void Dispose() => _sqlite.Dispose();

    private IStateStore Store(string kind) => kind switch
    {
        "memory" => new InMemoryStateStore(),
        "json" => new JsonFileStateStore(new FakeFileSystemService().AddMount("/state", FileAccessRights.ReadWrite), "/state"),
        _ => _sqlite,
    };

    [Theory]
    [InlineData("memory")]
    [InlineData("json")]
    [InlineData("sqlite")]
    [Trait("BR", "FWK-01")]
    public async Task ShouldReturnTheLatestSessionOfTheCrewKey_AndNoneOfAnotherKey(string kind)
    {
        var store = Store(kind);
        var ct = TestContext.Current.CancellationToken;
        var older = DateTime.UtcNow.AddMinutes(-5);
        await store.SaveAsync(new SessionState { SessionId = "run-1", CrewKey = "alpha@/crew/a.yaml", CreatedAt = older, UpdatedAt = older }, ct);
        await store.SaveAsync(new SessionState { SessionId = "run-2", CrewKey = "alpha@/crew/a.yaml", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }, ct);
        await store.SaveAsync(new SessionState { SessionId = "run-3", CrewKey = "beta@/crew/b.yaml" }, ct);

        var latest = await store.GetLatestForCrewAsync("alpha@/crew/a.yaml", ct);
        var none = await store.GetLatestForCrewAsync("gamma@/crew/c.yaml", ct);
        var summaries = await store.ListAsync(ct);

        Assert.Equal("run-2", latest?.SessionId);
        Assert.Equal("alpha@/crew/a.yaml", latest?.CrewKey);
        Assert.Null(none);
        Assert.Contains(summaries, s => s.SessionId == "run-3" && s.CrewKey == "beta@/crew/b.yaml");
    }
}
