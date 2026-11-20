namespace Orkeon.Constants.Llm;

/// <summary>
/// The lookup the per-model tables share (<see cref="LlmModelOutputLimits"/>,
/// <see cref="LlmModelContextWindows"/>): an exact id, else the longest registered prefix followed
/// by a separator, comparisons ignoring case.
/// </summary>
internal static class ModelIdTable
{
    /// <summary>The value of <paramref name="id"/> in <paramref name="table"/>, or null when no entry claims it.</summary>
    internal static int? Lookup(IReadOnlyDictionary<string, int> table, string id)
    {
        if (table.TryGetValue(id, out var exact))
            return exact;

        int? best = null;
        var bestLength = 0;
        foreach (var (key, value) in table)
        {
            if (key.Length <= bestLength || key.Length >= id.Length)
                continue;
            if (!id.StartsWith(key, StringComparison.OrdinalIgnoreCase) || !IsSeparator(id[key.Length]))
                continue;

            best = value;
            bestLength = key.Length;
        }

        return best;
    }

    /// <summary>The segment after a vendor slash (<c>google/gemini-3.7-flash</c> reads as <c>gemini-3.7-flash</c>), or null.</summary>
    internal static string? AfterVendorSlash(string id)
    {
        var slash = id.LastIndexOf('/');
        return slash >= 0 && slash < id.Length - 1 ? id[(slash + 1)..] : null;
    }

    // A family prefix only claims an id at a boundary: a date, a size or a routing suffix.
    private static bool IsSeparator(char c) => c is '-' or ':' or '@' or '_';
}
