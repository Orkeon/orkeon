using System.Collections.Concurrent;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Memory;

/// <summary>
/// Process-wide registry of what each crew declared about its memory: whether it remembers at all
/// (<c>memory:</c>), the provider type its memory lives in (e.g. <c>redis</c>) and its name, the
/// scope of that memory. The orchestrator records all three at kickoff (P2-O-02, GAP-20, GAP-30);
/// <see cref="MemoryService"/> reads the type and the name when it materializes the crew's memory
/// system, and <see cref="MemoryCoordinator"/> reads whether the crew remembers before storing or
/// recalling anything. Keyed by <see cref="CrewId"/>, so one crew's selection never leaks to
/// another. A crew that declared nothing is simply absent: it does not remember, and its memory
/// system, if a caller asks for it, is the host default.
/// </summary>
public sealed class CrewMemoryProviderRegistry
{
    private readonly ConcurrentDictionary<CrewId, Selection> _selections = new();

    /// <summary>
    /// Records what a crew declared at kickoff. A null or blank <paramref name="providerType"/>
    /// leaves the crew on the host default; a null or blank <paramref name="crewName"/> makes its
    /// id the scope of its memory.
    /// </summary>
    /// <param name="crewId">The crew.</param>
    /// <param name="providerType">The memory provider type the crew selected, if any.</param>
    /// <param name="crewName">The crew's name, if any — the scope of its long-term memory.</param>
    /// <param name="memoryEnabled">
    /// Whether the crew remembers (<c>memory: true</c>): only then does a run store the result of
    /// its tasks and recall its memories before each task (GAP-30).
    /// </param>
    public void Record(CrewId crewId, string? providerType, string? crewName, bool memoryEnabled)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        var type = string.IsNullOrWhiteSpace(providerType) ? null : providerType;
        var name = string.IsNullOrWhiteSpace(crewName) ? null : crewName.Trim();
        if (type is null && name is null && !memoryEnabled)
            _selections.TryRemove(crewId, out _);
        else
            _selections[crewId] = new Selection(type, name, memoryEnabled);
    }

    /// <summary>
    /// Forgets a crew's selection entirely — the daemon-side counterpart of
    /// <see cref="Record"/>: every hosted message loads a fresh crew with a fresh id,
    /// and an entry per run that nothing ever removes is a leak with a slow fuse.
    /// </summary>
    public void Remove(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        _selections.TryRemove(crewId, out _);
    }

    /// <summary>
    /// Whether the crew remembers: <see langword="true"/> only for a crew recorded with
    /// <c>memory: true</c>. A crew never recorded — an A2A task, a hand-built context — does not.
    /// </summary>
    public bool IsMemoryEnabled(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return _selections.TryGetValue(crewId, out var selection) && selection.MemoryEnabled;
    }

    /// <summary>
    /// Returns the crew's declared provider, or <c>null</c> when none was recorded.
    /// </summary>
    public string? GetProvider(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return _selections.TryGetValue(crewId, out var selection) ? selection.ProviderType : null;
    }

    /// <summary>
    /// Returns the crew's recorded name, or <c>null</c> for a crew recorded without one.
    /// </summary>
    public string? GetName(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return _selections.TryGetValue(crewId, out var selection) ? selection.CrewName : null;
    }

    /// <summary>
    /// Returns the scope of the crew's long-term memory: its recorded name, so that the runs of
    /// one crew share what they stored; its id when it has none, so that the memory of an unnamed
    /// crew lasts its one run (GAP-20).
    /// </summary>
    public string GetScope(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return GetName(crewId) ?? crewId.ToString();
    }

    private sealed record Selection(string? ProviderType, string? CrewName, bool MemoryEnabled);
}
