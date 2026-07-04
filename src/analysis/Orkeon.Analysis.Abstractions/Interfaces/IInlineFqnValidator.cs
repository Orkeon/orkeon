using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Scans a deliverable's text for Fully Qualified Names mentioned in prose (outside of
/// fenced citation code blocks, which are already covered by
/// <see cref="ICitationBlockValidator"/>), and verifies each against the live
/// <see cref="IRaggableStore"/>.
/// </summary>
/// <remarks>
/// Unlike the citation-block validator, this check never rejects the deliverable.
/// It surfaces unknown FQNs so that the pipeline can annotate the AUTO_SUMMARY with
/// a warning — operators decide whether to accept the output.
/// </remarks>
public interface IInlineFqnValidator
{
    /// <summary>
    /// Extracts prose FQN mentions and validates each against the store.
    /// </summary>
    Task<InlineFqnValidationResult> ValidateAsync(string content, CancellationToken ct);
}

/// <summary>
/// Outcome of an inline FQN scan.
/// </summary>
public sealed record InlineFqnValidationResult
{
    /// <summary>Distinct FQN strings found in prose (no duplicates).</summary>
    public ImmutableArray<string> ExtractedFqns { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Subset of <see cref="ExtractedFqns"/> that could not be resolved in the store
    /// (no canonical match, no longer-anchor in the document, no rewrite candidate).
    /// </summary>
    public ImmutableArray<string> UnknownFqns { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Bare-form FQN citations (e.g. <c>ts::MySqlDialect</c>) that resolved to exactly one
    /// canonical long FQN via local-name lookup. The mapping is bare → canonical.
    /// These citations are no longer in <see cref="UnknownFqns"/>.
    /// </summary>
    public ImmutableDictionary<string, string> Rewrites { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Bare-form FQN citations that matched 2 or more canonical FQNs and therefore could not
    /// be auto-rewritten. These citations are no longer in <see cref="UnknownFqns"/> but
    /// require operator review — the candidates are listed for context.
    /// </summary>
    public ImmutableArray<AmbiguousFqn> Ambiguous { get; init; } =
        ImmutableArray<AmbiguousFqn>.Empty;

    /// <summary>
    /// Backward-compatible positional constructor — keeps existing call sites compiling.
    /// </summary>
    public InlineFqnValidationResult(
        ImmutableArray<string> ExtractedFqns,
        ImmutableArray<string> UnknownFqns)
    {
        this.ExtractedFqns = ExtractedFqns;
        this.UnknownFqns = UnknownFqns;
    }

    /// <summary>Default constructor for <c>with</c>-init initialization.</summary>
    public InlineFqnValidationResult() { }
}

/// <summary>
/// A bare-form FQN citation that matched 2 or more canonical FQNs in the store. Lists the
/// candidates so operators can pick the intended one without a second tool call.
/// </summary>
/// <param name="BareFqn">The original bare-form citation, e.g. <c>ts::Symbol</c>.</param>
/// <param name="Candidates">Canonical FQNs sharing the trailing symbol name.</param>
public sealed record AmbiguousFqn(
    string BareFqn,
    ImmutableArray<string> Candidates);
