using System.Diagnostics.CodeAnalysis;
using System.Net;
using Orkeon.Constants.Llm;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>What a model address (Llm:BaseUrl) says: the provider, the native Ollama root, the locality.</summary>
[SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "The address is read as written; a malformed one is refused, not thrown.")]
public sealed record LlmEndpointReading
{
    /// <summary>Gets the provider key (an LlmProviderKeys value).</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the native root when the reading is Ollama, else null.</summary>
    public string? OllamaRoot { get; }

    /// <summary>Gets a value indicating whether the address is this machine or its Docker host.</summary>
    public bool IsLocal { get; }

    /// <summary>Gets the notice the reading carries.</summary>
    public LlmEndpointNotice Notice { get; }

    private const int OllamaPort = 11434;
    private const int DockerModelRunnerPort = 12434;
    private const string DockerModelRunnerHost = "model-runner.docker.internal";

    private static readonly (string Provider, string[] Domains)[] VendorRules =
    [
        (LlmProviderKeys.Together, ["together.xyz"]),
        (LlmProviderKeys.Qwen, ["dashscope.aliyuncs.com", "dashscope-intl.aliyuncs.com", "maas.aliyuncs.com"]),
        (LlmProviderKeys.DeepSeek, ["deepseek.com"]),
        (LlmProviderKeys.Kimi, ["moonshot.cn", "moonshot.ai"]),
        (LlmProviderKeys.Mistral, ["mistral.ai"]),
        (LlmProviderKeys.Gemini, ["generativelanguage.googleapis.com"]),
        (LlmProviderKeys.Grok, ["api.x.ai"]),
        (LlmProviderKeys.MiniMax, ["api.minimax.io", "api.minimaxi.com"]),
        (LlmProviderKeys.HuggingFace, ["huggingface.co", "hf.co"]),
        (LlmProviderKeys.Zai, ["api.z.ai", "bigmodel.cn"]),
        (LlmProviderKeys.OpenRouter, ["openrouter.ai"]),
        (LlmProviderKeys.Mammouth, ["mammouth.ai"]),
        (LlmProviderKeys.OpenAI, ["openai.com"]),
        (LlmProviderKeys.Anthropic, ["anthropic.com"]),
    ];

    private static readonly (string Prefix, string Provider)[] ModelPrefixRoutes =
    [
        ("gpt", LlmProviderKeys.OpenAI),
        ("claude", LlmProviderKeys.Anthropic),
        ("llama", LlmProviderKeys.Ollama),
        ("codellama", LlmProviderKeys.Ollama),
        (LlmProviderKeys.Mistral, LlmProviderKeys.Mistral),
        ("ministral", LlmProviderKeys.Mistral),
        (LlmProviderKeys.Qwen, LlmProviderKeys.Qwen),
        (LlmProviderKeys.DeepSeek, LlmProviderKeys.DeepSeek),
        (LlmProviderKeys.MoonshotAlias, LlmProviderKeys.Kimi),
        (LlmProviderKeys.GlmAlias, LlmProviderKeys.Zai),
        (LlmProviderKeys.Gemini, LlmProviderKeys.Gemini),
        (LlmProviderKeys.Grok, LlmProviderKeys.Grok),
        (LlmProviderKeys.MiniMax, LlmProviderKeys.MiniMax),
        (LlmProviderKeys.OpenRouter + "/", LlmProviderKeys.OpenRouter),
    ];

    private LlmEndpointReading(string providerKey, string? ollamaRoot, bool isLocal, LlmEndpointNotice notice)
    {
        ProviderKey = providerKey;
        OllamaRoot = ollamaRoot;
        IsLocal = isLocal;
        Notice = notice;
    }

