using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The contract hash of a tool (#445): what the caller and the bridge compare before a tool is bridged, since
/// <c>ToolContract.Version</c> is optional and most tools set none. Both sides compute it with
/// <see cref="HashOf"/>. It covers what makes the caller's pipeline treat a call the way the bridge's tool expects: the
/// shape of the input schema (names, types, enumerations, bounds, what is required), the tool's file carrying
/// (<see cref="IBaseTool.UsesFileSystem"/>, <c>ActsOnWorkspace</c>, <c>StaysWithCaller</c>) and the access table. The
/// description strings of the tool and of its parameters are left out: they are prose, and some name what the host
/// holds (a missing credential, a mounted path), which differs between two machines that run the same tool. A
/// parameter that is named <c>description</c> stays in. Object keys are sorted, and so are the values of
/// <c>enum</c> and <c>required</c>, whose order means nothing.
/// </summary>
internal static class ToolBridgeContract
{
    /// <summary>The most combinations of the enumerated parameters' values the access table samples.</summary>
    internal const int MaxCombinations = 256;

    /// <summary>The hash of <paramref name="tool"/>'s contract, as lowercase hexadecimal.</summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>The SHA-256 of its canonical schema, file carrying and access table.</returns>
    public static string HashOf(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalOf(tool))));
    }

    /// <summary>The text the hash is taken of: the canonical schema, then one line per trait and per access sample.</summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>The canonical text.</returns>
    internal static string CanonicalOf(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var schema = JsonSerializer.SerializeToNode(tool.Schema.ToInputJsonSchema());
        var canonical = new StringBuilder();
        canonical.Append(Canonical(schema, inProperties: false, key: null)).Append('\n');
        canonical.Append("uses_file_system=").Append(tool.UsesFileSystem ? "true" : "false").Append('\n');
        canonical.Append("acts_on_workspace=").Append(ToolContracts.ActsOnWorkspace(tool) ? "true" : "false").Append('\n');
        canonical.Append("stays_with_caller=").Append(ToolContracts.StaysWithCaller(tool) ? "true" : "false").Append('\n');
        foreach (var line in AccessTable(tool))
            canonical.Append(line).Append('\n');
        return canonical.ToString();
    }

    /// <summary>
    /// The access table, sampled: the class without arguments, the class of each value of every enumerated top-level
    /// parameter (<c>action: navigate</c>), its values those of <c>EnumMap</c> when set, and, when two parameters or
    /// more are enumerated, the class of each combination of their values, up to <see cref="MaxCombinations"/>.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <returns>One line per sample, in a fixed order.</returns>
    public static IReadOnlyList<string> AccessTable(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var lines = new List<string>
        {
            $"access={tool.Access}",
            $"()={tool.AccessFor(new Dictionary<string, object?>())}",
        };
        var enumerated = new List<(string Name, List<string> Values)>();
        foreach (var (name, parameter) in tool.Schema.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            IEnumerable<string>? raw = parameter.EnumMap is { Count: > 0 } map ? map.Keys
                : parameter.Enum is { Count: > 0 } values ? values.Select(v => v?.ToString() ?? "")
                : null;
            if (raw is null)
                continue;
            var sorted = raw.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            enumerated.Add((name, sorted));
            foreach (var value in sorted)
                lines.Add($"{name}={value}:{tool.AccessFor(new Dictionary<string, object?> { [name] = value })}");
        }

        if (enumerated.Count < 2 || enumerated.Aggregate(1L, (n, e) => Math.Min(n * e.Values.Count, MaxCombinations + 1L)) > MaxCombinations)
            return lines;
        IEnumerable<Dictionary<string, object?>> combinations = [new Dictionary<string, object?>(StringComparer.Ordinal)];
        foreach (var (name, values) in enumerated)
            combinations = combinations.SelectMany(c => values.Select(v => new Dictionary<string, object?>(c, StringComparer.Ordinal) { [name] = v }));
        foreach (var combination in combinations)
            lines.Add(string.Join(",", combination.Select(p => $"{p.Key}={p.Value}")) + ":" + tool.AccessFor(combination));
        return lines;
    }

    // Keys sorted at every level, the description strings dropped (not a parameter named description, a key of a
    // properties map), enum and required sorted, written without white space.
    private static string Canonical(JsonNode? node, bool inProperties, string? key) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj
            .Where(p => inProperties || !(string.Equals(p.Key, "description", StringComparison.Ordinal) && p.Value is JsonValue))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value, !inProperties && IsNameMap(p.Key), p.Key))) + "}",
        JsonArray array when key is "enum" or "required" && !inProperties =>
            "[" + string.Join(",", array.Select(item => Canonical(item, false, null)).Order(StringComparer.Ordinal)) + "]",
        JsonArray array => "[" + string.Join(",", array.Select(item => Canonical(item, false, null))) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    // The keys whose object maps names to schemas: its own keys are names, never keywords.
    private static bool IsNameMap(string key) => key is "properties" or "patternProperties" or "$defs" or "definitions";
}
