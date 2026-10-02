using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>llm</c> namespace on a Jint engine: <c>llm.default_</c>, the host's
/// provider on its own model, <c>llm.model(name, overrides?)</c>, the same provider on another
/// model, and <c>llm.profile(name, overrides?)</c>, one of the host's named profiles (GAP-17).
/// There is no per-vendor factory: a script picks among the providers the host configured, by
/// profile name, and a factory named after a vendor only renamed the host's one (GAP-12).
/// </summary>
public static class LlmNamespaceBinding
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
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var log = logger ?? NullLogger.Instance;

        var ns = new JsLlmNamespace(log, defaultProvider, profiles);
        engine.SetValue(GlobalName, ns);
    }
}

/// <summary>
/// Surface exposed to scripts as <c>llm</c>: the lazily resolved <c>default_</c>, the
/// <c>model(...)</c> shorthand and the host profiles of <c>profile(...)</c>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of the "llm" namespace declared in Typings/llm.d.ts; that declaration is the contract scripts read.
public sealed partial class JsLlmNamespace
{
    private readonly ILogger _logger;
    private readonly ILlmProvider? _defaultProvider;
    private readonly Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? _profiles;
    private JsLlmConfig? _default;
    private readonly Lock _defaultLock = new();

    internal JsLlmNamespace(
        ILogger logger,
        ILlmProvider? defaultProvider = null,
        Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry? profiles = null)
    {
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
    public JsLlmConfig model(JsValue name, JsValue? overrides = null)
    {
        if (name is null || !name.IsString())
            throw new Orkeon.Scripting.Exceptions.InvalidScriptException("llm.model(name) takes the model name as a string.");
        var onModel = new JsLlmConfig(default_.provider,
            default_.Domain with { Model = JsLlmConfig.RequireModel(name.AsString()) },
            default_.temperature, default_.maxTokens);
        return overrides is null ? onModel : onModel.with(overrides);
    }

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
    public JsLlmConfig profile(JsValue name, JsValue? overrides = null)
    {
        if (name is null || !name.IsString() || string.IsNullOrWhiteSpace(name.AsString()))
            throw new Orkeon.Scripting.Exceptions.InvalidScriptException("llm.profile(name) takes the profile name as a string.");

        var profileName = name.AsString().Trim();
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
                        profileName, $"llm.profile(\"{profileName}\")", _profiles?.Names ?? []));

            // The provider is built here, once per host: its name and model are what the script
            // reads, and an agent configured with it reaches it through ctx.llm too.
            var resolved = _profiles.Resolve(profileName);
            var profileModel = resolved.Provider.BaseConfig?.Model;
            var domain = (string.IsNullOrWhiteSpace(profileModel) ? LlmConfig.OnProfile() : LlmConfig.Create(profileModel))
                with { Profile = profileName };
            LogResolvedProfile(profileName, resolved.Provider.Name);
            onProfile = new JsLlmConfig(resolved.Provider.Name, domain, profileProvider: resolved.Provider);
        }

        return overrides is null ? onProfile : onProfile.with(overrides);
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
                string.IsNullOrWhiteSpace(hostModel) ? LlmConfig.OnProfile() : LlmConfig.Create(hostModel));
        }

        LogNoDefaultProvider();
        return new JsLlmConfig("undefined", LlmConfig.OnProfile());
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
