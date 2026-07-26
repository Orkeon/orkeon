using Orkeon.Rag.Validation;

namespace Orkeon.Rag.WebFallback;

/// <summary>
/// Transport options of the opt-in RAG web search fallback (RAG-06/C1 security
/// part): endpoint, authentication, timeout and suspicious-content policy of
/// <see cref="WebSearchDocumentRetriever"/>. Standalone options class bound on
/// <c>Orkeon:Rag:WebFallback</c> — intentionally NOT part of <c>RagOptions</c>
/// to keep the web egress surface a separate, explicit opt-in. Disabled by
/// default; an enabled fallback without an endpoint is treated as disabled and
/// loudly logged.
/// </summary>
/// <remarks>
/// Reconciliation (RAG-06/6D): lots 6A and 6C shipped two homonymous
/// <c>RagWebFallbackOptions</c> types in parallel. The Abstractions type
/// (<c>Orkeon.Rag.Abstractions.Options.RagWebFallbackOptions</c>, node
/// <c>RagOptions.Corrective.WebFallback</c>, section
/// <c>Orkeon:Rag:Corrective:WebFallback</c>) keeps the PIPELINE-side policy —
/// whether the corrective graph may route to its <c>web_fallback</c> node and
/// how many documents it may ask for. This type was renamed to
/// <c>WebSearchRetrieverOptions</c> and keeps the TRANSPORT concerns only; a
/// merge into the Abstractions type was rejected because
/// <see cref="SuspiciousAction"/> (validation layer) and the HTTP semantics
/// would violate the Domain+BCL-only rule of the shared kernel (ADR-006,
/// enforced by its ArchitectureTests). Both <c>Enabled</c> switches must be on
/// for a web document to ever reach the graph.
/// </remarks>
public sealed class WebSearchRetrieverOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionKey = "Orkeon:Rag:WebFallback";

    /// <summary>Master switch of the transport. Default <c>false</c> — strict opt-in.</summary>
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
