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
/// <c>provider</c> is informational — the name of the host's provider, which every agent talks
/// to. A script cannot choose another one (GAP-12): the per-vendor factories that pretended to
/// were removed. <c>with(...)</c> refuses a key it does not apply, so a setting never vanishes.
/// </remarks>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of LlmConfig in Typings/llm.d.ts; that declaration is the contract scripts read.
public sealed class JsLlmConfig
{
    private static readonly string[] s_overridable = ["model", "temperature", "maxTokens", "responseFormat"];

    public string provider { get; }
    public string model { get; }
    public double? temperature { get; }
    public int? maxTokens { get; }

    internal LlmConfig Domain { get; }

    internal JsLlmConfig(string provider, LlmConfig domain, double? temperature = null, int? maxTokens = null)
    {
        this.provider = provider;
        Domain = domain;
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
            newMax.IsNumber() ? (int)newMax.AsNumber() : maxTokens);
    }

    internal static string RequireModel(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidScriptException("A model name must be a non-empty string.")
            : value;
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
