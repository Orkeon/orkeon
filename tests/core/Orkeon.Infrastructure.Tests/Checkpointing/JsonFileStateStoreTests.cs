using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

public class JsonFileStateStoreTests
{
    private const string BaseVirtualPath = "/state";

    private static JsonFileStateStore CreateStore(out FakeFileSystemService fake)
    {
        fake = new FakeFileSystemService();
        fake.AddMount(BaseVirtualPath, FileAccessRights.ReadWrite);
        return new JsonFileStateStore(fake, BaseVirtualPath);
    }

    [Fact]
    public async Task Save_ThenGet_RoundTrips()
    {
        var store = CreateStore(out _);
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        await store.SaveAsync(state, TestContext.Current.CancellationToken);
        var loaded = await store.GetAsync(SessionId1, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(SessionId1, loaded.SessionId);
        Assert.Equal(CrewId1, loaded.CrewId);
        Assert.Equal(SessionPhase.Running, loaded.Phase);
    }

    [Fact]
    public async Task Save_WritesToVirtualPath()
    {
        var store = CreateStore(out var fake);
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        var content = await fake.TryReadAllTextAsync($"{BaseVirtualPath}/{SessionId1}.json", CancellationToken.None);
        Assert.NotNull(content);
        Assert.Contains(SessionId1, content);
    }

    [Fact]
    public async Task Load_ReadsBackCheckpoints()
    {
        var store = CreateStore(out _);
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
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 1,
            TaskCheckpoints = checkpoints,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await store.SaveAsync(state, TestContext.Current.CancellationToken);
        var loaded = await store.GetAsync(SessionId1, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.CompletedTaskIndex);
        Assert.True(loaded.TaskCheckpoints.ContainsKey(TaskId1));
        Assert.Equal("result-1", loaded.TaskCheckpoints[TaskId1].Output);
    }

    [Fact]
    public async Task GetLatestForCrew_ReturnsNewest()
    {
        var store = CreateStore(out _);
        var older = CreateState(SessionId1, CrewId1, SessionPhase.Completed,
            updatedAt: DateTime.UtcNow.AddMinutes(-10));
        var newer = CreateState("session-2", CrewId1, SessionPhase.Running,
            updatedAt: DateTime.UtcNow);
        var otherCrew = CreateState("session-3", CrewId2, SessionPhase.Running);

        await store.SaveAsync(older, TestContext.Current.CancellationToken);
        await store.SaveAsync(newer, TestContext.Current.CancellationToken);
        await store.SaveAsync(otherCrew, TestContext.Current.CancellationToken);

        var latest = await store.GetLatestForCrewAsync(CrewId1, TestContext.Current.CancellationToken);

        Assert.NotNull(latest);
        Assert.Equal("session-2", latest.SessionId);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var store = CreateStore(out var fake);
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        await store.DeleteAsync(SessionId1, TestContext.Current.CancellationToken);

        var content = await fake.TryReadAllTextAsync($"{BaseVirtualPath}/{SessionId1}.json", CancellationToken.None);
        Assert.Null(content);
        var loaded = await store.GetAsync(SessionId1, TestContext.Current.CancellationToken);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var store = CreateStore(out _);
        var result = await store.GetAsync("non-existent", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task List_ReturnsAllSessions()
    {
        var store = CreateStore(out _);
        await store.SaveAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), TestContext.Current.CancellationToken);
        await store.SaveAsync(CreateState("session-2", CrewId2, SessionPhase.Completed), TestContext.Current.CancellationToken);

        var list = await store.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task List_EmptyBaseDir_ReturnsEmpty()
    {
        var store = CreateStore(out _);
        var list = await store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(list);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("../../etc/passwd")]
    [InlineData("session/../../other")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_InvalidSessionId_Throws(string badId)
    {
        var store = CreateStore(out _);
        var state = new SessionState
        {
            SessionId = badId,
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(state, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_SessionIdTooLong_Throws()
    {
        var store = CreateStore(out _);
        var longId = new string('a', 300);
        var state = new SessionState
        {
            SessionId = longId,
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(state, TestContext.Current.CancellationToken));
    }

    private static SessionState CreateState(
        string sessionId, string crewId, SessionPhase phase,
        DateTime? updatedAt = null) =>
        new()
        {
            SessionId = sessionId,
            CrewId = crewId,
            Phase = phase,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
}
