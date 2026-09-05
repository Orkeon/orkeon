using Jint;
using Jint.Native;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Script-facing handle on an <see cref="LlmConfig"/>. Tracks the provider name (for
/// downstream resolution) and exposes <c>with({...})</c> for partial overrides.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of LlmConfig in Typings/llm.d.ts; that declaration is the contract scripts read.
public sealed class JsLlmConfig
{
    public string provider { get; }
    public string model { get; }
    public string? apiKey { get; }
    public double? temperature { get; }
    public int? maxTokens { get; }
    public Uri? baseUrl { get; }

    internal LlmConfig Domain { get; }

    internal JsLlmConfig(string provider, LlmConfig domain, double? temperature = null, int? maxTokens = null, Uri? baseUrl = null)
    {
        this.provider = provider;
        Domain = domain;
        model = domain.Model;
        apiKey = null;
        this.temperature = temperature;
        this.maxTokens = maxTokens;
        this.baseUrl = baseUrl;
    }

    public JsLlmConfig with(JsValue overrides)
    {
        if (overrides is null || overrides.IsUndefined() || overrides.IsNull()) return this;
        var newModel = overrides.Get("model");
        var newTemp = overrides.Get("temperature");
        var newMax = overrides.Get("maxTokens");
        var newBase = overrides.Get("baseUrl");
        var newResponseFormat = overrides.Get("responseFormat");

        var domain = newModel.IsString() ? LlmConfig.Create(newModel.AsString()) : Domain;

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
            newBase.IsString() ? new Uri(newBase.AsString()) : baseUrl);
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
