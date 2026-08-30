using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Infrastructure.Constants.Llm;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// Lists the models a provider currently serves.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a campaign can be told "run every <c>gpt-5.6-*</c>" without anyone
/// maintaining a list by hand. A hand-maintained list is precisely what produced the four
/// blocking gaps of the 2026-07-27 audit: four providers whose default model had been retired
/// upstream while the constant in the repo stayed put.
/// </para>
/// <para>
/// Four dialects cover the twelve providers. Azure has none in deployment mode — deployments
/// are names an operator chose, not a public catalogue — and says so with a typed error rather
/// than returning an empty list that would read as "this provider serves nothing".
/// </para>
/// </remarks>
internal static class LlmCatalogClient
{
    /// <summary>Providers whose catalogue is the OpenAI <c>GET /models</c> endpoint.</summary>
    private static readonly HashSet<string> OpenAiCompatible = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai", "together", "togetherai", "deepseek", "kimi", "moonshot",
        "qwen", "mistral", "huggingface", "hf", "zai", "glm", "zhipu",
        "gemini", "google",
        "grok", "xai",
        "minimax",
    };

    /// <summary>Default base URL per provider key, mirroring what the factory would use.</summary>
    private static readonly Dictionary<string, string> DefaultBaseUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = LlmEndpoints.OpenAI,
        ["anthropic"] = LlmEndpoints.Anthropic,
        ["deepseek"] = LlmEndpoints.DeepSeek,
        ["together"] = LlmEndpoints.Together,
        ["togetherai"] = LlmEndpoints.Together,
        ["qwen"] = LlmEndpoints.Qwen,
        ["kimi"] = LlmEndpoints.Kimi,
        ["moonshot"] = LlmEndpoints.Kimi,
        ["huggingface"] = LlmEndpoints.HuggingFace,
        ["hf"] = LlmEndpoints.HuggingFace,
        ["mistral"] = LlmEndpoints.Mistral,
        ["zai"] = LlmEndpoints.Zai,
        ["glm"] = LlmEndpoints.Zai,
        ["zhipu"] = LlmEndpoints.Zai,
        ["gemini"] = LlmEndpoints.Gemini,
        ["google"] = LlmEndpoints.Gemini,
        ["grok"] = LlmEndpoints.Grok,
        ["xai"] = LlmEndpoints.Grok,
        ["minimax"] = LlmEndpoints.MiniMax,
        ["ollama"] = LlmEndpoints.OllamaDefault,
    };

    /// <summary>Resolves the base URL a provider would use, honouring an explicit override.</summary>
    /// <param name="provider">Provider key.</param>
    /// <param name="overrideUrl">Explicit base URL, or <see langword="null"/> for the default.</param>
    /// <returns>The effective base URL, trimmed of any trailing slash.</returns>
    /// <exception cref="NotSupportedException">The provider has no default and none was supplied.</exception>
    public static string ResolveBaseUrl(string provider, string? overrideUrl)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl))
            return overrideUrl.TrimEnd('/');

        if (DefaultBaseUrls.TryGetValue(provider, out var known))
            return known.TrimEnd('/');

        throw new NotSupportedException(
            $"provider '{provider}' has no default base URL — pass --base-url explicitly.");
    }

    /// <summary>Lists the models the provider currently serves.</summary>
    /// <param name="client">HTTP client to use.</param>
    /// <param name="provider">Provider key, as accepted by the factory.</param>
    /// <param name="baseUrl">Effective base URL.</param>
    /// <param name="apiKey">API key, or <see langword="null"/> for providers that need none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The model identifiers, deduplicated and sorted ordinally. Sorted rather than left in
    /// vendor order because a campaign caps a wildcard at N models, and that cap has to select
    /// the same N on every run — vendor listing order is neither documented nor stable.
    /// </returns>
    /// <exception cref="NotSupportedException">The provider exposes no catalogue endpoint.</exception>
    /// <exception cref="HttpRequestException">The catalogue endpoint refused the request.</exception>
    public static async Task<IReadOnlyList<string>> ListAsync(
        HttpClient client, string provider, string baseUrl, string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        using var request = BuildRequest(provider, baseUrl.TrimEnd('/'), apiKey);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{provider} catalogue returned {(int)response.StatusCode}: {Shorten(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var ids = IsOllama(provider) ? ReadOllamaTags(document) : ReadOpenAiStyleData(document);

        return [.. ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>Filters identifiers by a shell-style glob (<c>*</c> and <c>?</c>) or an exact name.</summary>
    /// <param name="candidates">Identifiers to filter.</param>
    /// <param name="pattern">The glob or literal, or <see langword="null"/>/empty to keep everything.</param>
    /// <returns>The matching identifiers, order preserved.</returns>
    /// <remarks>
    /// A literal narrows to that one identifier rather than passing everything through: an
    /// option named <c>--filter</c> that quietly filters nothing would be a trap.
    /// </remarks>
    public static IReadOnlyList<string> Filter(IEnumerable<string> candidates, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (string.IsNullOrWhiteSpace(pattern))
            return [.. candidates];

        if (!IsGlob(pattern))
            return [.. candidates.Where(c => string.Equals(c, pattern, StringComparison.OrdinalIgnoreCase))];

        var regex = GlobToRegex(pattern);
        return [.. candidates.Where(c => regex.IsMatch(c))];
    }

    /// <summary>True when the value is a pattern rather than a literal identifier.</summary>
    /// <param name="value">The candidate pattern.</param>
    /// <returns><see langword="true"/> when it contains a glob metacharacter.</returns>
    public static bool IsGlob(string? value) =>
        value is not null && (value.Contains('*', StringComparison.Ordinal) || value.Contains('?', StringComparison.Ordinal));

    private static HttpRequestMessage BuildRequest(string provider, string baseUrl, string? apiKey)
    {
        if (IsAzure(provider))
        {
            throw new NotSupportedException(
                "Azure OpenAI serves deployments, not a public model catalogue — declare the " +
                "deployment names in the campaign JSON instead of using a wildcard.");
        }

        if (IsOllama(provider))
            return new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/tags");

        if (IsAnthropic(provider))
        {
            var anthropic = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
            anthropic.Headers.Add("x-api-key", apiKey ?? "");
            anthropic.Headers.Add("anthropic-version", "2023-06-01");
            return anthropic;
        }

        if (!OpenAiCompatible.Contains(provider))
        {
            throw new NotSupportedException(
                $"provider '{provider}' has no known catalogue endpoint — declare its models in the campaign JSON.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return request;
    }

    private static List<string> ReadOpenAiStyleData(JsonDocument document)
    {
        // Together answers with a BARE array - no data envelope (measured 2026-08-30; the
        // first real call crashed here, TryGetProperty being invalid on an array root).
        var root = document.RootElement;
        var data = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("data", out var envelope)
                && envelope.ValueKind == JsonValueKind.Array
                ? envelope
                : default;

        if (data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    private static List<string> ReadOllamaTags(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return [];

        return models.EnumerateArray()
            .Select(item => item.TryGetProperty("name", out var name) ? name.GetString() : null)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
    }

    private static bool IsOllama(string provider) =>
        string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnthropic(string provider) =>
        string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase);

    private static bool IsAzure(string provider) =>
        string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "azure-openai", StringComparison.OrdinalIgnoreCase);

    private static Regex GlobToRegex(string pattern)
    {
        var translated = string.Concat(pattern.Select(c => c switch
        {
            '*' => ".*",
            '?' => ".",
            _ => Regex.Escape(c.ToString()),
        }));

        return new Regex($"^{translated}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
    }

    private static string Shorten(string value) =>
        value.Length <= 200 ? value : value[..200] + "…";
}
