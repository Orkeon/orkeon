using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Analysis.Internal;

internal static class FqnSuggestions
{
    private const int MaxSuggestions = 3;
    private const int MaxDistance = 5;

    public static async Task<FqnNotFoundException> BuildAsync(
        IRaggableStore store,
        string missing,
        CancellationToken ct)
    {
        var pool = await store.QueryAsync(new NodeQuery { Take = 1000 }, ct).ConfigureAwait(false);
        var suggestions = pool
            .Where(n => !string.IsNullOrEmpty(n.Fqn))
            .Select(n => (n.Fqn, Distance: Levenshtein(n.Fqn, missing)))
            .Where(t => t.Distance <= MaxDistance)
            .OrderBy(t => t.Distance)
            .Take(MaxSuggestions)
            .Select(t => t.Fqn)
            .ToImmutableArray();
        return new FqnNotFoundException(missing, "never-existed", suggestions);
    }

    private static int Levenshtein(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
