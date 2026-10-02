using System.Globalization;
using System.Text.Json;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Builds the SQL predicates evaluated server-side by LanceDB (DataFusion SQL).
/// String literals are escaped by doubling single quotes.
/// </summary>
/// <remarks>
/// Tag and custom-metadata filters compile to <c>LIKE '%…%'</c> over the JSON-encoded
/// columns. A custom property matches as its <c>"key":"value"</c> pair, spelled the way
/// <see cref="LanceDbRecordMapper.JsonOptions"/> wrote it into <c>metadata_json</c> — the key
/// and the whole value, not a substring of either (GAP-20); values containing SQL LIKE
/// wildcards (<c>%</c>, <c>_</c>) still match loosely, and the comparison is case-sensitive.
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
    /// (<c>source</c> equality, tag containment, a custom property's <c>"key":"value"</c> pair in
    /// the metadata JSON), or <see langword="null"/> when the filter is empty.
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
                    clauses.Add(CustomPropertyClause(key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""));
                    break;
            }
        }

        return string.Join(" AND ", clauses);
    }

    /// <summary>
    /// Translates a typed <see cref="MemoryFilter"/> into a SQL predicate
    /// (<c>source</c> equality, tag containment over the JSON-encoded tags column,
    /// <c>"key":"value"</c> containment over the JSON-encoded metadata column), or
    /// <see langword="null"/> when the filter is null or empty. Custom-property matching
    /// relies on the compact JSON encoding of <c>metadata_json</c> and inherits the
    /// <c>LIKE</c> semantics documented on the class.
    /// </summary>
    public static string? FromMemoryFilter(MemoryFilter? filter)
    {
        if (filter is null || filter.IsEmpty)
            return null;

        var clauses = new List<string>();

        if (filter.Source is not null)
            clauses.Add($"{LanceDbArrowCodec.SourceColumn} = '{EscapeLiteral(filter.Source)}'");

        if (filter.Tags is { Count: > 0 })
        {
            foreach (var tag in filter.Tags)
                clauses.Add($"{LanceDbArrowCodec.TagsColumn} LIKE '%{EscapeLiteral(tag)}%'");
        }

        if (filter.CustomProperties is { Count: > 0 })
        {
            foreach (var (key, value) in filter.CustomProperties)
                clauses.Add(CustomPropertyClause(key, value));
        }

        return string.Join(" AND ", clauses);
    }

    /// <summary>
    /// The clause matching a custom property: its <c>"key":"value"</c> pair as the stored JSON
    /// spells it (same serializer options, so non-ASCII characters and quotes are escaped alike).
    /// </summary>
    private static string CustomPropertyClause(string key, string value)
    {
        var pair = $"{JsonSerializer.Serialize(key, LanceDbRecordMapper.JsonOptions)}:{JsonSerializer.Serialize(value, LanceDbRecordMapper.JsonOptions)}";
        return $"{LanceDbArrowCodec.MetadataColumn} LIKE '%{EscapeLiteral(pair)}%'";
    }

    /// <summary>Escapes a string literal for inclusion in a single-quoted SQL string.</summary>
    public static string EscapeLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
