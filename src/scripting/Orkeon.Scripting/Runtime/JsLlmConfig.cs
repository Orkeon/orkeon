using Jint;
using Jint.Native;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Exceptions;

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
/// not apply, so a setting never vanishes.
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

    internal JsLlmConfig(
        string provider,
        LlmConfig domain,
        double? temperature = null,
        int? maxTokens = null,
        Orkeon.Domain.SharedKernel.ILlmProvider? profileProvider = null)
    {
        this.provider = provider;
        Domain = domain;
        ProfileProvider = profileProvider;
        model = domain.Model;
        this.temperature = temperature;
        this.maxTokens = maxTokens;
    }

    public JsLlmConfig with(JsValue overrides)
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
            ProfileProvider);
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
    /// <see cref="LlmCacheConfig.Request"/> as the YAML <c>cache:</c> block is: null when it marks
    /// nothing cacheable, which turns an inherited cache off.
    /// </summary>
    /// <param name="value">The <c>{ systemPrompt?, tools?, ttl? }</c> object.</param>
    /// <param name="where">The call that received it, for the refusal.</param>
    internal static LlmCacheConfig? CacheFrom(JsValue? value, string where)
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
            return LlmCacheConfig.Request(systemPrompt, tools, ttl);
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
