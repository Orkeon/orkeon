using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Memory;

/// <summary>What the two <see cref="ICategoryMemoryStore"/> implementations share: category normalisation, content checks, ordering.</summary>
internal static class CategoryMemoryEntries
{
    private static readonly HashSet<string> KnownCategories =
        new(StringComparer.OrdinalIgnoreCase) { "user", "project", "feedback", "reference" };

    /// <summary>The ISO-8601 round-trip timestamp of now.</summary>
    public static string Now() => DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Refuses blank content; returns it trimmed.</summary>
    public static string RequireContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("memory content must not be empty.", nameof(content));
        return content.Trim();
    }

    /// <summary>A known category in lower case; any other name trimmed; blank is <c>user</c>.</summary>
    public static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "user";
        var trimmed = category.Trim();
#pragma warning disable CA1308 // lowercase is the required normalized storage/return form for the category key
        return KnownCategories.Contains(trimmed) ? trimmed.ToLowerInvariant() : trimmed;
#pragma warning restore CA1308
    }

    /// <summary>The entries of a category (all when null), in insertion order or newest first (CreatedAt, then id, descending).</summary>
    public static IReadOnlyList<MemoryEntry> Select(IEnumerable<MemoryEntry> entries, string? category, bool newestFirst)
    {
        var query = entries;
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(e => string.Equals(e.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
        if (newestFirst)
            query = query.OrderByDescending(e => e.CreatedAt, StringComparer.Ordinal).ThenByDescending(e => e.Id);
        return query.ToArray();
    }
}
