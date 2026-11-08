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
        Assert.Equal(CrewId1, loaded.CrewKey);
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
            CrewKey = CrewId1,
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
            CrewKey = CrewId1,
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
            CrewKey = CrewId1,
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
            CrewKey = crewId,
            Phase = phase,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task Save_KeepsThePreviousSession_WhenTheWriteIsTorn()
    {
        var store = CreateStore(out var fake);
        var failing = new Orkeon.Infrastructure.Tests.Doubles.FailingWriteFileSystemService(fake);
        var guarded = new JsonFileStateStore(failing, BaseVirtualPath);
        await guarded.SaveAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), TestContext.Current.CancellationToken);

        failing.FailNextJsonWrites = 1;
        await Assert.ThrowsAsync<IOException>(() =>
            guarded.SaveAsync(CreateState(SessionId1, CrewId1, SessionPhase.Completed), TestContext.Current.CancellationToken));

        var kept = await guarded.GetAsync(SessionId1, TestContext.Current.CancellationToken);
        Assert.Equal(SessionPhase.Running, kept!.Phase);
        Assert.Single(await guarded.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task Get_NamesTheSession_WhenItsCheckpointIsCorrupt()
    {
        var store = CreateStore(out var fake);
        await fake.WriteAllTextAsync($"{BaseVirtualPath}/{SessionId1}.json", "{ \"sessionId\": ", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => store.GetAsync(SessionId1, TestContext.Current.CancellationToken));

        Assert.Contains(SessionId1, ex.Message, StringComparison.Ordinal);
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException, exactMatch: false);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task SaveVersioned_KeepsThePreviousVersionAndManifest_WhenTheirWriteIsTorn()
    {
        var store = CreateStore(out var fake);
        var failing = new Orkeon.Infrastructure.Tests.Doubles.FailingWriteFileSystemService(fake);
        var guarded = new JsonFileStateStore(failing, BaseVirtualPath);
        await guarded.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), "a", null, TestContext.Current.CancellationToken);

        failing.FailNextJsonWrites = 1;
        await Assert.ThrowsAsync<IOException>(() =>
            guarded.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Completed), "b", null, TestContext.Current.CancellationToken));

        // The torn write went to a temp file: version 2 does not exist half-written, and version 1 is still listed.
        Assert.Null(await fake.TryReadAllTextAsync($"{BaseVirtualPath}/{SessionId1}/v002.json", CancellationToken.None));
        var history = await guarded.GetHistoryAsync(SessionId1, null, TestContext.Current.CancellationToken);
        Assert.Equal("a", Assert.Single(history).StepId);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async Task SaveVersioned_RebuildsTheManifestFromTheVersions_WhenItIsCorrupt()
    {
        var store = CreateStore(out var fake);
        await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), "a", null, TestContext.Current.CancellationToken);
        await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), "b", null, TestContext.Current.CancellationToken);
        await fake.WriteAllTextAsync($"{BaseVirtualPath}/{SessionId1}/manifest.json", "{ \"nextVersion\": ", TestContext.Current.CancellationToken);

        var third = await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Completed), "c", null, TestContext.Current.CancellationToken);

        // Version 1 is not overwritten: the next number follows the files on disk, and the history keeps all three.
        Assert.Equal(3, third.Version);
        var history = await store.GetHistoryAsync(SessionId1, null, TestContext.Current.CancellationToken);
        Assert.Equal(["a", "b", "c"], history.OrderBy(v => v.Version).Select(v => v.StepId));
    }

    [Fact]
    public async Task SaveVersioned_DeletesTheOldestVersions_BeyondTheRetentionLimit()
    {
        var fake = new FakeFileSystemService();
        fake.AddMount(BaseVirtualPath, FileAccessRights.ReadWrite);
        var store = new JsonFileStateStore(fake, BaseVirtualPath, maxVersions: 2);
        foreach (var step in new[] { "a", "b", "c", "d" })
            await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), step, null, TestContext.Current.CancellationToken);

        // The two newest are kept, the files of the older ones are gone, and the numbering goes on from the newest.
        var history = await store.GetHistoryAsync(SessionId1, null, TestContext.Current.CancellationToken);
        Assert.Equal(["c", "d"], history.OrderBy(v => v.Version).Select(v => v.StepId));
        Assert.False(await fake.ExistsAsync($"{BaseVirtualPath}/{SessionId1}/v001.json", TestContext.Current.CancellationToken));
        Assert.False(await fake.ExistsAsync($"{BaseVirtualPath}/{SessionId1}/v002.json", TestContext.Current.CancellationToken));
        Assert.True(await fake.ExistsAsync($"{BaseVirtualPath}/{SessionId1}/v004.json", TestContext.Current.CancellationToken));
        var fifth = await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Completed), "e", null, TestContext.Current.CancellationToken);
        Assert.Equal(5, fifth.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SaveVersioned_KeepsEveryVersion_WhenTheRetentionLimitIsZeroOrNegative(int maxVersions)
    {
        var fake = new FakeFileSystemService();
        fake.AddMount(BaseVirtualPath, FileAccessRights.ReadWrite);
        var store = new JsonFileStateStore(fake, BaseVirtualPath, maxVersions);
        for (var i = 0; i < 25; i++)
            await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), $"s{i}", null, TestContext.Current.CancellationToken);

        Assert.Equal(25, (await store.GetHistoryAsync(SessionId1, null, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task SaveVersioned_KeepsTwentyVersions_ByDefault()
    {
        var store = CreateStore(out _);
        for (var i = 0; i < 22; i++)
            await store.SaveVersionedAsync(CreateState(SessionId1, CrewId1, SessionPhase.Running), $"s{i}", null, TestContext.Current.CancellationToken);

        var history = await store.GetHistoryAsync(SessionId1, null, TestContext.Current.CancellationToken);
        Assert.Equal(JsonFileStateStore.DefaultMaxVersions, history.Count);
        Assert.Equal("s21", history.MaxBy(v => v.Version)!.StepId);
    }
}
