using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

public sealed class SqliteStateStoreTests : IDisposable
{
    private readonly SqliteStateStore _store;

    public SqliteStateStoreTests()
    {
        _store = new SqliteStateStore("Data Source=:memory:", new FakeFileSystemService());
    }

    [Fact]
    public async Task SaveAndRetrieve_RoundTrips()
    {
        // Arrange
        var checkpoints = new Dictionary<string, TaskCheckpoint>
        {
            [TaskId1] = new TaskCheckpoint
            {
                TaskId = TaskId1,
                Status = CheckpointStatus.Completed,
                Output = "result-1"
            }
        };
        var state = new SessionState
        {
            SessionId = SessionId1,
            CrewKey = CrewId1,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 1,
            TaskCheckpoints = checkpoints,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        await _store.SaveAsync(state, TestContext.Current.CancellationToken);
        var retrieved = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(SessionId1, retrieved.SessionId);
        Assert.Equal(CrewId1, retrieved.CrewKey);
        Assert.Equal(SessionPhase.Running, retrieved.Phase);
        Assert.Equal(1, retrieved.CompletedTaskIndex);
        Assert.True(retrieved.TaskCheckpoints.ContainsKey(TaskId1));
        Assert.Equal("result-1", retrieved.TaskCheckpoints[TaskId1].Output);
    }

    [Fact]
    public async Task GetLatestForCrew_Works()
    {
        // Arrange
        var older = CreateState(SessionId1, CrewId1, SessionPhase.Completed,
            updatedAt: DateTime.UtcNow.AddMinutes(-10));
        var newer = CreateState("session-2", CrewId1, SessionPhase.Running,
            updatedAt: DateTime.UtcNow);
        var otherCrew = CreateState("session-3", CrewId2, SessionPhase.Running);

        await _store.SaveAsync(older, TestContext.Current.CancellationToken);
        await _store.SaveAsync(newer, TestContext.Current.CancellationToken);
        await _store.SaveAsync(otherCrew, TestContext.Current.CancellationToken);

        // Act
        var latest = await _store.GetLatestForCrewAsync(CrewId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(latest);
        Assert.Equal("session-2", latest.SessionId);
    }

    [Fact]
    public async Task Delete_RemovesFromDatabase()
    {
        // Arrange
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        await _store.SaveAsync(state, TestContext.Current.CancellationToken);

        // Act
        await _store.DeleteAsync(SessionId1, TestContext.Current.CancellationToken);

        // Assert
        var retrieved = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Save_UpdatesExistingRecord()
    {
        // Arrange
        var initial = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        await _store.SaveAsync(initial, TestContext.Current.CancellationToken);

        var updated = initial with
        {
            Phase = SessionPhase.Completed,
            UpdatedAt = DateTime.UtcNow.AddMinutes(1)
        };

        // Act
        await _store.SaveAsync(updated, TestContext.Current.CancellationToken);

        // Assert
        var retrieved = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.Equal(SessionPhase.Completed, retrieved.Phase);

        // Should still have only one session
        var list = await _store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Single(list);
    }

    [Fact]
    public async Task List_ReturnsAllSessions()
    {
        // Arrange
        await _store.SaveAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), TestContext.Current.CancellationToken);
        await _store.SaveAsync(CreateState("session-2", CrewId2, SessionPhase.Completed), TestContext.Current.CancellationToken);

        // Act
        var list = await _store.ListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    public void Dispose()
    {
        _store.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SessionState CreateState(
        string sessionId, string crewId, SessionPhase phase,
        DateTime? updatedAt = null) =>
        new()
        {
            SessionId = sessionId,
            CrewKey = crewId,
            Phase = phase,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task ConcurrentCommands_OnTheSharedConnection_AllSucceed()
    {
        var ct = TestContext.Current.CancellationToken;
        // Real threads: the connection is shared and not thread-safe, so only the store's own gate keeps these apart.
        var work = Enumerable.Range(0, 48).Select(worker => System.Threading.Tasks.Task.Run(async () =>
        {
            for (var n = 0; n < 80; n++)
            {
                var id = $"s{worker}-{n}";
                var state = new SessionState { SessionId = id, CrewKey = CrewId1, Phase = SessionPhase.Running };
                await _store.SaveAsync(state, ct);
                var version = await _store.SaveVersionedAsync(state, stepId: id, ct: ct);
                await _store.ForkAsync(id, version.VersionId, ct: ct);
                await _store.GetAsync(id, ct);
                await _store.ListAsync(ct);
            }
        }, ct)).ToArray();

        await System.Threading.Tasks.Task.WhenAll(work);

        Assert.Equal(48 * 80 * 2, (await _store.ListAsync(ct)).Count);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task List_ReportsPhaseAndCompletedCount_FromTheRowWithoutTheBlob()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new SessionState
        {
            SessionId = SessionId1,
            CrewKey = CrewId1,
            Phase = SessionPhase.WaitingForInput,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                ["a#1"] = new() { TaskId = "a#1", Status = CheckpointStatus.Completed },
                ["b#1"] = new() { TaskId = "b#1", Status = CheckpointStatus.Failed },
            },
        };
        await _store.SaveAsync(state, ct);

        var summary = Assert.Single(await _store.ListAsync(ct));

        Assert.Equal(SessionPhase.WaitingForInput, summary.Phase);
        Assert.Equal(1, summary.CompletedTaskCount);
        Assert.Equal(CrewId1, summary.CrewKey);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task List_DoesNotReadTheSessionBlobs()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = $"r3list{Guid.NewGuid():N}";
        var cs = $"Data Source={name};Mode=Memory;Cache=Shared";
        using var store = new SqliteStateStore(cs, new FakeFileSystemService());
        await store.SaveAsync(new SessionState { SessionId = SessionId1, CrewKey = CrewId1, Phase = SessionPhase.Suspended }, ct);
        using (var other = new Microsoft.Data.Sqlite.SqliteConnection(cs))
        {
            await other.OpenAsync(ct);
            using var cmd = other.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET state_json = 'not json'";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var summary = Assert.Single(await store.ListAsync(ct));

        Assert.Equal(SessionPhase.Suspended, summary.Phase);
    }
}
