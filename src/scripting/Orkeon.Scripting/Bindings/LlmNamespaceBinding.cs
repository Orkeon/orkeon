using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>llm</c> namespace on a Jint engine: <c>llm.default_</c>, the host's
/// provider on its own model, <c>llm.model(name, overrides?)</c>, the same provider on another
/// model, <c>llm.profile(name, overrides?)</c>, one of the host's named profiles (GAP-17), and
/// <c>llm.profiles</c>, the names a script may give it (#364). A refusal is a JavaScript throw a
/// script catches (<see cref="JsHostError.Guard{T}"/>, SCR-25).
/// There is no per-vendor factory: a script picks among the providers the host configured, by
/// profile name, and a factory named after a vendor only renamed the host's one (GAP-12).
/// </summary>
internal static class LlmNamespaceBinding
{
    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "llm";

    /// <summary>
    /// Adds <c>llm</c> to <paramref name="engine"/>'s global scope.
    /// </summary>
    /// <param name="engine">Jint engine receiving the binding.</param>
    /// <param name="logger">Diagnostic sink; defaults to <see cref="NullLogger.Instance"/>.</param>
    /// <param name="defaultProvider">
    /// The host's <see cref="ILlmProvider"/> — the one the orchestrator and <c>ctx.llm</c> use.
    /// <c>llm.default_</c> reports its name and its configured model. Absent (or the
    /// <see cref="UndefinedLlmProvider"/> echo), <c>llm.default_</c> is the <c>undefined</c> echo
    /// and a warning says so.
    /// </param>
    /// <param name="profiles">
    /// The host's named LLM profiles behind <c>llm.profile(name)</c>; null offers the default
    /// profile alone.
    /// </param>
    public static void Register(
        Engine engine,
        ILogger? logger = null,
        ILlmProvider? defaultProvider = null,
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? profiles = null) =>
        Register(engine, logger, defaultProvider, profiles, wrapProfileProvider: null);

    /// <summary>The same registration, wrapping the provider of each named profile.</summary>
    /// <param name="engine">Jint engine receiving the binding.</param>
    /// <param name="logger">Diagnostic sink.</param>
    /// <param name="defaultProvider">The host's provider.</param>
    /// <param name="profiles">The host's named LLM profiles.</param>
    /// <param name="wrapProfileProvider">
    /// Wraps the provider of each named profile <c>llm.profile(name)</c> resolves; <c>orkeon run --test</c>
    /// puts the test override there, so a profile answers from <c>test.mockLlm</c> like the default.
    /// </param>
    internal static void Register(
        Engine engine,
        ILogger? logger,
        ILlmProvider? defaultProvider,
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? profiles,
        Func<ILlmProvider, ILlmProvider>? wrapProfileProvider)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var log = logger ?? NullLogger.Instance;

        var ns = new JsLlmNamespace(engine, log, defaultProvider, profiles, wrapProfileProvider);
        engine.SetValue(GlobalName, ns);
        JsLlmNamespace.Attach(engine, ns);
    }
}

/// <summary>
/// Surface exposed to scripts as <c>llm</c>: the lazily resolved <c>default_</c>, the
/// <c>model(...)</c> shorthand, the host profiles of <c>profile(...)</c> and their names in
/// <c>profiles</c>. Every method runs under <see cref="JsHostError.Guard{T}"/>: a CLR exception
/// thrown by a method of a wrapped object unwinds through the script's <c>catch</c> (#364).
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of the "llm" namespace declared in Typings/llm.d.ts; that declaration is the contract scripts read.
internal sealed partial class JsLlmNamespace
{
    private readonly Engine _engine;
    private readonly ILogger _logger;
    private readonly ILlmProvider? _defaultProvider;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? _profiles;
    private readonly Func<ILlmProvider, ILlmProvider>? _wrapProfileProvider;
    private JsLlmConfig? _default;
    private readonly Lock _defaultLock = new();
    private JsValue? _profileNames;

    // The namespace each engine registered, for ctx.llm { llm: { profile } } (#331): held on the CLR
    // side, so a script that shadows the global `llm` cannot change the profiles a call resolves.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Engine, JsLlmNamespace> s_byEngine = new();

