using Jint;
using Jint.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Registers the global <c>llm</c> namespace on a Jint engine. Exposes provider
/// factories (<c>llm.openai</c>, <c>llm.anthropic</c>, <c>llm.ollama</c>,
/// <c>llm.azureOpenai</c>, <c>llm.grok</c>) and the resolved <c>llm.default</c>
/// derived from the DI-bound <see cref="ILlmProvider"/> (preferred) or from the
/// <c>Orkeon:DefaultLlmProvider</c> configuration key (fallback).
/// </summary>
public static class LlmNamespaceBinding
{
    /// <summary>Configuration key resolving the default provider name.</summary>
    public const string DefaultProviderConfigKey = "Orkeon:DefaultLlmProvider";

    /// <summary>Name of the global namespace exposed to scripts.</summary>
    public const string GlobalName = "llm";

    /// <summary>
    /// Adds <c>llm</c> to <paramref name="engine"/>'s global scope.
    /// </summary>
    /// <param name="engine">Jint engine receiving the binding.</param>
    /// <param name="config">Host configuration (used for the <see cref="DefaultProviderConfigKey"/> fallback).</param>
    /// <param name="logger">Diagnostic sink; defaults to <see cref="NullLogger.Instance"/>.</param>
    /// <param name="defaultProvider">
    /// Optional DI-bound <see cref="ILlmProvider"/>. When provided (and not the
    /// <see cref="UndefinedLlmProvider"/> echo), it becomes the primary source for
    /// <c>llm.default_</c> — the script-visible provider name matches the runtime
    /// provider, so the orchestrator and the scripted task share one source of
    /// truth instead of drifting from each other.
    /// </param>
    public static void Register(
        Engine engine,
        IConfiguration? config = null,
        ILogger? logger = null,
        ILlmProvider? defaultProvider = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var log = logger ?? NullLogger.Instance;

        var ns = new JsLlmNamespace(config, log, defaultProvider);
        engine.SetValue(GlobalName, ns);
    }
}

/// <summary>
/// Surface exposed to scripts as <c>llm</c>. Holds factories for built-in providers
/// plus a lazily resolved <c>default</c>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed partial class JsLlmNamespace
{
    private readonly IConfiguration? _config;
    private readonly ILogger _logger;
    private readonly ILlmProvider? _defaultProvider;
    private JsLlmConfig? _default;
    private readonly object _defaultLock = new();

    // SML-009/R12.5: single source for the scripting default models (they were duplicated
    // verbatim between the provider getters and BuildFromProviderName below). NOTE: these
    // intentionally differ from Infrastructure ProviderDefaults (e.g. gpt-4o-mini vs gpt-4,
    // claude-haiku-4-5 vs claude-3-5-sonnet) — the scripting DSL favours the newer, cheaper
    // models. Reconciling the two default sets (YAML vs scripting) is a product decision left
    // to the maintainer; until then this keeps the scripting side defined in exactly one place.
    private const string OpenAiDefaultModel = "gpt-4o-mini";
    private const string AnthropicDefaultModel = "claude-haiku-4-5";
    private const string OllamaDefaultModel = "llama3";
    private const string AzureOpenAiDefaultModel = "gpt-4o-mini";
    private const string GrokDefaultModel = "grok-4.6";

    internal JsLlmNamespace(IConfiguration? config, ILogger logger, ILlmProvider? defaultProvider = null)
    {
        _config = config;
        _logger = logger;
        _defaultProvider = defaultProvider;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — exposed as llm.openai on the global `llm` object set via engine.SetValue.")]
    public Func<JsValue?, JsLlmConfig> openai => opts => Build("openai", opts, OpenAiDefaultModel);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — exposed as llm.anthropic on the global `llm` object set via engine.SetValue.")]
    public Func<JsValue?, JsLlmConfig> anthropic => opts => Build("anthropic", opts, AnthropicDefaultModel);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — exposed as llm.ollama on the global `llm` object set via engine.SetValue.")]
    public Func<JsValue?, JsLlmConfig> ollama => opts => Build("ollama", opts, OllamaDefaultModel);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — exposed as llm.azureOpenai on the global `llm` object set via engine.SetValue.")]
    public Func<JsValue?, JsLlmConfig> azureOpenai => opts => Build("azureOpenai", opts, AzureOpenAiDefaultModel);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "instance member required by the Jint JS binding surface — exposed as llm.grok on the global `llm` object set via engine.SetValue.")]
    public Func<JsValue?, JsLlmConfig> grok => opts => Build("grok", opts, GrokDefaultModel);

    public JsLlmConfig @default
    {
        get
        {
            if (_default is not null) return _default;
            lock (_defaultLock)
            {
                if (_default is not null) return _default;
                _default = ResolveDefault();
                return _default;
            }
        }
    }

    /// <summary>
    /// Alias for <see cref="@default"/> exposed under the JS name <c>default_</c>. The
    /// <c>orkeon-script</c> <c>.d.ts</c> rolls up <c>llm.default_</c> (typed twin used
    /// in <c>.ork.ts</c> files to side-step authoring conflicts with the <c>default</c>
    /// reserved word), so the runtime surface mirrors it. Both names point at the same
    /// lazily-resolved <see cref="JsLlmConfig"/> — no duplicated state.
    /// </summary>
