using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ICognitiveMemoryService"/> that records what a run asks of it: recall
/// answers nothing, remember and consolidate are counted, consolidate can be made to fail.
/// </summary>
public sealed class FakeCognitiveMemoryService : ICognitiveMemoryService
{
    public List<CrewId> Consolidated { get; } = [];
    public List<string> Remembered { get; } = [];
    public Exception? ConsolidateFails { get; set; }

    public Task<IReadOnlyList<ScoredMemory>> RecallAsync(
        CrewId crewId, string query, RecallOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScoredMemory>>([]);

    public Task<MemoryItem> RememberAsync(
        CrewId crewId, string content, string? context = null, CancellationToken cancellationToken = default)
    {
        Remembered.Add(content);
        return Task.FromResult(MemoryItem.Create(content));
    }

    public Task<ConsolidationResult> ConsolidateAsync(CrewId crewId, CancellationToken cancellationToken = default)
    {
        Consolidated.Add(crewId);
        return ConsolidateFails is null
            ? Task.FromResult(new ConsolidationResult())
            : Task.FromException<ConsolidationResult>(ConsolidateFails);
    }

    public Task<MemoryAnalysis> AnalyzeAsync(
        CrewId crewId, string content, string? context = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ContradictionCheck> CheckContradictionsAsync(
        CrewId crewId, string content, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ICrewMemorySystem GetMemorySystem(CrewId crewId) => throw new NotSupportedException();

    public void ReleaseMemorySystem(CrewId crewId)
    {
    }

    public Task SaveMemoryAsync(CrewId crewId, MemoryItem item, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId, string query, int maxResults = 10, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task ClearMemoryAsync(CrewId crewId, DomainMemoryType? typeFilter = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
