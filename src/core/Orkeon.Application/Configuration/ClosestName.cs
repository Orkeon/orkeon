namespace Orkeon.Application.Configuration;

/// <summary>The known name a misspelled one is likely meant to be.</summary>
public static class ClosestName
{
    /// <summary>
    /// The known name closest to <paramref name="written"/> - two edits away at most, a third of its
    /// length for a long one, a swap of two neighbours counting as one, case ignored - or null.
    /// </summary>
    /// <param name="written">The name as written.</param>
    /// <param name="known">The names that exist, the first winning a tie.</param>
    public static string? Of(string written, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(known);

        var budget = Math.Max(2, written.Length / 3);
        var upper = written.ToUpperInvariant();
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in known)
        {
            var distance = Distance(upper, candidate.ToUpperInvariant());
            if (distance <= budget && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The optimal string alignment distance: insertions, deletions, substitutions, adjacent swaps.</summary>
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1][];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i] = new int[b.Length + 1];
            d[i][0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
            d[0][j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i][j] = Math.Min(Math.Min(d[i - 1][j] + 1, d[i][j - 1] + 1), d[i - 1][j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i][j] = Math.Min(d[i][j], d[i - 2][j - 2] + 1);
            }
        }

        return d[a.Length][b.Length];
    }
}
