using System.Collections.Concurrent;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Memory;

/// <summary>
/// Process-wide registry mapping a crew to its declared memory-provider selection (e.g. <c>redis</c>).
/// The orchestrator records the crew's provider at kickoff (P2-O-02); <see cref="MemoryService"/> reads
/// it when it materializes the crew's memory system, resolving the string to a concrete
/// <c>IMemoryProvider</c> via the factory. Keyed by <see cref="CrewId"/>, so per-crew selections never
/// leak across crews. A crew with no declared provider is simply absent — the host default applies.
/// </summary>
public sealed class CrewMemoryProviderRegistry
{
    private readonly ConcurrentDictionary<CrewId, string> _providers = new();

    /// <summary>
    /// Records the provider selection for a crew. A null/blank value clears any prior selection,
    /// so the crew falls back to the host default.
    /// </summary>
    public void SetProvider(CrewId crewId, string? providerType)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        if (string.IsNullOrWhiteSpace(providerType))
            _providers.TryRemove(crewId, out _);
        else
            _providers[crewId] = providerType;
    }

    /// <summary>
    /// Returns the crew's declared provider, or <c>null</c> when none was recorded.
    /// </summary>
    public string? GetProvider(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return _providers.TryGetValue(crewId, out var provider) ? provider : null;
    }
}
