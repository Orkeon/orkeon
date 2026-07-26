using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagProfileResolver"/>: always returns
/// <see cref="Pipeline"/> and records every profile name resolved.
/// </summary>
public sealed class RecordingProfileResolver : IRagProfileResolver
{
    /// <summary>Pipeline returned for every profile.</summary>
    public required IRagPipeline Pipeline { get; init; }

    /// <summary>Profile names received, in order.</summary>
    public List<string> ResolvedProfiles { get; } = [];

    public IRagPipeline Resolve(string profileName)
    {
        ResolvedProfiles.Add(profileName);
        return Pipeline;
    }
}