    /// <summary>Reads an address, else the model, else the key.</summary>
    /// <param name="baseUrl">The address, or null.</param>
    /// <param name="model">The model name, or null.</param>
    /// <param name="apiKey">The key, read for its prefix only, or null.</param>
    /// <returns>The reading.</returns>
    public static LlmEndpointReading Of(string? baseUrl, string? model, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return new LlmEndpointReading(ReadModelThenKey(model, apiKey), null, false, LlmEndpointNotice.None);

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new LlmEndpointReading(LlmProviderKeys.Custom, null, false, LlmEndpointNotice.Refused);

        var host = uri.Host;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isLocal = IsLocalHost(host);
        var hasAnthropicSegment = segments.Any(s => string.Equals(s, LlmProviderKeys.Anthropic, StringComparison.OrdinalIgnoreCase));

        var vendor = ReadVendor(host);
        if (vendor is not null)
        {
            var refused = vendor != LlmProviderKeys.Anthropic && hasAnthropicSegment;
            return new LlmEndpointReading(vendor, null, false, refused ? LlmEndpointNotice.Refused : LlmEndpointNotice.None);
        }

        if (string.Equals(host, DockerModelRunnerHost, StringComparison.Ordinal)
            || segments.Any(s => string.Equals(s, "engines", StringComparison.OrdinalIgnoreCase))
            || uri.Port == DockerModelRunnerPort)
            return new LlmEndpointReading(LlmProviderKeys.DockerModelRunner, null, isLocal, LlmEndpointNotice.None);

        if (uri.Port == OllamaPort)
            return new LlmEndpointReading(LlmProviderKeys.Ollama, NativeOllamaRoot(baseUrl), isLocal, LlmEndpointNotice.None);

        if (host.Contains(LlmProviderKeys.Anthropic, StringComparison.Ordinal) || hasAnthropicSegment)
            return new LlmEndpointReading(LlmProviderKeys.Anthropic, null, false, LlmEndpointNotice.None);

        var notice = segments.Length == 0 ? LlmEndpointNotice.Silent : LlmEndpointNotice.None;
        return new LlmEndpointReading(LlmProviderKeys.Custom, null, isLocal, notice);
    }

    /// <summary>Gets the native Ollama root of an address, for a named provider.</summary>
    /// <param name="baseUrl">The address.</param>
    /// <returns>The root alone.</returns>
    public static string NativeOllamaRoot(string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        var text = baseUrl.Trim();
        var path = string.Empty;
        var head = text.TrimEnd('/');
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            head = uri.GetLeftPart(UriPartial.Authority);
            path = uri.AbsolutePath.TrimEnd('/');
        }

        var last = path.LastIndexOf('/');
        var tail = last >= 0 ? path[(last + 1)..] : path;
        if (string.Equals(tail, "v1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tail, "api", StringComparison.OrdinalIgnoreCase))
            path = path[..Math.Max(last, 0)];

        return head + path;
    }

    private static string? ReadVendor(string host)
    {
        if (host.Contains(LlmProviderKeys.AzureShort, StringComparison.Ordinal)
            || host.Contains(".cognitiveservices.", StringComparison.Ordinal))
            return LlmProviderKeys.AzureOpenAI;

        foreach (var (provider, domains) in VendorRules)
        {
            if (domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal)))
                return provider;
        }

        return null;
    }

    private static bool IsLocalHost(string host)
    {
        if (host is "localhost" or "host.docker.internal" or DockerModelRunnerHost)
            return true;

        return IPAddress.TryParse(host.Trim('[', ']'), out var ip)
            && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any));
    }

    private static string ReadModelThenKey(string? model, string? apiKey)
    {
        if (!string.IsNullOrEmpty(model))
        {
            if (string.Equals(model, LlmProviderKeys.Mistral, StringComparison.OrdinalIgnoreCase)
                || model.StartsWith("mistral:", StringComparison.OrdinalIgnoreCase))
                return LlmProviderKeys.Ollama;

            foreach (var (prefix, provider) in ModelPrefixRoutes)
            {
                if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return provider;
            }
        }

        if (apiKey is not null && apiKey.StartsWith("hf_", StringComparison.Ordinal))
            return LlmProviderKeys.HuggingFace;

        if (apiKey is not null && apiKey.StartsWith("xai-", StringComparison.Ordinal))
            return LlmProviderKeys.Grok;

        return LlmProviderKeys.OpenAI;
    }
}

/// <summary>The notice a reading carries.</summary>
public enum LlmEndpointNotice
{
    /// <summary>Nothing to say.</summary>
    None,

    /// <summary>The address is refused.</summary>
    Refused,

    /// <summary>A custom address with an empty path: warned.</summary>
    Silent,
}
