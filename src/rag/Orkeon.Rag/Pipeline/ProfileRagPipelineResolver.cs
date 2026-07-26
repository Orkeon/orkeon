using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;
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
/// overrides; <c>adaptive</c> (RAG-05/C3) resolves to an
/// <see cref="AdaptiveRagPipeline"/> routing over the classifier and the
/// <c>balanced</c>/<c>quality</c> pipelines; <c>default</c> (the harness's
/// implicit profile) resolves to the host's registered <c>IRagPipeline</c> — so
/// a host-provided pipeline keeps the last word for non-profiled queries.
/// Unknown names fail loudly with the list of known profiles.
/// </summary>
public sealed class ProfileRagPipelineResolver : IRagProfileResolver
{
    /// <summary>Name resolving to the host's default pipeline.</summary>
    public const string DefaultProfileName = "default";

    private readonly IConfiguration _configuration;
    private readonly Func<RagOptions, IRagPipeline> _pipelineFactory;
    private readonly Lazy<IRagPipeline> _defaultPipeline;
    private readonly Func<IQueryComplexityClassifier>? _complexityClassifier;
    private readonly Func<IChatClient>? _chatClient;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ConcurrentDictionary<string, IRagPipeline> _pipelines =
        new(StringComparer.Ordinal);

    /// <summary>Initializes the resolver.</summary>
    /// <param name="configuration">Configuration root; <c>Orkeon:Rag</c> keys override each preset individually.</param>
    /// <param name="pipelineFactory">Builds a pipeline from a fully composed <see cref="RagOptions"/>.</param>
    /// <param name="defaultPipeline">
    /// Lazily resolves the host's default <c>IRagPipeline</c> (served for the
    /// <c>default</c> profile name and blank names).
    /// </param>
    /// <param name="complexityClassifier">
    /// Lazily resolves the <see cref="IQueryComplexityClassifier"/> routing the
    /// <c>adaptive</c> profile. Required only when <c>adaptive</c> is resolved —
    /// <c>AddOrkeonRag</c> wires it (heuristic by default).
    /// </param>
    /// <param name="chatClient">
    /// Lazily resolves the chat client used by the <c>adaptive</c> profile's
    /// direct (<c>NoRetrieval</c>) answers. Required only when <c>adaptive</c>
    /// is resolved.
    /// </param>
    /// <param name="loggerFactory">Optional logger factory for the composed pipelines.</param>
    public ProfileRagPipelineResolver(
        IConfiguration configuration,
        Func<RagOptions, IRagPipeline> pipelineFactory,
        Func<IRagPipeline> defaultPipeline,
        Func<IQueryComplexityClassifier>? complexityClassifier = null,
        Func<IChatClient>? chatClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        ArgumentNullException.ThrowIfNull(defaultPipeline);

        _configuration = configuration;
        _pipelineFactory = pipelineFactory;
        _defaultPipeline = new Lazy<IRagPipeline>(defaultPipeline, LazyThreadSafetyMode.ExecutionAndPublication);
        _complexityClassifier = complexityClassifier;
        _chatClient = chatClient;
        _loggerFactory = loggerFactory;
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

        if (profile == RagProfile.Adaptive)
        {
            return _pipelines.GetOrAdd(
                RagProfilePresets.AdaptiveName,
                _ => CreateAdaptivePipeline());
        }

        return _pipelines.GetOrAdd(
            RagProfilePresets.NameOf(profile),
            _ => _pipelineFactory(RagOptionsFactory.Build(_configuration, profileName)));
    }

    /// <summary>
    /// Composes the <c>adaptive</c> routing pipeline: classifier-first, direct
    /// answer on <see cref="QueryRoute.NoRetrieval"/>, delegation to the memoized
    /// <c>balanced</c> pipeline on <see cref="QueryRoute.SingleShot"/>, and the
    /// documented fallback to <c>quality</c> on <see cref="QueryRoute.Iterative"/>
    /// (the corrective engine ships with RAG-06).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No classifier or chat-client accessor was provided — <c>AddOrkeonRag</c>
    /// wires both; direct constructions must pass them to use <c>adaptive</c>.
    /// </exception>
    private AdaptiveRagPipeline CreateAdaptivePipeline()
    {
        if (_complexityClassifier is null || _chatClient is null)
        {
            throw new InvalidOperationException(
                "The 'adaptive' profile needs an IQueryComplexityClassifier and an IChatClient. " +
                "AddOrkeonRag(configuration) wires both (heuristic classifier by default); when " +
                "constructing ProfileRagPipelineResolver directly, pass the complexityClassifier " +
                "and chatClient accessors.");
        }

        return new AdaptiveRagPipeline(
            _complexityClassifier(),
            _chatClient(),
            () => Resolve(RagProfilePresets.BalancedName),
            () => Resolve(RagProfilePresets.QualityName),
            RagProfilePresets.BalancedName,
            RagProfilePresets.QualityName,
            _loggerFactory?.CreateLogger<AdaptiveRagPipeline>());
    }
}