#pragma warning disable IDE1006
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "JS-surface alias deliberately named `default_` to side-step the `default` reserved word in .ork.ts authoring; the name mirrors the generated orkeon-script .d.ts and is part of the observable scripting API.")]
    public JsLlmConfig default_ => @default;
#pragma warning restore IDE1006

    private JsLlmConfig ResolveDefault()
    {
        // Precedence:
        //   1. Explicit config key (Orkeon:DefaultLlmProvider) — operator-set,
        //      back-compat with existing deployments that pin the script's view
        //      of the default to a logical name independent of DI.
        //   2. DI-bound ILlmProvider, excluding the UndefinedLlmProvider echo.
        //   3. UndefinedLlm echo + warning.
        var configuredName = _config?[LlmNamespaceBinding.DefaultProviderConfigKey];
        if (!string.IsNullOrWhiteSpace(configuredName))
            return BuildFromProviderName(configuredName);

        if (_defaultProvider is not null && _defaultProvider is not UndefinedLlmProvider)
        {
            LogResolvedDefaultFromDi(_defaultProvider.Name);
            return new JsLlmConfig(_defaultProvider.Name, LlmConfig.Default());
        }

        LogNoDefaultProvider(LlmNamespaceBinding.DefaultProviderConfigKey);
        return new JsLlmConfig("undefined", LlmConfig.Default());
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Resolved llm.default from DI-bound ILlmProvider '{Provider}'.")]
    private partial void LogResolvedDefaultFromDi(string provider);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "No default LLM provider configured. Using UndefinedLlm (echo). " +
            "Configure '{Key}' or register an ILlmProvider in DI to silence this warning.")]
    private partial void LogNoDefaultProvider(string key);

    private static JsLlmConfig BuildFromProviderName(string providerName) =>
#pragma warning disable CA1308 // normalized key for a switch; lowercase is the required form, not a comparison normalization
        providerName.ToLowerInvariant() switch
        {
            "openai" => Build("openai", null, OpenAiDefaultModel),
            "anthropic" => Build("anthropic", null, AnthropicDefaultModel),
            "ollama" => Build("ollama", null, OllamaDefaultModel),
            "azureopenai" => Build("azureOpenai", null, AzureOpenAiDefaultModel),
            "grok" => Build("grok", null, GrokDefaultModel),
            _ => new JsLlmConfig(providerName, LlmConfig.Default()),
        };
#pragma warning restore CA1308

    private static JsLlmConfig Build(string providerName, JsValue? options, string defaultModel)
    {
        var model = defaultModel;
        double? temperature = null;
        int? maxTokens = null;
        string? baseUrl = null;
        if (options is not null && options.IsObject())
        {
            var m = options.Get("model"); if (m.IsString()) model = m.AsString();
            var t = options.Get("temperature"); if (t.IsNumber()) temperature = t.AsNumber();
            var mt = options.Get("maxTokens"); if (mt.IsNumber()) maxTokens = (int)mt.AsNumber();
            var b = options.Get("baseUrl"); if (b.IsString()) baseUrl = b.AsString();
        }
        return new JsLlmConfig(providerName, LlmConfig.Create(model), temperature, maxTokens, baseUrl is null ? null : new Uri(baseUrl));
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