    internal JsLlmNamespace(
        Engine engine,
        ILogger logger,
        ILlmProvider? defaultProvider = null,
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? profiles = null,
        Func<ILlmProvider, ILlmProvider>? wrapProfileProvider = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _wrapProfileProvider = wrapProfileProvider;
        _logger = logger;
        _defaultProvider = defaultProvider;
        _profiles = profiles;
    }

    /// <summary>
    /// The host's provider on its own configured model — a value, not a factory. Named
    /// <c>default_</c> because <c>default</c> is a reserved word a TypeScript namespace cannot
    /// declare; the runtime carries the one name the typings can.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "JS-surface name deliberately spelled `default_`: `default` is a reserved word the llm.d.ts namespace cannot declare. Part of the observable scripting API.")]
    public JsLlmConfig default_
    {
        get
        {
            lock (_defaultLock)
                return _default ??= ResolveDefault();
        }
    }

    /// <summary>
    /// <c>llm.model(name, overrides?)</c>: <c>llm.default_</c> on the model <paramref name="name"/>,
    /// with the optional <c>temperature</c> / <c>maxTokens</c> / <c>responseFormat</c> of
    /// <paramref name="overrides"/> — the shorthand of <c>llm.default_.with({ model: name, … })</c>.
    /// </summary>
    public JsLlmConfig model(JsValue name, JsValue? overrides = null) => JsHostError.Guard(_engine, () =>
    {
        if (name is null || !name.IsString())
            throw new Orkeon.Scripting.Exceptions.InvalidScriptException("llm.model(name) takes the model name as a string.");
        var onModel = new JsLlmConfig(default_.provider,
            default_.Domain with { Model = JsLlmConfig.RequireModel(name.AsString()) },
            default_.temperature, default_.maxTokens, hostProvider: default_.Target, engine: _engine);
        return overrides is null ? onModel : onModel.with(overrides);
    });

    /// <summary>
    /// <c>llm.profile(name, overrides?)</c>: the host's profile <paramref name="name"/> — its
    /// provider, on its own model — with the optional <c>model</c> / <c>temperature</c> /
    /// <c>maxTokens</c> / <c>responseFormat</c> of <paramref name="overrides"/> (GAP-17). An agent
    /// configured with it runs its turns, and its <c>ctx.llm</c> calls, on that provider.
    /// <c>"default"</c> is <c>llm.default_</c>.
    /// </summary>
    /// <exception cref="Orkeon.Scripting.Exceptions.InvalidScriptException">
    /// The host offers no such profile; the message lists the known ones.
    /// </exception>
    public JsLlmConfig profile(JsValue name, JsValue? overrides = null) => JsHostError.Guard(_engine, () =>
    {
        if (name is null || !name.IsString() || string.IsNullOrWhiteSpace(name.AsString()))
            throw new Orkeon.Scripting.Exceptions.InvalidScriptException("llm.profile(name) takes the profile name as a string.");

        var profileName = name.AsString().Trim();
        var onProfile = OnProfile(profileName, $"llm.profile(\"{profileName}\")");
        return overrides is null ? onProfile : onProfile.with(overrides);
    });

    /// <summary>
    /// <c>llm.profiles</c> (#364): the names <c>llm.profile(name)</c> takes, <c>"default"</c> first, then
    /// the host's profiles in the order it declared them (an allow-list's refusals left out) - the list
    /// the refusal of an unknown name gives. Names only, never an endpoint or a key. A frozen array,
    /// built at the first read: the host's profiles do not change while the engine lives.
    /// </summary>
    public JsValue profiles => _profileNames ??= JsHostError.Guard(_engine, () =>
    {
        var names = new[] { Orkeon.Application.Interfaces.Ports.LlmProfiles.Default }
            .Concat(_profiles?.Names ?? [])
            .Select(n => (JsValue)new JsString(n))
            .ToArray();
        var array = new JsArray(_engine, names);
        _engine.Invoke(_engine.Intrinsics.Object.Get("freeze"), array);
        return (JsValue)array;
    });

    /// <summary>Records the namespace <paramref name="engine"/> registered.</summary>
    internal static void Attach(Engine engine, JsLlmNamespace ns) => s_byEngine.AddOrUpdate(engine, ns);

