using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ICognitiveMemoryService"/>: recall answers what <see cref="Recalled"/> holds,
/// remember and consolidate are recorded; the plain memory members are never reached by a coordinator
/// that goes through the cognitive path.
/// </summary>
internal sealed class FakeCognitiveMemoryService : ICognitiveMemoryService
{
    public List<ScoredMemory> Recalled { get; } = [];
    public List<(CrewId Crew, string Query, RecallOptions? Options)> Recalls { get; } = [];
    public List<(CrewId Crew, string Content, string? Context)> Remembered { get; } = [];
    public List<CrewId> Consolidated { get; } = [];
    public Exception? RecallFails { get; set; }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemory>> RecallAsync(
        CrewId crewId, string query, RecallOptions? options = null, CancellationToken cancellationToken = default)
    {
        Recalls.Add((crewId, query, options));
        if (RecallFails is not null)
            throw RecallFails;
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemory>>([.. Recalled]);
    }

    public System.Threading.Tasks.Task<MemoryItem> RememberAsync(
        CrewId crewId, string content, string? context = null, CancellationToken cancellationToken = default)
    {
        Remembered.Add((crewId, content, context));
        return System.Threading.Tasks.Task.FromResult(MemoryItem.Create(content));
    }

    public System.Threading.Tasks.Task<ConsolidationResult> ConsolidateAsync(
        CrewId crewId, CancellationToken cancellationToken = default)
    {
        Consolidated.Add(crewId);
        return System.Threading.Tasks.Task.FromResult(new ConsolidationResult());
    }

    public System.Threading.Tasks.Task<MemoryAnalysis> AnalyzeAsync(
        CrewId crewId, string content, string? context = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<ContradictionCheck> CheckContradictionsAsync(
        CrewId crewId, string content, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ICrewMemorySystem GetMemorySystem(CrewId crewId) => throw new NotSupportedException();

    public void ReleaseMemorySystem(CrewId crewId)
    {
    }

    public System.Threading.Tasks.Task SaveMemoryAsync(CrewId crewId, MemoryItem item, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId, string query, int maxResults = 10, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task ClearMemoryAsync(
        CrewId crewId, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
