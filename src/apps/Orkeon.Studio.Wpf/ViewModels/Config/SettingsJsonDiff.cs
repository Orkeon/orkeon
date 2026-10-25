using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// Counts what differs between two states of the settings document (lot 5, the unsaved bar):
/// one per value added, removed or changed, an array compared as one value - « 2 unsaved
/// changes » reads the fields the user touched, not the edits made to them.
/// </summary>
public static class SettingsJsonDiff
{
    /// <summary>The number of values that differ; an unreadable side counts as one change.</summary>
    public static int Count(string? saved, string? current)
    {
        if (string.Equals(saved, current, StringComparison.Ordinal))
            return 0;

        try
        {
            return Count(Parse(saved), Parse(current));
        }
        catch (JsonException)
        {
            return 1;
        }
    }

    private static JsonNode? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);

    private static int Count(JsonNode? left, JsonNode? right)
    {
        if (left is JsonObject l && right is JsonObject r)
        {
            var count = 0;
            foreach (var key in l.Select(p => p.Key).Union(r.Select(p => p.Key), StringComparer.Ordinal))
            {
                l.TryGetPropertyValue(key, out var a);
                r.TryGetPropertyValue(key, out var b);
                count += Count(a, b);
            }

            return count;
        }

        if (left is JsonObject only)
            return right is null ? Leaves(only) : 1;
        if (right is JsonObject added)
            return left is null ? Leaves(added) : 1;

        return JsonNode.DeepEquals(left, right) ? 0 : 1;
    }

    // An object added or removed whole counts its values: a new section of three fields is three changes.
    private static int Leaves(JsonObject node) =>
        Math.Max(1, node.Sum(p => p.Value is JsonObject child ? Leaves(child) : 1));
}