    /// <summary>
    /// The provider one <c>ctx.llm</c> call runs on when its options name the profile
    /// <paramref name="profileName"/> (#331), by the rule of <c>llm.profile(name)</c>: the default
    /// profile is the host's provider, another name one of the host's profiles — a decision profile's
    /// provider applies its stages to the call. An engine without the namespace offers the default
    /// alone, which is then <paramref name="fallback"/>: the namespace is what knows the host's provider.
    /// </summary>
    /// <exception cref="Orkeon.Scripting.Exceptions.InvalidScriptException">
    /// The host offers no such profile; the message lists the known ones.
    /// </exception>
    internal static ILlmProvider? ProviderFor(Engine engine, string profileName, string where, ILlmProvider? fallback)
    {
        if (!s_byEngine.TryGetValue(engine, out var ns))
        {
            if (Orkeon.Application.Interfaces.Ports.LlmProfiles.IsDefault(profileName)) return fallback;
            throw new Orkeon.Scripting.Exceptions.InvalidScriptException(
                Orkeon.Application.Interfaces.Ports.LlmProfiles.UnknownMessage(profileName, where, []));
        }

        // The default profile is the host's own provider, null included (the call then answers the
        // <undefined-llm> echo): never the agent's provider, which may be another profile's.
        return Orkeon.Application.Interfaces.Ports.LlmProfiles.IsDefault(profileName)
            ? ns._defaultProvider
            : ns.OnProfile(profileName, where).ProfileProvider ?? ns._defaultProvider;
    }

    private JsLlmConfig OnProfile(string profileName, string where)
    {
        JsLlmConfig onProfile;
        if (Orkeon.Application.Interfaces.Ports.LlmProfiles.IsDefault(profileName))
        {
            onProfile = default_;
        }
        else
        {
            if (_profiles is null || !_profiles.IsKnown(profileName))
                throw new Orkeon.Scripting.Exceptions.InvalidScriptException(
                    Orkeon.Application.Interfaces.Ports.LlmProfiles.UnknownMessage(
                        profileName, where, _profiles?.Names ?? []));

            // The registry builds the profile's provider once per host and caches it; its name and
            // model are what the script reads, and an agent configured with it reaches it through
            // ctx.llm too.
            var resolved = _profiles.Resolve(profileName);
            var profileModel = resolved.Provider.BaseConfig?.Model;
            var domain = (string.IsNullOrWhiteSpace(profileModel) ? LlmConfig.OnProfile() : LlmConfig.Create(profileModel))
                with { Profile = profileName };
            LogResolvedProfile(profileName, resolved.Provider.Name);
            var provider = _wrapProfileProvider is null ? resolved.Provider : _wrapProfileProvider(resolved.Provider);
            onProfile = new JsLlmConfig(resolved.Provider.Name, domain, profileProvider: provider, engine: _engine);
        }

        return onProfile;
    }

    private JsLlmConfig ResolveDefault()
    {
        // The echo provider is recognised under the token meter the host wraps it in.
        if (_defaultProvider is not null
            && Orkeon.Infrastructure.LLMs.MeteredLlmProvider.Unwrap(_defaultProvider) is not UndefinedLlmProvider)
        {
            LogResolvedDefaultFromDi(_defaultProvider.Name);
            // The model only — never the host's credentials or endpoint: this value is visible
            // to the script, and an agent's config carries nothing else the runtime applies. A
            // host provider configured without a model names none here either: the agent runs
            // on that provider's own default, never on OpenAI's (GAP-18).
            var hostModel = _defaultProvider.BaseConfig?.Model;
            return new JsLlmConfig(_defaultProvider.Name,
                string.IsNullOrWhiteSpace(hostModel) ? LlmConfig.OnProfile() : LlmConfig.Create(hostModel),
                hostProvider: _defaultProvider, engine: _engine);
        }

        LogNoDefaultProvider();
        return new JsLlmConfig("undefined", LlmConfig.OnProfile(), engine: _engine);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Resolved llm.profile(\"{Profile}\") to the host provider '{Provider}'.")]
    private partial void LogResolvedProfile(string profile, string provider);

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Resolved llm.default_ from the host's ILlmProvider '{Provider}'.")]
    private partial void LogResolvedDefaultFromDi(string provider);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "No LLM provider is configured for this script: llm.default_ is the <undefined-llm> echo. " +
            "Configure a provider for the host (orkeon init) to silence this warning.")]
    private partial void LogNoDefaultProvider();
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
