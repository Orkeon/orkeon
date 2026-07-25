namespace Orkeon.Domain.Memory;

/// <summary>
/// Typed metadata filter for memory searches, replacing the legacy
/// <c>Dictionary&lt;string, object&gt;</c> filter bags over time (RAG-02/C4, plan §4.2).
/// </summary>
/// <remarks>
/// <para>Semantics (aligned with the historical dictionary filter used by the providers):</para>
/// <list type="bullet">
///   <item><description><see cref="Source"/> — case-insensitive equality with <see cref="MemoryItem.Source"/>.</description></item>
///   <item><description><see cref="Tags"/> — <b>AND</b> semantics: every filter tag must be present in
///   <see cref="MemoryItem.Tags"/> (case-insensitive).</description></item>
///   <item><description><see cref="CustomProperties"/> — each key must exist in
///   <c>Metadata.CustomProperties</c> with a case-insensitively equal value.</description></item>
/// </list>
/// <para>
/// Use <see cref="FromDictionary"/> / <see cref="ToDictionary"/> to interoperate with the
/// legacy dictionary shape (<c>"source"</c>, <c>"tag"</c>/<c>"tags"</c>, custom keys).
/// </para>
/// </remarks>
public sealed record MemoryFilter
{
    /// <summary>Expected memory source (case-insensitive equality), or <see langword="null"/> to ignore.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// Tags that must ALL be present on the item (case-insensitive),
    /// or <see langword="null"/>/empty to ignore.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Custom metadata properties that must match exactly (case-insensitive value equality),
    /// or <see langword="null"/>/empty to ignore.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CustomProperties { get; init; }

    /// <summary>Gets a value indicating whether this filter matches every item (no criteria set).</summary>
    public bool IsEmpty =>
        Source is null
        && (Tags is null || Tags.Count == 0)
        && (CustomProperties is null || CustomProperties.Count == 0);

    /// <summary>
    /// Evaluates this filter against a memory item.
    /// </summary>
    /// <param name="item">The item to test.</param>
    /// <returns><see langword="true"/> when the item satisfies every criterion.</returns>
    public bool Matches(MemoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Source is not null
            && !string.Equals(item.Source, Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Tags is { Count: > 0 })
        {
            foreach (var tag in Tags)
            {
                if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
        }

        if (CustomProperties is { Count: > 0 })
        {
            var props = item.Metadata.CustomProperties;
            if (props is null)
                return false;

            foreach (var (key, value) in CustomProperties)
            {
                if (!props.TryGetValue(key, out var actual)
                    || !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a <see cref="MemoryFilter"/> from the legacy dictionary filter shape:
    /// <c>"source"</c> maps to <see cref="Source"/>, <c>"tag"</c>/<c>"tags"</c> (a string or a
    /// sequence of strings) maps to <see cref="Tags"/>, any other key becomes a
    /// <see cref="CustomProperties"/> entry (value converted via <c>ToString()</c>).
    /// </summary>
    /// <param name="filter">The legacy filter dictionary, possibly <see langword="null"/> or empty.</param>
    /// <returns>The equivalent typed filter, or <see langword="null"/> when the input is null or empty.</returns>
    public static MemoryFilter? FromDictionary(IReadOnlyDictionary<string, object>? filter)
    {
        if (filter is null || filter.Count == 0)
            return null;

        string? source = null;
        var tags = new List<string>();
        var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in filter)
        {
            switch (key.ToUpperInvariant())
            {
                case "SOURCE":
                    source = value?.ToString();
                    break;

                case "TAG" or "TAGS":
                    switch (value)
                    {
                        case string single when !string.IsNullOrWhiteSpace(single):
                            tags.Add(single);
                            break;
                        case IEnumerable<string> many:
                            tags.AddRange(many.Where(t => !string.IsNullOrWhiteSpace(t)));
                            break;
                        default:
                            if (value?.ToString() is { Length: > 0 } fallback)
                                tags.Add(fallback);
                            break;
                    }

                    break;

                default:
                    custom[key] = value?.ToString() ?? string.Empty;
                    break;
            }
        }

        return new MemoryFilter
        {
            Source = source,
            Tags = tags.Count > 0 ? tags : null,
            CustomProperties = custom.Count > 0 ? custom : null
        };
    }

    /// <summary>
    /// Converts this filter back to the legacy dictionary shape for providers that still
    /// consume <c>Dictionary&lt;string, object&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Lossy edge case: the legacy shape carries a single <c>"tags"</c> entry — one tag is
    /// emitted as a plain string (the historical format); multiple tags are emitted as a
    /// <c>string[]</c>, which pre-typed-filter providers may not understand.
    /// </remarks>
    /// <returns>A mutable dictionary in the legacy filter shape (empty when <see cref="IsEmpty"/>).</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (Source is not null)
            result["source"] = Source;

        if (Tags is { Count: > 0 })
            result["tags"] = Tags.Count == 1 ? Tags[0] : Tags.ToArray();

        if (CustomProperties is { Count: > 0 })
        {
            foreach (var (key, value) in CustomProperties)
                result[key] = value;
        }

        return result;
    }
}
