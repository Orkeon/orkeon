using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.LLMs.Profiles;

/// <summary>
/// One named profile a host offers: how to build its provider, and the configuration its chat
/// client merges per-call options into. Registered by
/// <c>AddOrkeonLlmProfiles(configuration)</c> for every <c>Llm:Profiles:&lt;name&gt;</c>, or by
/// <c>AddOrkeonLlmProfile(name, …)</c> for a provider the factory does not build.
/// </summary>
/// <param name="Name">The name a crew references.</param>
/// <param name="Provider">Builds the provider, once, from the container.</param>
/// <param name="BaseConfig">
/// The configuration the profile's chat client starts each call from — its model, key and
/// endpoint; null takes the provider's own.
/// </param>
public sealed record LlmProfileRegistration(
    string Name,
    Func<IServiceProvider, ILlmProvider> Provider,
    LlmConfig? BaseConfig = null)
{
    /// <summary>
    /// The profiles a failed call moves to, in order (FWK-05): the failures a retry cannot cure (a
    /// transport error, a 5xx, a 429 once the retries are spent, a timeout) go to the first, then the
    /// second. A name the host does not define, or a chain that comes back to its own profile, refuses
    /// the registry's construction. Null or empty: no fallback.
    /// </summary>
    public IReadOnlyList<string>? Fallback { get; init; }

    /// <summary>
    /// The longest the chain may spend, from its first call, before it stops trying the next profile; null
    /// bounds it only by each profile's own timeout and retries. Checked between two profiles.
    /// </summary>
    public TimeSpan? FallbackBudget { get; init; }
}

/// <summary>
/// Which of its profiles a host lets crews name. <c>orkeon-host</c> runs crews it does not
/// control, and binds its <c>Orkeon:Host:LlmProfiles</c> allow-list here (GAP-17).
/// </summary>
public sealed class LlmProfileAccessOptions
{
    /// <summary>
    /// The profiles crews may name; null (the default) allows every profile the host defines.
    /// The default profile is always allowed.
    /// </summary>
    public IReadOnlyList<string>? AllowedProfiles { get; set; }
}

