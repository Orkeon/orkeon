using Orkeon.Rag.Validation;

namespace Orkeon.Rag.WebFallback;

/// <summary>
/// Options for the opt-in RAG web search fallback (RAG-06/C1 security part).
/// Standalone options class bound on <c>Orkeon:Rag:WebFallback</c> — intentionally
/// NOT part of <c>RagOptions</c> to keep the web egress surface a separate,
/// explicit opt-in. Disabled by default; an enabled fallback without an endpoint
/// is treated as disabled and loudly logged.
/// </summary>
public sealed class RagWebFallbackOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionKey = "Orkeon:Rag:WebFallback";

    /// <summary>Master switch. Default <c>false</c> — strict opt-in.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Search endpoint URL of a SearxNG-compatible JSON API
    /// (queried as <c>{Endpoint}?q={query}&amp;format=json</c>, expecting a
    /// <c>results[].url</c> JSON array). Empty = disabled (logged).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Name of the environment variable holding the API key, sent as an
    /// <c>Authorization: Bearer</c> header when set. Empty = no auth header.
    /// The key itself never lives in configuration files.
    /// </summary>
    public string ApiKeyEnvVar { get; set; } = "";

    /// <summary>Maximum number of search results fetched per query. Default 3.</summary>
    public int MaxResults { get; set; } = 3;

    /// <summary>Per-request timeout (search call and each page download). Default 10 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// What to do with documents the injection validator flags as
    /// <see cref="PromptInjectionVerdict.Suspicious"/>: keep them flagged in
    /// metadata (default) or discard them. Rejected documents are always discarded.
    /// </summary>
    public SuspiciousContentAction SuspiciousAction { get; set; } = SuspiciousContentAction.Flag;
}
