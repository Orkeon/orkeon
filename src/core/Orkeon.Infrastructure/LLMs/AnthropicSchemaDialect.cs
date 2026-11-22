using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Anthropic's dialect of JSON Schema for <c>output_config.format</c> (issue #459): the API takes a
/// subset of JSON Schema and answers a 400 to the rest. The provider works on a copy of the caller's
/// schema, as Anthropic's own SDK helpers do: it closes every object, moves the constraints the API
/// refuses into the node's <c>description</c>, and refuses before the call what the API refuses and
/// cannot be rewritten without changing what the schema means.
/// </summary>
/// <remarks>
/// Every list was measured against the Messages API (<c>anthropic-version: 2023-06-01</c>,
/// claude-haiku-4-5, 2026-11-22). The API checks a keyword against the node's type: an object node
/// refuses any keyword off its list, an array node refuses <c>contains</c> and <c>prefixItems</c>,
/// while a string, number or <c>allOf</c> node takes <c>if</c>/<c>then</c>/<c>else</c> and other
/// keywords without enforcing them. The answer is still validated against the original schema by the
/// callers that validate one (<c>OutputValidationCoordinator</c>), so a moved constraint is checked
/// after the call.
/// </remarks>
internal static class AnthropicSchemaDialect
{
    /// <summary>The error type of a request the provider refuses before sending it.</summary>
    public const string RefusedErrorType = "UnsupportedResponseSchema";

    /// <summary>Keywords the API refuses, moved into the description whatever their value.</summary>
    private static readonly string[] MovedConstraints =
    [
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "maxItems", "uniqueItems",
    ];

