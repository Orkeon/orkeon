namespace Orkeon.Infrastructure.Constants.Llm;

/// <summary>
/// Centralised HTTP constants (headers, content-types, SSE markers, authentication prefixes)
/// used across LLM providers, streaming parsers, and HTTP tools.
/// Eliminates scattered magic string literals and provides a single place to update them.
/// </summary>
public static class HttpDefaults
{
    // ── Anthropic ──────────────────────────────────────────────────────────

    /// <summary>Anthropic API key request header name.</summary>
    public const string AnthropicApiKeyHeader = "x-api-key";

    /// <summary>Anthropic API version request header name.</summary>
    public const string AnthropicVersionHeader = "anthropic-version";

    /// <summary>Anthropic Messages API version value.</summary>
    public const string AnthropicApiVersion = "2023-06-01";

    /// <summary>
    /// Anthropic workspace-scoping request header name. Mandatory with identity-linked API
    /// keys (the API refuses the request without it, 2026-08-30); meaningless with classic
    /// keys, so it is only sent when <c>LlmConfig.WorkspaceId</c> is set.
    /// </summary>
    public const string AnthropicWorkspaceIdHeader = "anthropic-workspace-id";

    // ── Azure OpenAI ────────────────────────────────────────────────────────

    /// <summary>Azure OpenAI API key request header name.</summary>
    public const string AzureApiKeyHeader = "api-key";

    // ── OpenRouter ──────────────────────────────────────────────────────────

    /// <summary>
    /// OpenRouter's application-attribution header naming the app's URL — literally
    /// <c>HTTP-Referer</c>, as the vendor documents it, not the standard <c>Referer</c> that
    /// <c>HttpRequestHeaders.Referrer</c> would emit (LLM-09, D-06).
    /// </summary>
    public const string OpenRouterRefererHeader = "HTTP-Referer";

    /// <summary>OpenRouter's application-attribution header naming the app (<c>X-OpenRouter-Title</c>).</summary>
    public const string OpenRouterTitleHeader = "X-OpenRouter-Title";

    /// <summary>
    /// The URL Orkeon attributes its OpenRouter traffic to. Constant, not configurable: it is
    /// neither a secret nor a preference but the application's identity in OpenRouter's
    /// public app ranking.
    /// </summary>
    public const string OpenRouterReferer = "https://github.com/Orkeon/orkeon";

    /// <summary>The application title Orkeon attributes its OpenRouter traffic to.</summary>
    public const string OpenRouterTitle = "Orkeon";

    // ── Content types ───────────────────────────────────────────────────────

    /// <summary>JSON content-type header value.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>Server-Sent Events content-type header value.</summary>
    public const string SseContentType = "text/event-stream";

    // ── SSE protocol ───────────────────────────────────────────────────────

    /// <summary>Prefix for SSE data lines (<c>data: </c>).</summary>
    public const string SseDataPrefix = "data: ";

    /// <summary>SSE stream termination marker (<c>[DONE]</c>).</summary>
    public const string SseDoneMarker = "[DONE]";

    // ── Authentication ──────────────────────────────────────────────────────

    /// <summary>Bearer token authentication prefix, including trailing space.</summary>
    public const string BearerPrefix = "Bearer ";

    // ── User agent ─────────────────────────────────────────────────────────

    /// <summary>Default User-Agent string sent by Orkeon HTTP clients.</summary>
    public const string DefaultUserAgent = Domain.Constants.Http.HttpDefaults.DefaultUserAgent;

    // ── Azure defaults ─────────────────────────────────────────────────────

    /// <summary>Azure-specific constants.</summary>
    internal static class AzureDefaults
    {
        /// <summary>Default Azure OpenAI REST API version.</summary>
        public const string DefaultApiVersion = "2024-02-01";
    }
}
