using Orkeon.Constants.Llm;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Infers the LLM provider from <c>Llm:BaseUrl</c>. An <c>appsettings.json</c> has no
/// <c>Llm:Provider</c> key — the endpoint decides — so the UIs display the result of
/// this heuristic read-only, next to the URL field, and never write it back.
/// </summary>
public static class LlmProviderDetector
{
    /// <summary>Reported when no base URL is configured (the <c>none</c> preset).</summary>
    public const string None = LlmProviderKeys.None;

    /// <summary>Reported for an endpoint that matches no known host.</summary>
    public const string Custom = LlmProviderKeys.Custom;

    /// <summary>Local Ollama server.</summary>
    public const string Ollama = LlmProviderKeys.Ollama;

    /// <summary>Docker Model Runner's llama.cpp OpenAI-compatible endpoint.</summary>
    public const string DockerModelRunner = LlmProviderKeys.DockerModelRunner;

    /// <summary>Azure OpenAI (any <c>*.openai.azure.com</c> deployment host).</summary>
    public const string AzureOpenAI = LlmProviderKeys.AzureOpenAI;

    private const int OllamaDefaultPort = 11434;
    private const int DockerModelRunnerDefaultPort = 12434;
    private const string LlamaCppEnginePathFragment = "/engines/llama.cpp";
    private const string AzureOpenAIHostSuffix = ".openai.azure.com";

    /// <summary>
    /// Known cloud hosts, derived from <see cref="LlmProviderEndpoints"/> - the satellite the
    /// providers themselves read, so there is no copy left to pin.
    /// </summary>
    private static readonly Dictionary<string, string> KnownHosts = BuildKnownHosts();

    /// <summary>
    /// Returns the provider key inferred from <paramref name="baseUrl"/>:
    /// <see cref="None"/> when there is no URL, a provider key for a recognized host,
    /// and <see cref="Custom"/> for anything else (an OpenAI-compatible endpoint the
    /// runtime will drive through the OpenAI dialect).
    /// </summary>
    [SuppressMessage("Design", "CA1054",
        Justification = "The input is the raw JSON field, which may be malformed or half-typed; the " +
                        "detector reports 'custom' for it instead of failing to construct a System.Uri.")]
    public static string Detect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return None;

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            return Custom;

        // Local endpoints are told apart by port and path, not by host.
        if (uri.IsLoopback || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.AbsolutePath.Contains(LlamaCppEnginePathFragment, StringComparison.OrdinalIgnoreCase)
                || uri.Port == DockerModelRunnerDefaultPort)
            {
                return DockerModelRunner;
            }

            if (uri.Port == OllamaDefaultPort)
                return Ollama;

            return Custom;
        }

        if (uri.Host.EndsWith(AzureOpenAIHostSuffix, StringComparison.OrdinalIgnoreCase))
            return AzureOpenAI;

        return KnownHosts.GetValueOrDefault(uri.Host, Custom);
    }

    private static Dictionary<string, string> BuildKnownHosts()
    {
        var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Add(LlmProviderEndpoints.OpenAI, LlmProviderKeys.OpenAI);
        Add(LlmProviderEndpoints.Anthropic, LlmProviderKeys.Anthropic);
        Add(LlmProviderEndpoints.Groq, LlmProviderKeys.Groq);
        Add(LlmProviderEndpoints.DeepSeek, LlmProviderKeys.DeepSeek);
        Add(LlmProviderEndpoints.Together, LlmProviderKeys.Together);
        Add(LlmProviderEndpoints.Qwen, LlmProviderKeys.Qwen);
        Add(LlmProviderEndpoints.Kimi, LlmProviderKeys.Kimi);
        Add(LlmProviderEndpoints.HuggingFace, LlmProviderKeys.HuggingFace);
        Add(LlmProviderEndpoints.Mistral, LlmProviderKeys.Mistral);
        Add(LlmProviderEndpoints.Zai, LlmProviderKeys.Zai);
        // Gemini's OpenAI-compatible host. Missing until now, so Studio reported "custom" for
        // the endpoint its own preset catalogue writes — the runtime maps it to "gemini".
        Add(LlmProviderEndpoints.Gemini, LlmProviderKeys.Gemini);
        Add(LlmProviderEndpoints.Grok, LlmProviderKeys.Grok);

        // The mainland-China Moonshot twin, documented on LlmEndpoints.Kimi.
        hosts[LlmProviderEndpoints.KimiChinaHost] = "kimi";

        return hosts;

        void Add(string endpoint, string provider)
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                hosts[uri.Host] = provider;
        }
    }
}