/// <summary>
/// The default <see cref="ILlmProfileRegistry"/>: the host's default provider (the container's
/// <see cref="ILlmProvider"/>, <see cref="IBasicLlmProvider"/> and <see cref="IChatClient"/>) plus
/// every <see cref="LlmProfileRegistration"/>, each built on first use, once, and held for the
/// container's lifetime. <c>AddOrkeonLlmProfile</c> meters a registration's provider for the
/// host's <see cref="ILlmUsageSink"/> and limits it by its <see cref="ILlmRateLimiter"/>; the
/// registry does the same to the provider an agent carries itself (<see cref="ForProvider"/>,
/// GAP-34, GAP-38), one of the entrances of the metered path.
/// </summary>
public sealed partial class LlmProfileRegistry : ILlmProfileRegistry
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, LlmProfileRegistration> _offered;
    private readonly Dictionary<string, LlmProfileRegistration> _all;
    private readonly ConcurrentDictionary<string, Lazy<LlmProfile>> _built = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<LlmProfile> _default;

    /// <summary>
    /// The clients of the providers agents carry themselves, one per instance, kept as long as the
    /// instance lives: an agent built for one run takes its client with it.
    /// </summary>
    private readonly ConditionalWeakTable<ILlmProvider, LlmProfile> _ownProviders = new();

    /// <summary>Builds the registry over the container's profile registrations.</summary>
    /// <param name="services">The container the providers are built from.</param>
    /// <param name="registrations">Every profile the host registered.</param>
    /// <param name="access">The host's allow-list; unset allows every registered profile.</param>
    /// <exception cref="InvalidOperationException">
    /// Two registrations share a name, one uses the reserved default name, a <see cref="LlmProfileRegistration.Fallback"/>
    /// names a profile nobody defines, or the fallback chains come back to a profile they started from.
    /// </exception>
    public LlmProfileRegistry(
        IServiceProvider services,
        IEnumerable<LlmProfileRegistration> registrations,
        IOptions<LlmProfileAccessOptions>? access = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);
        _services = services;

        var all = new Dictionary<string, LlmProfileRegistration>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var registration in registrations)
        {
            if (LlmProfiles.IsDefault(registration.Name))
                throw new InvalidOperationException(
                    $"'{LlmProfiles.Default}' is the reserved name of the host's default profile; a profile cannot take it.");
            if (!all.TryAdd(registration.Name, registration))
                throw new InvalidOperationException($"Two LLM profiles are named '{registration.Name}'.");
            order.Add(registration.Name);
        }

        _all = all;
        ValidateFallbacks(all);

        var allowed = access?.Value.AllowedProfiles;
        var allowedSet = allowed is null ? null : new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        Names = [.. order.Where(name => allowedSet is null || allowedSet.Contains(name))];
        _offered = Names.ToDictionary(name => name, name => all[name], StringComparer.OrdinalIgnoreCase);

        _default = new Lazy<LlmProfile>(() => new LlmProfile
        {
            Name = LlmProfiles.Default,
            Provider = _services.GetRequiredService<ILlmProvider>(),
            BasicProvider = _services.GetRequiredService<IBasicLlmProvider>(),
            ChatClient = _services.GetRequiredService<IChatClient>(),
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; }

    /// <inheritdoc />
    public bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return LlmProfiles.IsDefault(name) || _offered.ContainsKey(name.Trim());
    }

    /// <inheritdoc />
    public LlmProfile Resolve(string? name)
    {
        if (name is null || LlmProfiles.IsDefault(name))
            return _default.Value;

        var key = name.Trim();
        if (!_offered.TryGetValue(key, out var registration))
            throw new InvalidOperationException(LlmProfiles.UnknownMessage(key, "A crew", Names));

        return _built.GetOrAdd(registration.Name, _ => new Lazy<LlmProfile>(() => Build(registration))).Value;
    }

    /// <inheritdoc />
    public LlmProfile ForProvider(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _ownProviders.GetValue(provider, own =>
            // The agent's own provider is the host's own code, built off the metered path — by
            // AgentBuilder.WithLlm, or by WithAgentFrameworkAgent: metered and limited here, where the
            // run resolves it, as the agent's work; a provider metered or limited already is left as it is.
            Assemble(ManagerLlm.ProviderPrefix + own.Name,
                LlmProviderEntrance.Enter(own, _services.GetService<ILlmUsageSink>(), _services.GetService<ILlmRateLimiter>(), _services.GetService<ICostBudgetManager>()),
                baseConfig: null));
    }

    private LlmProfile Build(LlmProfileRegistration registration)
    {
        // Metered and limited by its registration (AddOrkeonLlmProfile), one of the entrances of the
        // metered path — a second meter or limiter here would count every call twice.
        var own = Assemble(registration.Name, registration.Provider(_services), registration.BaseConfig);
        if (registration.Fallback is not { Count: > 0 } fallback)
            return own;

        // The chain sits above profiles already entered and assembled, each with its own call mode,
        // meter and trace: the metric and the span name the provider that answered (FWK-05).
        var links = new List<FallbackLink>
        {
            new(registration.Name, own.Provider, registration.BaseConfig ?? own.Provider.BaseConfig),
        };
        foreach (var name in fallback.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = ResolveForChain(name, out var targetConfig);
            links.Add(new FallbackLink(target.Name, target.Provider, targetConfig));
        }

        WarnAboutLesserFallbacks(registration.Name, links);
        var chain = new FallbackLlmProvider(
            registration.Name,
            links,
            _services.GetService<ILlmFallbackObserver>(),
            Logger(),
            registration.FallbackBudget,
            _services.GetService<TimeProvider>() ?? TimeProvider.System);
        return Assemble(registration.Name, chain, registration.BaseConfig);
    }

    /// <summary>A profile a chain falls back on: any the host defines, hidden from crews by the allow-list or not.</summary>
    private LlmProfile ResolveForChain(string name, out LlmConfig? baseConfig)
    {
        if (LlmProfiles.IsDefault(name))
        {
            baseConfig = null;
            return _default.Value;
        }

        var registration = _all[name];
        baseConfig = registration.BaseConfig;
        return _built.GetOrAdd(registration.Name, _ => new Lazy<LlmProfile>(() => Build(registration))).Value;
    }

    private ILogger Logger() =>
        (_services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger<LlmProfileRegistry>();

    /// <summary>
    /// Refuses, when the registry is built, a fallback that names a profile nobody defines and a chain
    /// that comes back to a profile it started from. Built later it would recurse inside the lazy
    /// resolution of the profile; <c>CrewFactory</c> sees only the names crews reference, not the profiles
    /// between them.
    /// </summary>
    private static void ValidateFallbacks(Dictionary<string, LlmProfileRegistration> all)
    {
        foreach (var registration in all.Values)
        {
            foreach (var name in registration.Fallback ?? [])
            {
                if (!LlmProfiles.IsDefault(name.Trim()) && !all.ContainsKey(name.Trim()))
                    throw new InvalidOperationException(
                        $"The Fallback of LLM profile '{registration.Name}' names '{name.Trim()}', which no profile defines. " +
                        $"Profiles: {(all.Count == 0 ? "none" : string.Join(", ", all.Keys.Order(StringComparer.OrdinalIgnoreCase)))}; " +
                        $"'{LlmProfiles.Default}' names the host's own.");
            }
        }

        foreach (var registration in all.Values)
        {
            var path = new List<string> { registration.Name };
            FindCycle(all, registration, path);
        }
    }

    private static void FindCycle(Dictionary<string, LlmProfileRegistration> all, LlmProfileRegistration current, List<string> path)
    {
        foreach (var name in (current.Fallback ?? []).Select(n => n.Trim()))
        {
            if (!all.TryGetValue(name, out var next))
                continue;

            var seen = path.FindIndex(p => string.Equals(p, next.Name, StringComparison.OrdinalIgnoreCase));
            if (seen >= 0)
                throw new InvalidOperationException(
                    $"The Fallback chains of LLM profiles come back to a profile they started from: {string.Join(" -> ", path.Skip(seen).Append(next.Name))}. " +
                    "A profile cannot be its own fallback, directly or through another.");

            path.Add(next.Name);
            FindCycle(all, next, path);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>
    /// Warns, once per chain, about a fallback that declares less than the profile it replaces: an option
    /// the first profile honours and the fallback cannot is dropped there, never silently.
    /// </summary>
    private void WarnAboutLesserFallbacks(string profile, List<FallbackLink> links)
    {
        var primary = links[0].Provider.Capabilities;
        var table = _services.GetService<IModelCapabilityRegistry>() ?? ModelCapabilityRegistry.Default;
        var primaryModel = table.TryGet(ModelOf(links[0]));
        foreach (var link in links.Skip(1))
        {
            var lacks = new List<string>();
            var capabilities = link.Provider.Capabilities;
            if (primary.Vision && !capabilities.Vision)
                lacks.Add("vision");
            if (capabilities.ResponseFormat < primary.ResponseFormat)
                lacks.Add($"response format ({primary.ResponseFormat} -> {capabilities.ResponseFormat})");
            if (primary.GbnfGrammar && !capabilities.GbnfGrammar)
                lacks.Add("GBNF grammar");
            AddModelLacks(lacks, primary, primaryModel, capabilities, table.TryGet(ModelOf(link)));

            if (lacks.Count > 0)
                LogLesserFallback(Logger(), link.Name, profile, string.Join(", ", lacks));
        }
    }

    /// <summary>The model a chain link runs on, when its configuration names one.</summary>
    private static string? ModelOf(FallbackLink link) =>
        link.BaseConfig?.Model is { Length: > 0 } own ? own : link.Provider.BaseConfig?.Model;

    /// <summary>
    /// The per-model side of the comparison (FWK-21, #356): audio input from the table, and reasoning from the
    /// effective values each call is judged against - the provider's declaration narrowed by the model's entry.
    /// A fallback model the table does not know takes no audio (nothing sends it any).
    /// </summary>
    private static void AddModelLacks(
        List<string> lacks,
        LlmProviderCapabilities primaryProvider, ModelCapabilities? primary,
        LlmProviderCapabilities fallbackProvider, ModelCapabilities? fallback)
    {
        if (primary is { AudioInput: true } && fallback is not { AudioInput: true })
            lacks.Add("audio input");

        var primaryThinking = EffectiveThinking(primaryProvider, primary);
        var fallbackThinking = EffectiveThinking(fallbackProvider, fallback);
        if (fallbackThinking < primaryThinking)
        {
            lacks.Add(fallbackThinking == ThinkingSupport.None
                ? "thinking"
                : $"thinking ({primaryThinking} -> {fallbackThinking})");
        }

        if (fallbackThinking == ThinkingSupport.None)
            return;

        var missing = EffectiveEfforts(primaryProvider, primary, primaryThinking)
            .Except(EffectiveEfforts(fallbackProvider, fallback, fallbackThinking), StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
            lacks.Add($"reasoning effort ({string.Join(", ", missing)})");
    }

    /// <summary>The lower of the provider's thinking support and the model's, as <c>ReasoningJudgement</c> takes it.</summary>
    private static ThinkingSupport EffectiveThinking(LlmProviderCapabilities provider, ModelCapabilities? model)
        => model?.Thinking is { } narrowed && narrowed < provider.Thinking ? narrowed : provider.Thinking;

    /// <summary>
    /// The efforts a call can send: the model's list narrowed to the levels the provider carries (a vendor value
    /// outside <see cref="LlmThinkingConfig.Levels"/> kept), or the provider's levels for a model the table does not know.
    /// </summary>
    private static IEnumerable<string> EffectiveEfforts(LlmProviderCapabilities provider, ModelCapabilities? model, ThinkingSupport thinking)
    {
        if (thinking == ThinkingSupport.None)
            return [];
        if (model?.ReasoningEfforts is not { } listed)
            return provider.EffortLevels;
        return listed.Where(effort => !LlmThinkingConfig.Levels.Contains(effort) || provider.EffortLevels.Contains(effort));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fallback profile '{Fallback}' of '{Profile}' does not support {Missing}, which '{Profile}' does: an option that needs it is dropped when a call lands on it.")]
    private static partial void LogLesserFallback(ILogger logger, string fallback, string profile, string missing);

    /// <summary>
    /// Builds a profile's three surfaces over one provider — the shape
    /// <c>AddOrkeonLlmProvider</c> gives the default — with the vendor's native tool-call
    /// parser when the provider is Anthropic, as the infrastructure default chat client does.
    /// </summary>
    private LlmProfile Assemble(string name, ILlmProvider provider, LlmConfig? baseConfig)
    {
        var nativeParser = MeteredLlmProvider.Unwrap(provider) is AnthropicLlmProvider
            ? new AnthropicToolCallParser()
            : null;

        return new LlmProfile
        {
            Name = name,
            Provider = provider,
            BasicProvider = new LlmProviderAdapter(provider),
            ChatClient = new LlmProviderToChatClientAdapter(
                provider,
                baseConfig ?? provider.BaseConfig,
                textFallbackParser: _services.GetService<IToolCallParser>(),
                nativeToolCallParser: nativeParser),
        };
    }
}
