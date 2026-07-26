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
/// <c>balanced</c>/<c>corrective</c> pipelines; <c>corrective</c> (RAG-06)
/// resolves to the corrective graph pipeline through the dedicated factory
/// (wired by <c>AddOrkeonCorrectiveRag</c> — absent, resolution fails loudly);
/// <c>default</c> (the harness's implicit profile) resolves to the host's
/// registered <c>IRagPipeline</c> — so a host-provided pipeline keeps the last
/// word for non-profiled queries. Unknown names fail loudly with the list of
/// known profiles.
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
    private readonly Func<RagOptions, IRagPipeline>? _correctivePipelineFactory;
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
    /// <param name="correctivePipelineFactory">
    /// Builds the corrective graph pipeline (RAG-06) from the fully composed
    /// <c>corrective</c> preset options. Required only when <c>corrective</c>
    /// (directly, or through the <c>adaptive</c> profile's <c>Iterative</c>
    /// route) is resolved — <c>AddOrkeonRag</c>/<c>AddOrkeonCorrectiveRag</c>
    /// wire it.
    /// </param>
    /// <param name="loggerFactory">Optional logger factory for the composed pipelines.</param>
    public ProfileRagPipelineResolver(
        IConfiguration configuration,
        Func<RagOptions, IRagPipeline> pipelineFactory,
        Func<IRagPipeline> defaultPipeline,
        Func<IQueryComplexityClassifier>? complexityClassifier = null,
        Func<IChatClient>? chatClient = null,
        Func<RagOptions, IRagPipeline>? correctivePipelineFactory = null,
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
        _correctivePipelineFactory = correctivePipelineFactory;
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

        if (profile == RagProfile.Corrective)
        {
            return _pipelines.GetOrAdd(
                RagProfilePresets.CorrectiveName,
                _ => CreateCorrectivePipeline());
        }

        return _pipelines.GetOrAdd(
            RagProfilePresets.NameOf(profile),
            _ => _pipelineFactory(RagOptionsFactory.Build(_configuration, profileName)));
    }

    /// <summary>
    /// Composes the <c>adaptive</c> routing pipeline: classifier-first, direct
    /// answer on <see cref="QueryRoute.NoRetrieval"/>, delegation to the memoized
    /// <c>balanced</c> pipeline on <see cref="QueryRoute.SingleShot"/>, and
    /// delegation to the memoized <c>corrective</c> graph pipeline on
    /// <see cref="QueryRoute.Iterative"/> (since RAG-06 — the former documented
    /// fallback to <c>quality</c> is lifted).
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
            () => Resolve(RagProfilePresets.CorrectiveName),
            RagProfilePresets.BalancedName,
            RagProfilePresets.CorrectiveName,
            _loggerFactory?.CreateLogger<AdaptiveRagPipeline>());
    }

    /// <summary>
    /// Composes the <c>corrective</c> graph pipeline (RAG-06) from the
    /// <c>corrective</c> preset overridden by the <c>Orkeon:Rag</c> configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No corrective factory was provided — the corrective graph services are an
    /// opt-in: call <c>AddOrkeonCorrectiveRag(services, configuration)</c>
    /// (<c>AddOrkeonRag(configuration)</c> does it for you); when constructing
    /// ProfileRagPipelineResolver directly, pass the
    /// <c>correctivePipelineFactory</c> accessor.
    /// </exception>
    private IRagPipeline CreateCorrectivePipeline()
    {
        if (_correctivePipelineFactory is null)
        {
            throw new InvalidOperationException(
                "The 'corrective' profile needs the corrective graph services (RAG-06). " +
                "Opt in with AddOrkeonCorrectiveRag(services, configuration) — " +
                "AddOrkeonRag(configuration) wires it for you; when constructing " +
                "ProfileRagPipelineResolver directly, pass the correctivePipelineFactory accessor.");
        }

        return _correctivePipelineFactory(
            RagOptionsFactory.Build(_configuration, RagProfilePresets.CorrectiveName));
    }
}
