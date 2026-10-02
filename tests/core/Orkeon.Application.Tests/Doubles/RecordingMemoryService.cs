using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryService"/> around a real one: every call is recorded, then passed
/// on — to tell a memory that was never touched from one that was. The real one stays the caller's
/// to dispose.
/// </summary>
internal sealed class RecordingMemoryService(IMemoryService inner) : IMemoryService
{
    public List<string> Calls { get; } = [];

    public IMemoryService Inner => inner;

    public ICrewMemorySystem GetMemorySystem(CrewId crewId)
    {
        Calls.Add(nameof(GetMemorySystem));
        return inner.GetMemorySystem(crewId);
    }

    public void ReleaseMemorySystem(CrewId crewId)
    {
        Calls.Add(nameof(ReleaseMemorySystem));
        inner.ReleaseMemorySystem(crewId);
    }

    public System.Threading.Tasks.Task SaveMemoryAsync(CrewId crewId, MemoryItem item, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(SaveMemoryAsync));
        return inner.SaveMemoryAsync(crewId, item, cancellationToken);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId, string query, int maxResults = 10, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(SearchMemoryAsync));
        return inner.SearchMemoryAsync(crewId, query, maxResults, typeFilter, cancellationToken);
    }

    public System.Threading.Tasks.Task ClearMemoryAsync(
        CrewId crewId, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(nameof(ClearMemoryAsync));
        return inner.ClearMemoryAsync(crewId, typeFilter, cancellationToken);
    }
}
