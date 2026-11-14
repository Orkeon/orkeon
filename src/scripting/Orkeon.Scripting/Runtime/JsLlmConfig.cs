using Jint;
using Jint.Native;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Script-facing handle on the model settings an agent runs with: what <c>llm.default_</c>,
/// <c>llm.model(...)</c> and <c>with(...)</c> return, and the only value <c>agentBuilder().llm(...)</c>
/// accepts.
/// </summary>
/// <remarks>
/// <c>provider</c> is informational — the name of the provider the configuration talks to: the
/// host's default one, or the one of the host profile <c>llm.profile(name)</c> picked (GAP-17).
/// A script never names a vendor, a key or an endpoint: the per-vendor factories were removed
/// (GAP-12), and a profile is what the host configured. <c>with(...)</c> refuses a key it does
/// not apply, so a setting never vanishes. Its refusal is a JavaScript throw a script catches
/// (<see cref="JsHostError.Guard{T}"/>, #364) on a configuration the <c>llm</c> namespace of an engine
/// built; one a CLR caller builds without an engine throws the CLR exception.
/// </remarks>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of LlmConfig in Typings/llm.d.ts; that declaration is the contract scripts read.
public sealed class JsLlmConfig
{
    private static readonly string[] s_overridable = ["model", "temperature", "maxTokens", "responseFormat", "effort", "cache"];

    public string provider { get; }
    public string model { get; }
    public string? profile => Domain.Profile;
    public double? temperature { get; }
    public int? maxTokens { get; }

    internal LlmConfig Domain { get; }

    /// <summary>
    /// The provider of the profile this configuration runs on, for <c>ctx.llm</c> of a procedural
    /// agent configured with it; null on the host's default profile.
    /// </summary>
    internal Orkeon.Domain.SharedKernel.ILlmProvider? ProfileProvider { get; }

    /// <summary>
    /// The provider a call on this configuration talks to: the profile's, else the host's default one; null when
    /// there is none. <c>capabilities</c> and <c>reasoning</c> ask it, so they judge as the call will.
    /// </summary>
    internal Orkeon.Domain.SharedKernel.ILlmProvider? Target => ProfileProvider ?? _hostProvider;

    private readonly Orkeon.Domain.SharedKernel.ILlmProvider? _hostProvider;

    private readonly Engine? _engine;

    /// <summary>
    /// What the provider and this configuration's model take for reasoning (#359): the effort levels, the thinking
    /// support, and whether the capability table knows the model. Null when the provider cannot tell.
    /// </summary>
    public JsLlmCapabilities? capabilities => JsLlmCapabilities.From(Target?.DescribeReasoning(Domain.Model));

    /// <summary>
    /// The effort a call on this configuration asks for and the one the provider sends (#360), the host's
    /// <c>Llm:Thinking:Effort</c> included. Null when no effort is asked or the provider cannot tell.
    /// </summary>
    public JsLlmReasoning? reasoning => JsLlmReasoning.From(Target?.ReportEffort(Domain));

    internal JsLlmConfig(
        string provider,
        LlmConfig domain,
        double? temperature = null,
        int? maxTokens = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? profileProvider = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? hostProvider = null,
        Engine? engine = null)
    {
        _engine = engine;
        this.provider = provider;
        Domain = domain;
        ProfileProvider = profileProvider;
        _hostProvider = hostProvider;
        model = domain.Model;
        this.temperature = temperature;
        this.maxTokens = maxTokens;
    }

    public JsLlmConfig with(JsValue overrides) =>
        _engine is null ? WithCore(overrides) : JsHostError.Guard(_engine, () => WithCore(overrides));