    /// <summary>The string formats the API takes; any other one is moved into the description.</summary>
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.Ordinal)
    {
        "date-time", "time", "date", "duration", "email", "hostname", "uri", "ipv4", "ipv6", "uuid",
    };

    /// <summary>The keywords whose value is a map of name to sub-schema.</summary>
    private static readonly string[] SchemaMaps = ["properties", "$defs", "definitions"];

    /// <summary>The keywords whose value is a list of sub-schemas.</summary>
    private static readonly string[] SchemaLists = ["anyOf", "allOf"];

    /// <summary>Keywords the API refuses on an object node (it takes none off its own list).</summary>
    private static readonly string[] ObjectRefusedKeywords =
    [
        "if", "then", "else", "patternProperties", "dependentSchemas", "dependencies", "contains",
        "propertyNames", "unevaluatedProperties", "unevaluatedItems", "additionalItems",
    ];

    /// <summary>Keywords the API refuses on an array node.</summary>
    private static readonly string[] ArrayRefusedKeywords = ["contains", "prefixItems"];

    /// <summary>Keywords the API refuses on an <c>anyOf</c> node.</summary>
    private static readonly string[] AnyOfRefusedKeywords = ["if", "then", "else"];

    /// <summary>The keywords that give a node its type; the API refuses a node with none of them.</summary>
    private static readonly string[] TypingKeywords = ["type", "anyOf", "allOf", "$ref", "const", "enum"];

    /// <summary>The annotations the API takes beside a <c>$ref</c>; any other sibling is refused.</summary>
    private static readonly HashSet<string> RefSiblings = new(StringComparer.Ordinal)
    {
        "$ref", "description", "title", "default", "examples", "$comment", "deprecated", "readOnly", "writeOnly",
    };

    /// <summary>The adapted copy, or the reason the schema is refused.</summary>
    internal sealed record Adaptation(JsonNode? Schema, string? Refusal);

    /// <summary>Adapts <paramref name="schemaJson"/> to the dialect, or names why it cannot be sent.</summary>
    public static Adaptation Adapt(string schemaName, string schemaJson)
    {
        var root = JsonNode.Parse(schemaJson);
        if (root is not JsonObject rootObject)
        {
            return new Adaptation(null, $"Anthropic cannot take the response schema '{schemaName}': "
                + "the root must be a JSON object, not a boolean schema or a bare value.");
        }

        var problem = FindRecursion(rootObject) ?? Walk(rootObject, "#");
        return problem is null
            ? new Adaptation(rootObject, null)
            : new Adaptation(null, $"Anthropic cannot take the response schema '{schemaName}': {problem}");
    }

    /// <summary>The refusal of a request that ends on an assistant turn while a JSON output is asked.</summary>
    public static string PrefillRefusal(string schemaName) =>
        $"Anthropic cannot take the response schema '{schemaName}' on messages that end on an assistant turn: "
        + "prefilling is incompatible with JSON outputs. End the messages on a user turn.";

    private static string? Walk(JsonObject node, string path)
    {
        if (node.Count == 0)
            return $"the empty schema `{{}}` at `{path}` accepts any value and is not supported; name a `type`.";
        if (node.ContainsKey("oneOf"))
            return $"`oneOf` at `{path}` is not supported; use `anyOf`.";
        if (node.ContainsKey("not"))
            return $"`not` at `{path}` is not supported.";
        if (node.TryGetPropertyValue("additionalProperties", out var additional)
            && additional?.GetValueKind() != JsonValueKind.False)
        {
            return $"`additionalProperties` at `{path}` must be `false`: an object lists every key it may hold "
                + "(use an array of key and value items for arbitrary keys).";
        }

        if (node.TryGetPropertyValue("$ref", out var reference)
            && reference?.GetValueKind() == JsonValueKind.String
            && !reference.GetValue<string>().StartsWith('#'))
        {
            return $"external `$ref` at `{path}` ('{reference.GetValue<string>()}') is not supported; "
                + "inline the definition under `$defs`.";
        }

        if (RefusedKeyword(node) is { } refused)
            return $"`{refused.Keyword}` at `{path}` is not supported on {refused.Kind} node.";

        if (!TypingKeywords.Any(node.ContainsKey))
            return $"the schema at `{path}` names no `type` (nor `anyOf`, `allOf`, `$ref`, `const` or `enum`).";

        if (node.TryGetPropertyValue("description", out var description)
            && description?.GetValueKind() != JsonValueKind.String)
        {
            return $"`description` at `{path}` must be a string.";
        }

        if (node["items"] is { } items && items is not JsonObject)
        {
            return $"`items` at `{path}` must be one schema object: a tuple or a boolean `items` is not supported.";
        }

        if (IsObjectNode(node) && !node.ContainsKey("additionalProperties"))
        {
            // Closing an object that lists no key would leave the model nothing to answer but `{}`.
            if (node["properties"] is not JsonObject { Count: > 0 })
            {
                return $"the object at `{path}` declares no `properties`: closing it would drop every key. "
                    + "List the keys it may hold, or set `additionalProperties: false` to mean an empty object "
                    + "(use an array of key and value items for arbitrary keys).";
            }

            node["additionalProperties"] = false;
        }

        MoveUnsupportedConstraints(node);

        // After the move: a constraint written into the description is no longer a sibling.
        if (node.ContainsKey("$ref") && node.Select(p => p.Key).FirstOrDefault(k => !RefSiblings.Contains(k)) is { } sibling)
        {
            return $"`$ref` at `{path}` cannot be combined with `{sibling}`: move it into the referenced "
                + "definition, or inline the definition in place of the `$ref`.";
        }

        return WalkChildren(node, path);
    }

    /// <summary>The first keyword the API refuses on a node of this kind, with the kind's name.</summary>
    private static (string Keyword, string Kind)? RefusedKeyword(JsonObject node)
    {
        var (keywords, kind) = IsObjectNode(node) ? (ObjectRefusedKeywords, "an object")
            : HasType(node, "array") ? (ArrayRefusedKeywords, "an array")
            : node.ContainsKey("anyOf") ? (AnyOfRefusedKeywords, "an `anyOf`")
            : (null, null);
        if (keywords is null)
            return null;
        var keyword = Array.Find(keywords, node.ContainsKey);
        return keyword is null ? null : (keyword, kind!);
    }

    private static string? WalkChildren(JsonObject node, string path)
    {
        foreach (var map in SchemaMaps)
        {
            if (node[map] is not JsonObject children)
                continue;
            foreach (var (name, child) in children)
            {
                if (WalkSchema(child, $"{path}/{map}/{Escape(name)}") is { } problem)
                    return problem;
            }
        }

        foreach (var list in SchemaLists)
        {
            if (node[list] is not JsonArray items)
                continue;
            for (var i = 0; i < items.Count; i++)
            {
                if (WalkSchema(items[i], $"{path}/{list}/{i}") is { } problem)
                    return problem;
            }
        }

        // Walk refused an `items` that is not one schema object before this point.
        return node["items"] is JsonObject single ? Walk(single, $"{path}/items") : null;
    }

    /// <summary>Walks a sub-schema; the API takes no boolean or bare value where a schema is expected.</summary>
    private static string? WalkSchema(JsonNode? child, string path) => child is JsonObject schema
        ? Walk(schema, path)
        : $"the schema at `{path}` must be a JSON object: a boolean schema is not supported.";

    private static bool IsObjectNode(JsonObject node) => node.ContainsKey("properties") || HasType(node, "object");

    private static bool HasType(JsonObject node, string name) => node["type"] switch
    {
        JsonValue type when type.GetValueKind() == JsonValueKind.String => type.GetValue<string>() == name,
        JsonArray types => types.Any(t => t?.GetValueKind() == JsonValueKind.String && t.GetValue<string>() == name),
        _ => false,
    };

    private static void MoveUnsupportedConstraints(JsonObject node)
    {
        // `uniqueItems: false` constrains nothing: it is dropped rather than written into the description.
        if (node["uniqueItems"] is JsonValue unique && unique.GetValueKind() == JsonValueKind.False)
            node.Remove("uniqueItems");

        // In the node's own order, so the description reads the constraints as the author wrote them.
        var keys = node.Where(p => IsUnsupportedConstraint(p.Key, p.Value)).Select(p => p.Key).ToList();
        if (keys.Count == 0)
            return;

        var moved = new List<string>(keys.Count);
        foreach (var key in keys)
            TakeInto(node, key, moved);

        if (moved.Count == 0)
            return;

        // The SDK helpers' shape, so the model still reads the constraint: "{minimum: 100}".
        var note = "{" + string.Join(", ", moved) + "}";
        // Walk refused a description that is not a string before this point.
        node["description"] = node["description"] is JsonValue d && d.GetValue<string>() is { Length: > 0 } existing
            ? existing + "\n\n" + note
            : note;
    }

    private static bool IsUnsupportedConstraint(string key, JsonNode? value) => key switch
    {
        // Only 0 and 1 are taken for minItems.
        // A number too large for a double (1e400) is above 1 all the same.
        "minItems" => value is JsonValue n && n.GetValueKind() == JsonValueKind.Number
            && (!n.TryGetValue<double>(out var count) || count > 1),
        "format" => value is JsonValue f && f.GetValueKind() == JsonValueKind.String
            && !SupportedFormats.Contains(f.GetValue<string>()),
        _ => Array.IndexOf(MovedConstraints, key) >= 0,
    };

    private static void TakeInto(JsonObject node, string key, List<string> moved)
    {
        if (!node.TryGetPropertyValue(key, out var value))
            return;
        node.Remove(key);
        var text = value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String
            ? scalar.GetValue<string>()
            : value?.ToJsonString() ?? "null";
        moved.Add(string.Create(CultureInfo.InvariantCulture, $"{key}: {text}"));
    }

    /// <summary>
    /// Follows every local <c>$ref</c> from the root; a reference to a node already on the path is a
    /// recursive schema, which the API does not take.
    /// </summary>
    private static string? FindRecursion(JsonObject root)
    {
        var walk = new RefWalk(root, new HashSet<string>(StringComparer.Ordinal) { "#" }, new HashSet<string>(StringComparer.Ordinal));
        return FindRecursion(root, walk, "#", depth: 0);
    }

    /// <summary>The root, the references on the current path, and those already walked through whole.</summary>
    private sealed record RefWalk(JsonObject Root, HashSet<string> OnPath, HashSet<string> Done);

    private static string? FindRecursion(JsonNode? node, RefWalk walk, string path, int depth)
    {
        if (depth > 256)
            return null;

        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (FindRecursion(array[i], walk, $"{path}/{i}", depth + 1) is { } found)
                        return found;
                }
                return null;

            case JsonObject obj:
                if (obj["$ref"] is JsonValue r && r.GetValueKind() == JsonValueKind.String
                    && r.GetValue<string>() is { } target && target.StartsWith('#'))
                {
                    if (walk.OnPath.Contains(target))
                        return $"recursive `$ref` at `{path}` ('{target}'): recursive schemas are not supported.";
                    // A definition reached twice is walked once: a schema that reuses one is not recursive.
                    if (!walk.Done.Contains(target) && Resolve(walk.Root, target) is { } resolved)
                    {
                        walk.OnPath.Add(target);
                        var found = FindRecursion(resolved, walk, target, depth + 1);
                        walk.OnPath.Remove(target);
                        if (found is not null)
                            return found;
                        walk.Done.Add(target);
                    }
                }

                foreach (var (name, child) in obj)
                {
                    // Definitions are reached through the references that use them, with the path they
                    // are named by, so a definition nothing uses is not walked here.
                    if (path == "#" && name is "$defs" or "definitions")
                        continue;
                    if (FindRecursion(child, walk, $"{path}/{Escape(name)}", depth + 1) is { } found)
                        return found;
                }
                return null;

            default:
                return null;
        }
    }

    private static JsonNode? Resolve(JsonObject root, string pointer)
    {
        JsonNode? current = root;
        foreach (var raw in pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current switch
            {
                JsonObject obj => obj[segment],
                JsonArray array when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                    && i < array.Count => array[i],
                _ => null,
            };
            if (current is null)
                return null;
        }

        return current;
    }

    private static string Escape(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(c switch
            {
                '~' => "~0",
                '/' => "~1",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }
}
