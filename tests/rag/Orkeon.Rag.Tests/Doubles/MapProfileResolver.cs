using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IRagProfileResolver"/> holding one pipeline per profile name:
/// records every name resolved and fails like the real resolver on an unknown one.
/// </summary>
public sealed class MapProfileResolver : IRagProfileResolver
{
    /// <summary>Pipelines keyed by profile name (case-insensitive).</summary>
    public Dictionary<string, IRagPipeline> Pipelines { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Profile names received, in order.</summary>
    public List<string> ResolvedProfiles { get; } = [];

    public IRagPipeline Resolve(string profileName)
    {
        ResolvedProfiles.Add(profileName);
        return Pipelines.TryGetValue(profileName, out var pipeline)
            ? pipeline
            : throw new ArgumentException($"Unknown RAG profile '{profileName}'.", nameof(profileName));
    }
}