    private JsLlmConfig WithCore(JsValue overrides)
    {
        if (overrides is null || overrides.IsUndefined() || overrides.IsNull()) return this;
        if (!overrides.IsObject())
            throw new InvalidScriptException("LlmConfig.with(overrides) expects an object.");

        foreach (var key in overrides.AsObject().GetOwnPropertyKeys())
        {
            var name = key.ToString();
            if (Array.IndexOf(s_overridable, name) < 0)
                throw new InvalidScriptException(
                    $"LlmConfig.with(...) does not apply '{name}'. It takes: {string.Join(", ", s_overridable)}.");
        }

        var newModel = overrides.Get("model");
        var newTemp = overrides.Get("temperature");
        var newMax = overrides.Get("maxTokens");
        var newResponseFormat = overrides.Get("responseFormat");

        var newEffort = overrides.Get("effort");

        var domain = newModel.IsString() ? Domain with { Model = RequireModel(newModel.AsString()) } : Domain;

        if (newEffort.IsString())
            domain = domain with { Thinking = ThinkingAt(newEffort.AsString()) };

        var newCache = overrides.Get("cache");
        if (!newCache.IsUndefined())
            domain = domain with { Cache = CacheFrom(newCache, "LlmConfig.with({ cache })") };

        // ResponseFormat sits on Domain (consumed by adapter → provider). 'text' = clear.
        if (newResponseFormat.IsString())
        {
            var rf = newResponseFormat.AsString();
            domain = domain with
            {
                ResponseFormat = string.Equals(rf, "text", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new LlmResponseFormat { Type = rf },
            };
        }

        return new JsLlmConfig(
            provider,
            domain,
            newTemp.IsNumber() ? newTemp.AsNumber() : temperature,
            newMax.IsUndefined() ? maxTokens : RequireMaxTokens(newMax),
            ProfileProvider,
            _hostProvider,
            _engine);
    }

    private static int RequireMaxTokens(JsValue value)
    {
        if (value.IsNumber())
        {
            var n = value.AsNumber();
            if (n >= 1 && n <= int.MaxValue && double.IsInteger(n)) return (int)n;
        }
        throw new InvalidScriptException(
            "LlmConfig.with(...) maxTokens must be an integer between 1 and 2147483647; omit it to keep the inherited cap.");
    }

    /// <summary>The thinking setting every <c>effort</c> option builds: enabled, at the validated level.</summary>
    internal static LlmThinkingConfig ThinkingAt(string? effort)
        => new() { Enabled = true, Effort = RequireEffort(effort) };

    internal static string RequireEffort(string? value)
        => value is not null && LlmThinkingConfig.Levels.Contains(value)
            ? value
            : throw new InvalidScriptException(
                $"effort must be {string.Join('|', LlmThinkingConfig.Levels)}, not '{value}'.");

    private static readonly string[] s_cacheKeys = ["systemPrompt", "tools", "ttl"];

    /// <summary>
    /// The prompt-cache request a script's <c>cache</c> option describes, mapped by
    /// <see cref="LlmCacheConfig.Request"/> as the YAML <c>cache:</c> block is:
    /// <see cref="LlmCacheConfig.Off"/> when it marks nothing cacheable, which turns an inherited
    /// cache - the host default <c>Llm:Cache</c> included - off.
    /// </summary>
    /// <param name="value">The <c>{ systemPrompt?, tools?, ttl? }</c> object.</param>
    /// <param name="where">The call that received it, for the refusal.</param>
    internal static LlmCacheConfig CacheFrom(JsValue? value, string where)
    {
        if (value is null || !value.IsObject() || value.IsArray())
            throw new InvalidScriptException($"{where} takes an object {{ systemPrompt?, tools?, ttl? }}.");

        var cache = value.AsObject();
        foreach (var key in cache.GetOwnPropertyKeys())
        {
            var name = key.ToString();
            if (Array.IndexOf(s_cacheKeys, name) < 0)
                throw new InvalidScriptException(
                    $"{where} does not apply '{name}'. It takes: {string.Join(", ", s_cacheKeys)}.");
        }

        var systemPrompt = RequireFlag(cache.Get("systemPrompt"), where, "systemPrompt");
        var tools = RequireFlag(cache.Get("tools"), where, "tools");
        var ttlValue = cache.Get("ttl");
        string? ttl = null;
        if (!ttlValue.IsUndefined())
        {
            ttl = ttlValue.IsString()
                ? ttlValue.AsString()
                : throw new InvalidScriptException(
                    $"{where} ttl must be {string.Join(" or ", LlmCacheConfig.Ttls)}; leave it out for the vendor default.");
            if (!systemPrompt && !tools)
                throw new InvalidScriptException(
                    $"{where} ttl caches nothing on its own: set systemPrompt or tools to true.");
        }

        try
        {
            return LlmCacheConfig.Request(systemPrompt, tools, ttl) ?? LlmCacheConfig.Off;
        }
        catch (ArgumentException ex)
        {
            throw new InvalidScriptException($"{where}: {ex.Message}");
        }
    }

    private static bool RequireFlag(JsValue value, string where, string name)
    {
        if (value.IsUndefined()) return false;
        if (value.IsBoolean()) return value.AsBoolean();
        throw new InvalidScriptException($"{where} {name} must be true or false.");
    }

    internal static string RequireModel(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidScriptException("A model name must be a non-empty string.")
            : value;
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
