using System.Collections.Immutable;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.DeliverableResolvers;

/// <summary>
/// Outcome of a deliverable resolution attempt by an <see cref="IDeliverableResolver"/>.
/// </summary>
/// <param name="Persisted">True when the deliverable was successfully written to disk.</param>
/// <param name="Path">Virtual path of the persisted file, or <c>null</c> when no write occurred.</param>
/// <param name="SizeBytes">Size in bytes of the persisted content, or 0 when nothing was written.</param>
/// <param name="SourceUsed">The <see cref="DeliverableSource"/> the resolver acted upon.</param>
/// <param name="FailureReason">
/// Short machine-readable reason when <paramref name="Persisted"/> is false
/// (e.g. <c>"empty_final_message"</c>, <c>"invalid_json_despite_grammar"</c>, <c>"access_denied"</c>).
/// </param>
/// <param name="UnknownFqns">
/// Prose-cited FQNs that could not be resolved in the RaggableTree store. Non-blocking —
/// surfaced for the AUTO_SUMMARY warnings section. <c>null</c> when no check ran.
/// </param>
/// <param name="RewrittenFqns">
/// Bare-form prose citations (e.g. <c>ts::Symbol</c>) that resolved uniquely via local-name
/// lookup. Mapping is bare → canonical FQN. <c>null</c> when no validator ran or no
/// rewrites occurred.
/// </param>
/// <param name="AmbiguousFqns">
/// Bare-form citations that matched 2+ canonical FQNs and require operator review.
/// <c>null</c> when no validator ran or no ambiguity was detected.
/// </param>
/// <param name="PartialExtraction">
/// True when the deliverable was persisted but does not fully satisfy the declared schema
/// (typically: missing required top-level keys). The caller — runner shared layer, FINAL_SUMMARY
/// emitter, downstream gates — decides whether a partial deliverable counts as success.
/// Surfaced for Experiment 07 friction #3 so a partially-shaped JSON survives instead of being
/// silently dropped on the floor.
/// </param>
public sealed record DeliverableResolutionResult(
    bool Persisted,
    string? Path,
    int SizeBytes,
    DeliverableSource SourceUsed,
    string? FailureReason = null,
    ImmutableArray<string>? UnknownFqns = null,
    ImmutableDictionary<string, string>? RewrittenFqns = null,
    ImmutableArray<DeliverableAmbiguousFqn>? AmbiguousFqns = null,
    bool PartialExtraction = false);

/// <summary>
/// Bare-form FQN that matched 2 or more canonical entries during validation.
/// </summary>
/// <param name="BareFqn">Original bare-form citation, e.g. <c>ts::Symbol</c>.</param>
/// <param name="Candidates">Canonical FQNs sharing the trailing symbol name.</param>
public sealed record DeliverableAmbiguousFqn(
    string BareFqn,
    ImmutableArray<string> Candidates);
