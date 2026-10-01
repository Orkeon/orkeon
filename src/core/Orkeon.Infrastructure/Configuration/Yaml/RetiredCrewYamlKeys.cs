using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Crew YAML keys that were removed from the schema. The deserializer ignores unknown keys,
/// so a crew that still writes one would lose it in silence; the loader looks for them in
/// the raw document and warns instead (GAP-02).
/// </summary>
internal static class RetiredCrewYamlKeys
{
    /// <summary>A removed key: its dotted path from the document root, and what replaces it.</summary>
    internal sealed record RetiredKey(string Path, string Guidance);

    private static readonly RetiredKey[] s_keys =
    [
        new("rag.provider",
            "the document store is the host's choice — set Orkeon:Rag:Provider in the settings"),
    ];

    /// <summary>
    /// The retired keys present in <paramref name="yaml"/>. A document that does not parse
    /// yields none: the deserializer reports it with a better message.
    /// </summary>
    public static IReadOnlyList<RetiredKey> Find(string yaml)
    {
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlException)
        {
            return [];
        }

        if (root is null)
            return [];

        return s_keys.Where(key => IsPresent(root, key.Path.Split('.'))).ToList();
    }

    private static bool IsPresent(YamlMappingNode root, string[] segments)
    {
        YamlNode node = root;
        foreach (var segment in segments)
        {
            if (node is not YamlMappingNode mapping
                || !mapping.Children.TryGetValue(new YamlScalarNode(segment), out var child))
            {
                return false;
            }

            node = child;
        }

        return true;
    }
}
