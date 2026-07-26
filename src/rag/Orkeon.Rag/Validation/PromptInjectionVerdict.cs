namespace Orkeon.Rag.Validation;

/// <summary>
/// Per-document verdict produced by <see cref="PromptInjectionDocumentValidator"/>.
/// </summary>
public enum PromptInjectionVerdict
{
    /// <summary>No injection heuristic fired above the suspicious threshold.</summary>
    Clean,

    /// <summary>
    /// Some heuristics fired but below the rejection threshold. The document may be
    /// kept (flagged) or discarded depending on <see cref="PromptInjectionPolicy.SuspiciousAction"/>.
    /// </summary>
    Suspicious,

    /// <summary>
    /// High-confidence injection content. The document must never reach an LLM prompt.
    /// </summary>
    Rejected,
}
