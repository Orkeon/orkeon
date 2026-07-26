namespace Orkeon.Rag.Validation;

/// <summary>
/// What to do with a document whose verdict is <see cref="PromptInjectionVerdict.Suspicious"/>.
/// <see cref="PromptInjectionVerdict.Rejected"/> documents are always discarded (and traced).
/// </summary>
public enum SuspiciousContentAction
{
    /// <summary>Keep the document but mark it (metadata / findings) so consumers see the flag.</summary>
    Flag,

    /// <summary>Discard the document, with the reasons traced in logs and results.</summary>
    Discard,
}

/// <summary>
/// Configurable handling policy for <see cref="PromptInjectionDocumentValidator"/>.
/// The verdict itself is always deterministic; the policy only decides what happens
/// to <see cref="PromptInjectionVerdict.Suspicious"/> documents and where the
/// verdict bands sit.
/// </summary>
public sealed record PromptInjectionPolicy
{
    /// <summary>Default policy: flag suspicious documents, thresholds 0.3 / 0.7.</summary>
    public static PromptInjectionPolicy Default { get; } = new();

    /// <summary>Handling of suspicious documents. Default: <see cref="SuspiciousContentAction.Flag"/>.</summary>
    public SuspiciousContentAction SuspiciousAction { get; init; } = SuspiciousContentAction.Flag;

    /// <summary>Risk score (inclusive) from which a document is at least suspicious. Default 0.3.</summary>
    public double SuspiciousThreshold { get; init; } = 0.3;

    /// <summary>Risk score (exclusive) above which a document is rejected. Default 0.7.</summary>
    public double RejectThreshold { get; init; } = 0.7;
}
