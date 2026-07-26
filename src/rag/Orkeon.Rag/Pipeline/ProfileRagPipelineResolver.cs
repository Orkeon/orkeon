using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Configuration;
using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Preset-aware <see cref="IRagProfileResolver"/> (RAG-04/C4): builds — and
/// memoizes — one pipeline per profile name. <c>fast</c> / <c>balanced</c> /
/// <c>quality</c> resolve to a <see cref="StagedRagPipeline"/> composed from
/// <see cref="RagProfilePresets"/> plus the <c>Orkeon:Rag</c> configuration
/// overrides; <c>default</c> (the harness's implicit profile) resolves to the
/// host's registered <c>IRagPipeline</c> — so a host-provided pipeline keeps
/// the last word for non-profiled queries. Unknown names fail loudly with the
/// list of known profiles.
/// </summary>
public sealed class ProfileRagPipelineResolver : IRagProfileResolver
{
    /// <summary>Name resolving to the host's default pipeline.</summary>
    public const string DefaultProfileName = "default";

    private readonly IConfiguration _configuration;
    private readonly Func<RagOptions, IRagPipeline> _pipelineFactory;
    private readonly Lazy<IRagPipeline> _defaultPipeline;
    private readonly ConcurrentDictionary<string, IRagPipeline> _pipelines =
        new(StringComparer.Ordinal);

    /// <summary>Initializes the resolver.</summary>
    /// <param name="configuration">Configuration root; <c>Orkeon:Rag</c> keys override each preset individually.</param>
    /// <param name="pipelineFactory">Builds a pipeline from a fully composed <see cref="RagOptions"/>.</param>
    /// <param name="defaultPipeline">
    /// Lazily resolves the host's default <c>IRagPipeline</c> (served for the
    /// <c>default</c> profile name and blank names).
    /// </param>
    public ProfileRagPipelineResolver(
        IConfiguration configuration,
        Func<RagOptions, IRagPipeline> pipelineFactory,
        Func<IRagPipeline> defaultPipeline)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        ArgumentNullException.ThrowIfNull(defaultPipeline);

        _configuration = configuration;
        _pipelineFactory = pipelineFactory;
        _defaultPipeline = new Lazy<IRagPipeline>(defaultPipeline, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public IRagPipeline Resolve(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var normalized = profileName.Trim().ToUpperInvariant();
        if (normalized == "DEFAULT")
            return _defaultPipeline.Value;

        if (!RagProfilePresets.TryParse(profileName, out var profile))
        {
            throw new RagComponentNotFoundException(
                "profile",
                profileName,
                [.. RagProfilePresets.KnownProfileNames, DefaultProfileName]);
        }

        return _pipelines.GetOrAdd(
            RagProfilePresets.NameOf(profile),
            _ => _pipelineFactory(RagOptionsFactory.Build(_configuration, profileName)));
    }
}
