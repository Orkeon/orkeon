using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Writes the test completion of the connection probe (STUDIO-43) in the dialect each provider
/// speaks, carrying the profile's model and thinking switch the way the runtime carries them.
/// <para>
/// The fiche asked for the request to go through the runtime's own translation
/// (<c>OpenAICompatibleProviderBase</c>). Studio Core cannot reference Infrastructure (see its
/// csproj), so the payload is written here from each provider's declared
/// <see cref="LlmProviderCapabilities.Thinking"/> level — the same declaration the runtime
/// translates from — and that level is pinned against the real providers by
/// <c>LlmProbeDialectDriftTests</c>.
/// </para>
/// </summary>
public static class LlmProbeDialect
{
    /// <summary>The output cap of the test completion: enough for "pong", a few tokens billed.</summary>
    public const int CompletionMaxTokens = 16;

    /// <summary>The one user message of the test completion.</summary>
    public const string CompletionPrompt = "ping";

    /// <summary>The version header every Anthropic request must carry.</summary>
    internal const string AnthropicVersion = "2023-06-01";

    /// <summary>
    /// What each provider declares in <see cref="LlmProviderCapabilities.Thinking"/>, keyed by
    /// the key <see cref="LlmProviderDetector"/> reports. Custom and Docker Model Runner
    /// endpoints are driven as OpenAI by the runtime, so they read OpenAI's level.
    /// </summary>
    private static readonly Dictionary<string, ThinkingSupport> Thinking = new(StringComparer.Ordinal)
    {
        [LlmProviderKeys.OpenAI] = ThinkingSupport.EffortOnly,
        [LlmProviderKeys.Anthropic] = ThinkingSupport.Toggle,
        [LlmProviderKeys.DeepSeek] = ThinkingSupport.Toggle,
        [LlmProviderKeys.Together] = ThinkingSupport.None,
        [LlmProviderKeys.Qwen] = ThinkingSupport.Budget,
        [LlmProviderKeys.Kimi] = ThinkingSupport.Toggle,
        [LlmProviderKeys.HuggingFace] = ThinkingSupport.None,
        [LlmProviderKeys.Mistral] = ThinkingSupport.EffortOnly,
        [LlmProviderKeys.Zai] = ThinkingSupport.Toggle,
        [LlmProviderKeys.Gemini] = ThinkingSupport.EffortOnly,
        [LlmProviderKeys.Grok] = ThinkingSupport.EffortOnly,
        [LlmProviderKeys.MiniMax] = ThinkingSupport.None,
        [LlmProviderKeys.OpenRouter] = ThinkingSupport.Budget,
        [LlmProviderKeys.Mammouth] = ThinkingSupport.None,
        [LlmProviderKeys.Ollama] = ThinkingSupport.Toggle,
    };

    /// <summary>The thinking control the provider behind <paramref name="providerKey"/> declares.</summary>
    public static ThinkingSupport ThinkingSupportOf(string providerKey) =>
        Thinking.TryGetValue(providerKey, out var support) ? support : Thinking[LlmProviderKeys.OpenAI];

    /// <summary>Builds the test completion for <paramref name="provider"/>.</summary>
    internal static HttpRequestMessage BuildCompletion(
        string provider, Uri baseUri, string? apiKey, string model, bool? thinkingEnabled)
    {
        var baseUrl = baseUri.AbsoluteUri.TrimEnd('/');
        var messages = new object[] { new Dictionary<string, object> { ["role"] = "user", ["content"] = CompletionPrompt } };
        var payload = new Dictionary<string, object> { ["model"] = model, ["messages"] = messages };
        string url;

        if (string.Equals(provider, LlmProviderKeys.Ollama, StringComparison.Ordinal))
        {
            // Ollama's native chat hangs off the server root, like its catalogue.
            url = $"{baseUri.GetLeftPart(UriPartial.Authority)}/api/chat";
            payload["stream"] = false;
            payload["options"] = new Dictionary<string, object> { ["num_predict"] = CompletionMaxTokens };
            if (thinkingEnabled is { } think)
                payload["think"] = think;

            return Post(url, payload);
        }

        if (string.Equals(provider, LlmProviderKeys.Anthropic, StringComparison.Ordinal))
        {
            url = $"{baseUrl}/v1/messages";
            payload["max_tokens"] = CompletionMaxTokens;
            if (thinkingEnabled is { } enabled)
                payload["thinking"] = new Dictionary<string, object> { ["type"] = enabled ? "adaptive" : "disabled" };

            var anthropic = Post(url, payload);
            anthropic.Headers.Add("x-api-key", apiKey ?? "");
            anthropic.Headers.Add("anthropic-version", AnthropicVersion);
            return anthropic;
        }

        // The OpenAI dialect, as OpenAICompatibleProviderBase writes it.
        url = $"{baseUrl}/chat/completions";
        payload[string.Equals(provider, LlmProviderKeys.OpenAI, StringComparison.Ordinal)
            ? "max_completion_tokens"   // OpenAI retired max_tokens on its current chat models
            : "max_tokens"] = CompletionMaxTokens;
        if (thinkingEnabled is { } on)
            WriteThinkingSwitch(provider, payload, on);

        var message = Post(url, payload);
        if (!string.IsNullOrWhiteSpace(apiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return message;
    }

    /// <summary>
    /// The on/off switch, bounded by the declared level exactly as the runtime bounds it: an
    /// effort-only or switchless API gets nothing (the runtime drops it with a warning), DashScope
    /// reads <c>enable_thinking</c>, OpenRouter a <c>reasoning</c> object, the rest a
    /// <c>thinking</c> block.
    /// </summary>
    private static void WriteThinkingSwitch(string provider, Dictionary<string, object> payload, bool enabled)
    {
        if (ThinkingSupportOf(provider) < ThinkingSupport.Toggle)
            return;

        if (string.Equals(provider, LlmProviderKeys.Qwen, StringComparison.Ordinal))
            payload["enable_thinking"] = enabled;
        else if (string.Equals(provider, LlmProviderKeys.OpenRouter, StringComparison.Ordinal))
            payload["reasoning"] = new Dictionary<string, object> { ["enabled"] = enabled };
        else
            payload["thinking"] = new Dictionary<string, object> { ["type"] = enabled ? "enabled" : "disabled" };
    }

    private static HttpRequestMessage Post(string url, Dictionary<string, object> payload) =>
        new(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
}
