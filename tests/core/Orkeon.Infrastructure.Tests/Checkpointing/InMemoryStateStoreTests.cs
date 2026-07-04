using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

public class InMemoryStateStoreTests
{
    private readonly InMemoryStateStore _store = new();

    [Fact]
    public async Task SaveAndRetrieve_RoundTrips()
    {
        // Arrange
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        // Act
        await _store.SaveAsync(state, TestContext.Current.CancellationToken);
        var retrieved = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(SessionId1, retrieved.SessionId);
        Assert.Equal(CrewId1, retrieved.CrewId);
        Assert.Equal(SessionPhase.Running, retrieved.Phase);
    }

    [Fact]
    public async Task GetLatestForCrew_FiltersCorrectly()
    {
        // Arrange
        var older = CreateState(SessionId1, CrewId1, SessionPhase.Completed,
            updatedAt: DateTime.UtcNow.AddMinutes(-10));
        var newer = CreateState("session-2", CrewId1, SessionPhase.Running,
            updatedAt: DateTime.UtcNow);
        var otherCrew = CreateState("session-3", CrewId2, SessionPhase.Running,
            updatedAt: DateTime.UtcNow.AddMinutes(5));

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
    public async Task Delete_RemovesSession()
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

    [Fact]
    public async Task Save_Overwrites_ExistingSession()
    {
        // Arrange
        var initial = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        await _store.SaveAsync(initial, TestContext.Current.CancellationToken);

        var updated = initial with { Phase = SessionPhase.Completed };
        await _store.SaveAsync(updated, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(SessionPhase.Completed, retrieved.Phase);
    }

    private static SessionState CreateState(
        string sessionId, string crewId, SessionPhase phase,
        DateTime? updatedAt = null) =>
        new()
        {
            SessionId = sessionId,
            CrewId = crewId,
            Phase = phase,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
}
