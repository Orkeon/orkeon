using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The contract hash of a tool (#445): what the caller and the bridge compare before a tool is bridged, since
/// <c>ToolContract.Version</c> is optional and most tools set none. It covers what makes the caller's pipeline
/// treat a call the way the bridge's tool expects: the shape of the input schema (names, types, enumerations,
/// bounds, what is required) and the access table. The descriptions are left out: they are prose, and some
/// name what the host holds (a missing credential, a mounted path), which differs between two machines that
/// run the same tool.
/// </summary>
internal static class ToolBridgeContract
{
    /// <summary>The hash of <paramref name="tool"/>'s contract, as lowercase hexadecimal.</summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>The SHA-256 of its canonical schema and access table.</returns>
    public static string HashOf(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var schema = JsonSerializer.SerializeToNode(tool.Schema.ToInputJsonSchema());
        var canonical = new StringBuilder();
        canonical.Append(Canonical(schema));
        canonical.Append('\n');
        foreach (var line in AccessTable(tool))
            canonical.Append(line).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>
    /// The access table, sampled: the class without arguments and the class of each value of every enumerated
    /// top-level parameter (<c>action: navigate</c>), so a tool whose class depends on the operation asked compares
    /// its per-operation classes.
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
        foreach (var (name, parameter) in tool.Schema.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (parameter.Enum is not { Count: > 0 } values)
                continue;
            foreach (var value in values.Select(v => v?.ToString() ?? "").Order(StringComparer.Ordinal))
                lines.Add($"{name}={value}:{tool.AccessFor(new Dictionary<string, object?> { [name] = value })}");
        }

        return lines;
    }

    // Keys sorted at every level, the descriptions dropped, written without white space.
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj
            .Where(p => !string.Equals(p.Key, "description", StringComparison.Ordinal))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };
}
