namespace Orkeon.Rag.Validation;

/// <summary>
/// A region of document content that triggered a prompt-injection heuristic.
/// Spans make every decision auditable: nothing is discarded or flagged silently.
/// </summary>
/// <param name="RuleId">Stable identifier of the heuristic rule that fired.</param>
/// <param name="Description">Human-readable description of the rule.</param>
/// <param name="Start">Zero-based character offset of the match in the document content.</param>
/// <param name="Length">Length (in characters) of the matched region.</param>
/// <param name="Excerpt">Truncated excerpt of the matched text (max 80 characters).</param>
public sealed record InjectionSpan(
    string RuleId,
    string Description,
    int Start,
    int Length,
    string Excerpt);
