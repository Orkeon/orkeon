using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;
// IMemorySystem alias removed (now ICrewMemorySystem)

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IMemoryService for testing.
/// </summary>
public sealed class MockMemoryService : IMemoryService
{
    public void ReleaseMemorySystem(CrewId crewId)
    {
        // Nothing held per crew in this double.
    }

    private readonly List<MemoryItem> _savedItems = [];
    private IReadOnlyList<MemoryItem> _searchResult = Array.Empty<MemoryItem>();

    // --- Tracking ---
    public int SaveCallCount { get; private set; }
    public int SearchCallCount { get; private set; }
    public int ClearCallCount { get; private set; }
    public MemoryItem? LastSavedItem { get; private set; }
    public string? LastSearchQuery { get; private set; }

    // --- Configuration ---
    public void SetSearchResult(IReadOnlyList<MemoryItem> result) => _searchResult = result;

    /// <summary>Gets all saved items for assertions.</summary>
    public IReadOnlyList<MemoryItem> SavedItems => _savedItems;

    public ICrewMemorySystem GetMemorySystem(CrewId crewId) =>
        throw new NotImplementedException("Mock does not implement GetMemorySystem");

    public Task SaveMemoryAsync(CrewId crewId, MemoryItem item, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        LastSavedItem = item;
        _savedItems.Add(item);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId,
        string query,
        int maxResults = 10,
        DomainMemoryType? typeFilter = null,
        CancellationToken cancellationToken = default)
    {
        SearchCallCount++;
        LastSearchQuery = query;
        return Task.FromResult(_searchResult);
    }

    public Task ClearMemoryAsync(CrewId crewId, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default)
    {
        ClearCallCount++;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        SaveCallCount = 0;
        SearchCallCount = 0;
        ClearCallCount = 0;
        LastSavedItem = null;
        LastSearchQuery = null;
        _savedItems.Clear();
    }
}
