using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Default implementation of <see cref="IInlineFqnValidator"/>.
/// Extracts FQN-shaped tokens from prose, excludes anything inside fenced code
/// blocks (handled by <see cref="CitationBlockValidator"/>), and resolves each
/// distinct candidate against <see cref="IRaggableStore.GetAsync"/>.
/// </summary>
/// <remarks>
/// Unknown bare-form citations such as <c>ts::Symbol</c> trigger a fallback
/// <see cref="IRaggableStore.FindByLocalNameAsync"/> lookup: a unique match is
/// auto-rewritten to its canonical FQN; multiple matches are surfaced as
/// <see cref="AmbiguousFqn"/> entries so operators can disambiguate.
/// </remarks>
public sealed partial class InlineFqnValidator : IInlineFqnValidator
{
    [GeneratedRegex(@"```(?:\w+)?\n.*?```", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FencedBlockRegex();

    [GeneratedRegex(@"(?<![\w:/@\-])/?[a-z@][a-zA-Z0-9_@/\-]*(?:::[A-Z][A-Za-z0-9_]*)+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FqnRegex();

    /// <summary>
    /// Bare-form pattern eligible for local-name fallback: a single lowercase prefix
    /// (the agent's "kind tag" — typically <c>ts</c>, <c>cs</c>, <c>py</c>) followed by
    /// exactly one <c>::</c> and a CamelCase symbol. Excludes legitimate package-prefixed
    /// FQNs which already encode their canonical form.
    /// </summary>
    [GeneratedRegex(@"^[a-z][a-z0-9_]*::[A-Z][A-Za-z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BareFqnRegex();

    /// <summary>Maximum number of candidates listed per ambiguous bare FQN.</summary>
    private const int MaxAmbiguousCandidates = 5;

    private readonly IRaggableStore _store;

    /// <summary>Initializes a new instance.</summary>
    public InlineFqnValidator(IRaggableStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<InlineFqnValidationResult> ValidateAsync(string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new InlineFqnValidationResult();

        var prose = FencedBlockRegex().Replace(content, string.Empty);

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in FqnRegex().Matches(prose))
            candidates.Add(m.Value);

        if (candidates.Count == 0)
            return new InlineFqnValidationResult();

        var unknown = ImmutableArray.CreateBuilder<string>();
        var rewrites = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var ambiguous = ImmutableArray.CreateBuilder<AmbiguousFqn>();

        foreach (var fqn in candidates)
        {
            ct.ThrowIfCancellationRequested();
            await ClassifyCandidateAsync(fqn, content, unknown, rewrites, ambiguous, ct).ConfigureAwait(false);
        }

        return new InlineFqnValidationResult
        {
            ExtractedFqns = candidates.ToImmutableArray(),
            UnknownFqns = unknown.ToImmutable(),
            Rewrites = rewrites.ToImmutable(),
            Ambiguous = ambiguous.ToImmutable(),
        };
    }

    private async System.Threading.Tasks.Task ClassifyCandidateAsync(
        string fqn,
        string content,
        ImmutableArray<string>.Builder unknown,
        ImmutableDictionary<string, string>.Builder rewrites,
        ImmutableArray<AmbiguousFqn>.Builder ambiguous,
        CancellationToken ct)
    {
        var node = await _store.GetAsync(fqn, ct).ConfigureAwait(false);
        if (node is not null) return;

        if (HasLongerAnchor(fqn, content)) return;

        // Bare-form fallback — only attempt for the narrow `<lang>::Symbol` shape
        // produced by agent prose summaries. Skip anything more structured: if the
        // agent already wrote a multi-segment FQN that failed `GetAsync`, the
        // problem is not "missing prefix" but a stale or fabricated reference.
        if (BareFqnRegex().IsMatch(fqn))
        {
            var localName = fqn[(fqn.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            var matches = await _store.FindByLocalNameAsync(localName, ct).ConfigureAwait(false);

            if (matches.Count == 1)
            {
                rewrites[fqn] = matches[0].Fqn;
                return;
            }

            if (matches.Count > 1)
            {
                var listed = matches
                    .Take(MaxAmbiguousCandidates)
                    .Select(m => m.Fqn)
                    .ToImmutableArray();
                ambiguous.Add(new AmbiguousFqn(fqn, listed));
                return;
            }
        }

        unknown.Add(fqn);
    }

    /// <summary>
    /// Returns true when the unknown FQN's trailing symbol (the segment after the last
    /// <c>::</c>) appears in another <c>::Symbol</c> occurrence in the document with a
    /// different prefix — i.e. the unknown candidate is an abbreviated reference whose
    /// canonical long form lives elsewhere (typically inside a fenced
    /// <c>markdown_ready_block</c> the <see cref="FqnRegex"/> cannot fully match because
    /// it contains lowercase intermediate segments like <c>::inngest::</c>).
    /// </summary>
    private static bool HasLongerAnchor(string unknownFqn, string content)
    {
        var lastSep = unknownFqn.LastIndexOf("::", StringComparison.Ordinal);
        if (lastSep < 0) return false;

        var tail = unknownFqn[(lastSep + 2)..];
        if (tail.Length == 0) return false;

        var needle = "::" + tail;

        var span = content.AsSpan();
        var cursor = 0;
        while (cursor < span.Length)
        {
            var hit = span[cursor..].IndexOf(needle.AsSpan(), StringComparison.Ordinal);
            if (hit < 0) return false;
            var absolute = cursor + hit;

            // Reject hits that are merely the suffix of a longer identifier (e.g.
            // `::InngestFunction` should not anchor `::Inngest`).
            var afterIdx = absolute + needle.Length;
            if (afterIdx < span.Length && IsIdentifierChar(span[afterIdx]))
            {
                cursor = absolute + needle.Length;
                continue;
            }

            // The prefix is everything from the previous whitespace/punctuation up to the
            // hit. If that prefix differs from the unknown FQN's own prefix, this is a
            // legitimate anchor.
            var prefixStart = absolute;
            while (prefixStart > 0 && IsIdentifierChar(span[prefixStart - 1]))
                prefixStart--;

            var prefix = span[prefixStart..absolute];
            var unknownPrefix = unknownFqn.AsSpan(0, lastSep);
            if (!prefix.SequenceEqual(unknownPrefix))
                return true;

            cursor = absolute + needle.Length;
        }

        return false;
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '/' || c == '@' || c == '-' || c == ':' || c == '.';
}
