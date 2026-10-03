using System.Text.Json;

namespace Orkeon.Hosting.Tests;

/// <summary>The JSON of a settings file holding the given keys, each nested along its <c>:</c> path.</summary>
internal static class SettingsJson
{
    public static string Of(IReadOnlyDictionary<string, string> values)
    {
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            var node = root;
            var parts = key.Split(':');
            foreach (var part in parts[..^1])
            {
                if (!node.TryGetValue(part, out var next) || next is not Dictionary<string, object> child)
                {
                    child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    node[part] = child;
                }

                node = child;
            }

            node[parts[^1]] = value;
        }

        return JsonSerializer.Serialize(root);
    }
}
