using System.Globalization;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Builds the SQL predicates evaluated server-side by LanceDB (DataFusion SQL).
/// String literals are escaped by doubling single quotes.
/// </summary>
/// <remarks>
/// Tag and custom-metadata filters compile to <c>LIKE '%…%'</c> over the JSON-encoded
/// columns, mirroring the substring semantics of the historical provider; values
/// containing SQL LIKE wildcards (<c>%</c>, <c>_</c>) therefore match loosely.
/// See <c>docs/architecture/memory-system.md</c>.
/// </remarks>
internal static class LanceDbFilterBuilder
{
    /// <summary>SQL predicate matching every row (used by <c>ClearAsync</c>).</summary>
    public const string MatchAllPredicate = "true";

    /// <summary>Builds the equality predicate for a primary key.</summary>
    public static string KeyEquals(string key)
        => $"{LanceDbArrowCodec.IdColumn} = '{EscapeLiteral(key)}'";

    /// <summary>
    /// Translates the metadata filter dictionary into a SQL predicate
    /// (<c>source</c> equality, tag containment, metadata substring), or
    /// <see langword="null"/> when the filter is empty.
    /// </summary>
    public static string? FromMetadataFilter(IReadOnlyDictionary<string, object>? filter)
    {
        if (filter is null || filter.Count == 0)
            return null;

        var clauses = new List<string>(filter.Count);
        foreach (var (key, value) in filter)
        {
            var literal = EscapeLiteral(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
#pragma warning disable CA1308 // lowercase is the normalized filter-key token driving the switch, not a comparison normalization
            switch (key.ToLowerInvariant())
#pragma warning restore CA1308
            {
                case "source":
                    clauses.Add($"{LanceDbArrowCodec.SourceColumn} = '{literal}'");
                    break;

                case "tag" or "tags":
                    clauses.Add($"{LanceDbArrowCodec.TagsColumn} LIKE '%{literal}%'");
                    break;

                default:
                    clauses.Add($"{LanceDbArrowCodec.MetadataColumn} LIKE '%{literal}%'");
                    break;
            }
        }

        return string.Join(" AND ", clauses);
    }

    /// <summary>Escapes a string literal for inclusion in a single-quoted SQL string.</summary>
    public static string EscapeLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
