using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Crew YAML keys that were removed from the schema. The deserializer ignores unknown keys,
/// so a crew that still writes one would lose it in silence; the loader looks for them in
/// the raw document instead. A key whose value no longer has any meaning draws a warning
/// (GAP-02); a key that promised a behaviour the crew would silently lose is refused (GAP-07).
/// </summary>
internal static class RetiredCrewYamlKeys
{
    /// <summary>
    /// A removed key: its dotted path from the crew document root (<c>*</c> matches any one
    /// key), what replaces it, and whether its presence fails the load.
    /// </summary>
    internal sealed record RetiredKey(string Path, string Guidance, bool Refused = false);

    /// <summary>A removed key found in a document, with the concrete path it was found at.</summary>
    internal sealed record Occurrence(string Path, RetiredKey Key);

    private const string GraphConfigGuidance =
        "the crew-level circuit breaker bounded Graph mode only, where graphConfig carries the same settings — " +
        "on a process: graph crew, write graphConfig: { circuitBreakerPreset, maxTransitions, maxStateVisits, maxTotalDurationSeconds }; " +
        "other modes have no state machine to bound";

    private const string TaskBreakerGuidance =
        "the task state machine it configured never ran — a task is bounded by its agent's maxIter and by output-validation retries; " +
        "Graph-mode limits live in the crew's graphConfig block";

    private static readonly RetiredKey[] s_keys =
    [
        new("rag.provider",
            "the document store is the host's choice — set Orkeon:Rag:Provider in the settings"),
        new("circuitBreaker", GraphConfigGuidance, Refused: true),
        new("tasks.*.circuitBreaker", TaskBreakerGuidance, Refused: true),
    ];

    /// <summary>
    /// The retired keys present in <paramref name="yaml"/>. A document that does not parse
    /// yields none: the deserializer reports it with a better message.
    /// </summary>
    /// <param name="yaml">The raw YAML document.</param>
    /// <param name="documentPath">
    /// Where the document sits in the crew document: empty for a whole crew (or its settings
    /// file), <c>tasks</c> for a flat <c>tasks.yaml</c>, <c>tasks.&lt;name&gt;</c> for one
    /// per-entity task file.
    /// </param>
    public static IReadOnlyList<Occurrence> Find(string yaml, string documentPath = "")
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

        var prefix = documentPath.Length == 0 ? [] : documentPath.Split('.');
        var found = new List<Occurrence>();
        foreach (var key in s_keys)
        {
            var segments = key.Path.Split('.');
            if (!StartsWith(segments, prefix))
                continue;

            foreach (var path in Walk(root, segments.AsSpan(prefix.Length).ToArray(), [.. prefix]))
                found.Add(new Occurrence(path, key));
        }

        return found;
    }

    private static bool StartsWith(string[] segments, string[] prefix)
    {
        if (prefix.Length > segments.Length)
            return false;

        for (var i = 0; i < prefix.Length; i++)
        {
            if (segments[i] != "*" && !string.Equals(segments[i], prefix[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> Walk(YamlNode node, string[] remaining, List<string> walked)
    {
        if (remaining.Length == 0)
        {
            yield return string.Join('.', walked);
            yield break;
        }

        if (node is not YamlMappingNode mapping)
            yield break;

        var segment = remaining[0];
        var rest = remaining[1..];
        if (segment == "*")
        {
            foreach (var child in mapping.Children)
            {
                if (child.Key is not YamlScalarNode { Value: { } name })
                    continue;
                foreach (var path in Walk(child.Value, rest, [.. walked, name]))
                    yield return path;
            }
        }
        else if (mapping.Children.TryGetValue(new YamlScalarNode(segment), out var child))
        {
            foreach (var path in Walk(child, rest, [.. walked, segment]))
                yield return path;
        }
    }
}
