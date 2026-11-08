namespace Orkeon.Infrastructure.MCP;

/// <summary>Applies <see cref="McpToolFilterConfig"/> to the tools a server lists.</summary>
internal static class McpToolFilter
{
    /// <summary>
    /// Keeps the tools <c>Allow</c> admits (all when it is empty) minus those <c>Deny</c> names, and
    /// reports each entry that matched no listed tool as <c>Allow:name</c> or <c>Deny:name</c>.
    /// </summary>
    public static (List<McpToolDefinition> Kept, List<string> Unmatched) Apply(
        IReadOnlyList<McpToolDefinition> tools, McpToolFilterConfig? filter)
    {
        if (filter is null || (filter.Allow.Count == 0 && filter.Deny.Count == 0))
            return ([.. tools], []);

        var listed = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var unmatched = new List<string>();
        unmatched.AddRange(filter.Allow.Where(n => !listed.Contains(n)).Select(n => $"Allow:{n}"));
        unmatched.AddRange(filter.Deny.Where(n => !listed.Contains(n)).Select(n => $"Deny:{n}"));

        var kept = tools
            .Where(t => filter.Allow.Count == 0 || filter.Allow.Contains(t.Name, StringComparer.Ordinal))
            .Where(t => !filter.Deny.Contains(t.Name, StringComparer.Ordinal))
            .ToList();
        return (kept, unmatched);
    }
}
