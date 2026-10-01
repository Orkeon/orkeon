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
    private static readonly string[] s_overridable = ["model", "temperature", "maxTokens", "responseFormat"];

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

        var domain = newModel.IsString() ? Domain with { Model = RequireModel(newModel.AsString()) } : Domain;

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
            newMax.IsNumber() ? (int)newMax.AsNumber() : maxTokens,
            ProfileProvider);
    }

    internal static string RequireModel(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidScriptException("A model name must be a non-empty string.")
            : value;
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
